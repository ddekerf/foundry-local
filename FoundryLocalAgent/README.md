# Foundry Local + Microsoft Agent Framework

A small console app showing a **Microsoft Agent Framework** agent (with C# tools and multi-turn memory)
running on a model served by **Foundry Local**. It's fully on-device: no cloud, no API key.

## How they fit together

```
AIAgent (Microsoft.Agents.AI)
   │  instructions, tools, session/history, streaming
   ▼
OpenAI.Chat.ChatClient  ──HTTP──►  Foundry Local web service  http://127.0.0.1:<port>/v1
                                        │
                                        ▼
                                  local ONNX model (CPU / GPU / NPU)
```

Agent Framework works with any `IChatClient` or OpenAI-compatible endpoint. Foundry Local *is* an
OpenAI-compatible endpoint, so you connect them like this:

1. Start Foundry Local in-process with `Configuration.Web` set, load a model, call `StartWebServiceAsync()`.
2. Point an `OpenAIClient` at `manager.Urls[0] + "/v1"`. The API key can be any non-empty string.
3. Call `chatClient.AsAIAgent(instructions, name, tools: [...])` (from `Microsoft.Agents.AI.OpenAI`).
4. Call `agent.RunAsync(prompt, session)` / `agent.RunStreamingAsync(prompt, session)`.

The rest of your Agent Framework code is the same as with Azure OpenAI or OpenAI. To switch between
local and cloud, you only change the endpoint, key and model id.

## What the sample does

- Lists catalog models that support **tool calling** (`--list`). Agents with tools need such a model.
- Loads `qwen2.5-1.5b` by default, or any alias you pass.
- Creates an agent with two C# tools: `GetCurrentTime` and `GetWeather` (fake, offline data).
  Tool calls are printed as `[tool] ...`.
- Runs a scripted conversation in one `AgentSession`, so follow-up questions ("And in Paris?") have context.
- Continues with an interactive streaming chat in the same session. Type `/new` to start a fresh session.

## Run it

```powershell
cd FoundryLocalAgent
dotnet run                    # qwen2.5-1.5b
dotnet run -- phi-4-mini      # better reasoning/tool use, ~4.9 GB
dotnet run -- --list          # models with tool-calling support
```

It shares its model cache with `../FoundryLocalSample` (same `AppName`).

## Packages

| Package | Why |
|---|---|
| `Microsoft.AI.Foundry.Local` | Runs the model locally and serves the OpenAI-compatible endpoint |
| `Microsoft.Agents.AI.OpenAI` | Agent Framework, plus `AsAIAgent()` on the OpenAI `ChatClient` (pulls in `Microsoft.Agents.AI` and `OpenAI`) |
| `Microsoft.Extensions.Logging.Console` | Logging |

## Tips

- **Model choice matters most.** Small models (0.5B–1.5B) can call tools but sometimes skip them or
  get arguments wrong. For real agent work use `phi-4-mini`, `qwen2.5-7b`, `qwen3-4b`/`qwen3-8b` or
  `mistral-nemo-12b-instruct`, and enable GPU/NPU acceleration (see `--eps` in FoundryLocalSample).
  In testing, `qwen2.5-1.5b` called the tools correctly, but once it had answered "I don't have that
  information", it kept saying that for later questions in the same session. `/new` resets it.
- **Everything else in Agent Framework works too**, because it all sits on top of the chat client:
  workflows (sequential, concurrent, handoff, group chat), agents-as-tools (`agent.AsAIFunction()`),
  middleware, MCP tools, OpenTelemetry, etc.
- In a longer-running app (or ASP.NET Core) create the `FoundryLocalManager` once at startup and
  register the `ChatClient` / `IChatClient` / `AIAgent` in DI.
- If the Foundry Local CLI service is already running (`foundry server status`), you can skip the
  in-process web service and point the `OpenAIClient` at that URL instead.
