// Foundry Local – basic .NET sample
//
// Shows the core workflow of the Microsoft.AI.Foundry.Local SDK (v2.x):
//   1. Initialize the FoundryLocalManager singleton
//   2. Browse the model catalog
//   3. Download (cached after first run) and load a model
//   4. Chat with it in-process via ChatSession (single answer + streaming chat)
//   5. Optionally expose the model as an OpenAI-compatible REST endpoint and call it
//      with the official OpenAI .NET SDK (run with --web)
//
// Usage:
//   dotnet run                         -> uses qwen2.5-0.5b, then interactive chat
//   dotnet run -- phi-3.5-mini         -> pick another model alias
//   dotnet run -- --list               -> list the catalog and exit
//   dotnet run -- --web                -> also demo the OpenAI-compatible web service
//   dotnet run -- --eps                -> download hardware execution providers (GPU/NPU) first

using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.Text;

var listOnly = args.Contains("--list");
var useWeb = args.Contains("--web");
var useEps = args.Contains("--eps");
var modelAlias = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "qwen2.5-0.5b";

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// 1. Initialize the manager (a process-wide singleton)
// ---------------------------------------------------------------------------
using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => o.SingleLine = true)
    .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));

var config = new Configuration
{
    AppName = "foundry_local_sample",                 // data/cache go to ~/.foundry_local_sample
    LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
    // Only needed for the --web demo. Port 0 = let the OS pick a free port.
    Web = useWeb ? new Configuration.WebService { Urls = "http://127.0.0.1:0" } : null,
};

await FoundryLocalManager.CreateAsync(config, loggerFactory.CreateLogger("FoundryLocal"));
var manager = FoundryLocalManager.Instance;

try
{
    // Optional: download/register hardware-accelerated execution providers (WinML on Windows).
    // Without this, models run on CPU. EPs are large and cached after the first download.
    if (useEps)
    {
        Header("Execution providers");
        foreach (var ep in manager.DiscoverEps())
            Console.WriteLine($"  {ep.Name,-40} registered: {ep.IsRegistered}");

        var epResult = await manager.DownloadAndRegisterEpsAsync((name, pct) =>
            Console.Write($"\r  {name,-40} {pct,6:F1}%"));
        Console.WriteLine($"\n  Success: {epResult.Success}, Status: {epResult.Status}");
    }

    // -----------------------------------------------------------------------
    // 2. Browse the catalog
    // -----------------------------------------------------------------------
    var catalog = await manager.GetCatalogAsync();

    if (listOnly)
    {
        Header("Model catalog");
        var cachedIds = (await catalog.GetCachedModelsAsync()).Select(m => m.Id).ToHashSet();
        foreach (var m in (await catalog.ListModelsAsync()).OrderBy(m => m.Alias))
        {
            var cached = cachedIds.Contains(m.Id) ? "  (cached)" : "";
            Console.WriteLine($"  {m.Alias,-32} {m.Id}{cached}");
        }
        return;
    }

    var model = await catalog.GetModelAsync(modelAlias)
        ?? throw new InvalidOperationException(
            $"Model '{modelAlias}' not found. Run 'dotnet run -- --list' to see available aliases.");

    // -----------------------------------------------------------------------
    // 3. Download (no-op when already cached) and load the model
    // -----------------------------------------------------------------------
    Header($"Model: {model.Alias}");
    Console.WriteLine($"  Selected variant: {model.Id}");
    foreach (var v in model.Variants)
        Console.WriteLine($"    - {v.Id} (cached: {await v.IsCachedAsync()})");

    Console.WriteLine("  Downloading (skipped if cached)...");
    await model.DownloadAsync(p => Console.Write($"\r  Download: {p,6:F1}%"));
    Console.WriteLine();

    Console.WriteLine("  Loading into memory...");
    await model.LoadAsync();
    Console.WriteLine("  Loaded.");

    try
    {
        // Generation settings shared by the sessions below.
        var options = new RequestOptions
        {
            Search = new SearchOptions { Temperature = 0.7f, MaxOutputTokens = 512 },
        };

        // -------------------------------------------------------------------
        // 4a. Single (non-streaming) completion
        // -------------------------------------------------------------------
        Header("Single completion");
        const string question = "In one sentence: why run a language model locally?";
        Console.WriteLine($"> {question}");

        using (var oneShot = new ChatSession(model))
        {
            oneShot.SetOptions(options);

            using var request = new Request()
                .AddItem(MessageItem.System("You are a concise, helpful assistant."))
                .AddItem(MessageItem.User(question));

            using var response = await oneShot.ProcessRequestAsync(request);
            foreach (var item in response)
            {
                if (item is MessageItem message)
                    Console.WriteLine(message.GetSimpleText());
            }
            Console.WriteLine($"  [finish reason: {response.FinishReason}]");
        }

        // -------------------------------------------------------------------
        // 5. Optional: OpenAI-compatible REST endpoint + official OpenAI SDK
        // -------------------------------------------------------------------
        if (useWeb)
            await RunWebServiceDemoAsync(manager, model.Id);

        // -------------------------------------------------------------------
        // 4b. Interactive streaming chat.
        //     ChatSession is stateful: it remembers earlier turns, so each
        //     request only carries the NEW user message.
        // -------------------------------------------------------------------
        Header("Interactive chat (streaming) – empty line or 'exit' to quit");

        using var chat = new ChatSession(model);
        chat.SetOptions(options).SetStreaming(true);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var firstTurn = true;
        while (!cts.IsCancellationRequested)
        {
            Console.Write("\nYou: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            using var request = new Request();
            if (firstTurn)
                request.AddItem(MessageItem.System("You are a friendly, helpful assistant. Keep answers short."));
            request.AddItem(MessageItem.User(input));
            firstTurn = false;

            Console.Write("AI:  ");
            try
            {
                await using var stream = chat.ProcessStreamingRequestAsync(request, cts.Token);
                await foreach (var item in stream)
                {
                    // Ordinary tokens arrive as TextItem(Default); reasoning models also emit TextItem(Reasoning).
                    if (item is TextItem { Type: TextItemType.Default } text)
                        Console.Write(text.Text);
                }
                using var final = await stream.FinalResponse;
                Console.WriteLine($"\n  [finish reason: {final.FinishReason}, turns so far: {chat.TurnCount}]");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(" [cancelled]");
                break;
            }
        }
    }
    finally
    {
        Console.WriteLine("\nUnloading model...");
        await model.UnloadAsync();
    }
}
finally
{
    manager.Dispose();
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static async Task RunWebServiceDemoAsync(FoundryLocalManager manager, string modelId)
{
    Header("OpenAI-compatible web service");
    await manager.StartWebServiceAsync();
    try
    {
        var baseUrl = manager.Urls!.First().TrimEnd('/');
        Console.WriteLine($"  Listening on {baseUrl}  (any OpenAI-compatible client can use {baseUrl}/v1)");

        // The local endpoint doesn't check the key, but the OpenAI SDK requires a non-empty one.
        var client = new OpenAIClient(
            new ApiKeyCredential("notneeded"),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl + "/v1") });

        var chat = client.GetChatClient(modelId);
        const string prompt = "Name three benefits of running an LLM locally. Bullet points, no intro.";
        Console.WriteLine($"> {prompt}");

        foreach (var update in chat.CompleteChatStreaming(prompt))
            foreach (var part in update.ContentUpdate)
                Console.Write(part.Text);
        Console.WriteLine();
    }
    finally
    {
        await manager.StopWebServiceAsync();
    }
}

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
}
