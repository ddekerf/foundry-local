// Foundry Local + Microsoft Agent Framework – basic .NET sample
//
// Foundry Local runs the model on this machine and exposes it as an OpenAI-compatible endpoint.
// Microsoft Agent Framework (Microsoft.Agents.AI) builds an agent on top of that endpoint:
// instructions, C# function tools, multi-turn sessions and streaming. Everything stays on-device.
//
//   Agent Framework (AIAgent) --> OpenAI ChatClient --> http://127.0.0.1:<port>/v1 --> Foundry Local --> local model
//
// Usage:
//   dotnet run                    -> qwen2.5-1.5b, scripted demo, then interactive chat
//   dotnet run -- phi-4-mini      -> use another model (it must support tool calling)
//   dotnet run -- --list          -> list catalog models that support tool calling

using System.ClientModel;
using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

var listOnly = args.Contains("--list");
var modelAlias = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "qwen2.5-1.5b";
Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// 1. Start Foundry Local with its OpenAI-compatible web service enabled
// ---------------------------------------------------------------------------
using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => o.SingleLine = true)
    .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));

await FoundryLocalManager.CreateAsync(
    new Configuration
    {
        AppName = "foundry_local_sample",   // same app name as FoundryLocalSample, so they share the model cache
        LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
        Web = new Configuration.WebService { Urls = "http://127.0.0.1:0" }, // 0 = any free port
    },
    loggerFactory.CreateLogger("FoundryLocal"));

var manager = FoundryLocalManager.Instance;

try
{
    var catalog = await manager.GetCatalogAsync();

    if (listOnly)
    {
        Console.WriteLine("Models with tool-calling support (needed for agents with tools):");
        foreach (var m in (await catalog.ListModelsAsync()).OrderBy(m => m.Alias))
        {
            var info = m.Info;
            if (info.SupportsToolCalling == true)
                Console.WriteLine($"  {m.Alias,-30} {info.FileSizeMb,7} MB   {info.Id}");
        }
        return;
    }

    // -----------------------------------------------------------------------
    // 2. Download + load the model, then start the local endpoint
    // -----------------------------------------------------------------------
    var model = await catalog.GetModelAsync(modelAlias)
        ?? throw new InvalidOperationException($"Model '{modelAlias}' not found. Try 'dotnet run -- --list'.");

    if (model.Info.SupportsToolCalling != true)
        Console.WriteLine($"Warning: '{modelAlias}' is not marked as supporting tool calling; tools may be ignored.");

    Console.WriteLine($"Model: {model.Id}");
    await model.DownloadAsync(p => Console.Write($"\r  Download: {p,6:F1}%"));
    Console.WriteLine();
    await model.LoadAsync();
    await manager.StartWebServiceAsync();

    var endpoint = new Uri(manager.Urls!.First().TrimEnd('/') + "/v1");
    Console.WriteLine($"Foundry Local endpoint: {endpoint}");

    try
    {
        // -------------------------------------------------------------------
        // 3. Build the agent: OpenAI ChatClient (pointed at Foundry Local) -> AIAgent
        // -------------------------------------------------------------------
        ChatClient chatClient = new OpenAIClient(
                new ApiKeyCredential("notneeded"),                       // local endpoint ignores the key
                new OpenAIClientOptions { Endpoint = endpoint })
            .GetChatClient(model.Id);

        AIAgent agent = chatClient.AsAIAgent(
            name: "LocalAssistant",
            instructions: """
                You are a helpful assistant running fully offline on the user's PC.
                Use the available tools whenever a question needs the current time or the weather.
                Keep answers short.
                """,
            tools:
            [
                AIFunctionFactory.Create(Tools.GetCurrentTime),
                AIFunctionFactory.Create(Tools.GetWeather),
            ]);

        // A session keeps the conversation history, so follow-up questions have context.
        AgentSession session = await agent.CreateSessionAsync();

        // -------------------------------------------------------------------
        // 4. Scripted demo: tool calls + multi-turn memory
        // -------------------------------------------------------------------
        string[] demo =
        [
            "What time is it right now?",
            "What's the weather like in Amsterdam?",
            "And in Paris? Which of the two cities is warmer?",
        ];

        foreach (var prompt in demo)
        {
            Console.WriteLine($"\nYou: {prompt}");
            AgentResponse response = await agent.RunAsync(prompt, session);
            Console.WriteLine($"Agent: {response.Text}");
        }

        // -------------------------------------------------------------------
        // 5. Interactive streaming chat in the same session
        // -------------------------------------------------------------------
        Console.WriteLine("\n=== Interactive chat (streaming) – '/new' resets the session, empty line or 'exit' quits ===");
        while (true)
        {
            Console.Write("\nYou: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (input.Trim() == "/new")
            {
                session = await agent.CreateSessionAsync();   // forget the conversation so far
                Console.WriteLine("(new session started)");
                continue;
            }

            Console.Write("Agent: ");
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(input, session))
                Console.Write(update.Text);
            Console.WriteLine();
        }
    }
    finally
    {
        await manager.StopWebServiceAsync();
        await model.UnloadAsync();
    }
}
finally
{
    manager.Dispose();
}

// ---------------------------------------------------------------------------
// Tools the agent can call. [Description] text is sent to the model as the tool/parameter docs.
// ---------------------------------------------------------------------------
static class Tools
{
    [Description("Gets the current local date and time.")]
    public static string GetCurrentTime()
    {
        Log($"GetCurrentTime()");
        return DateTime.Now.ToString("dddd yyyy-MM-dd HH:mm");
    }

    [Description("Gets the current weather for a city.")]
    public static string GetWeather([Description("The city name, e.g. 'Amsterdam'.")] string city)
    {
        Log($"GetWeather(\"{city}\")");
        // Fake but deterministic data so the sample works offline.
        var seed = city.Trim().ToLowerInvariant().Aggregate(0, (h, c) => h * 31 + c);
        var rnd = new Random(seed);
        string[] conditions = ["sunny", "cloudy", "rainy", "windy", "foggy"];
        return $"{city}: {conditions[rnd.Next(conditions.Length)]}, {rnd.Next(-2, 24)} °C";
    }

    static void Log(string call)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [tool] {call}");
        Console.ForegroundColor = old;
    }
}
