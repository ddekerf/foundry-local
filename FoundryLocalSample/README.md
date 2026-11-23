# Foundry Local – basic .NET sample

A small console app that shows the core workflow of [Foundry Local](https://www.foundrylocal.ai/):
running open-source LLMs **entirely on your own machine**, with no cloud, no API key, and no data
leaving the device.

## What is Foundry Local?

Foundry Local is Microsoft's on-device AI runtime. It has:

- **Native SDKs** (C#, Python, JS, C++, Rust). Your app downloads, caches, loads and calls
  optimized ONNX models **in-process**. End users don't need anything else installed.
- **A model catalog** of curated models (Phi, Qwen, Mistral, DeepSeek-R1, gpt-oss, Whisper, …),
  each in hardware-specific variants (CPU / GPU / NPU). The SDK picks the best one for the device.
- **Hardware acceleration** through ONNX Runtime execution providers (EPs). On Windows these come
  from WinML, which is bundled in the NuGet package and downloads EPs on demand.
- **An optional OpenAI-compatible REST server** (`/v1/chat/completions`, `/v1/models`), so existing
  OpenAI clients and tools can talk to the local model.
- **A CLI** (`winget install Microsoft.FoundryLocal`) for trying models out: `foundry model list`,
  `foundry run qwen2.5-0.5b`, `foundry server status`. **This sample doesn't need the CLI.**

## What the sample does

| Step | API |
|---|---|
| 1. Initialize the SDK | `FoundryLocalManager.CreateAsync(config, logger)` → `FoundryLocalManager.Instance` |
| 2. Browse the catalog | `GetCatalogAsync()`, `ListModelsAsync()`, `GetCachedModelsAsync()`, `GetModelAsync(alias)` |
| 3. Download and load | `model.DownloadAsync(progress)`, `model.LoadAsync()` |
| 4a. One-shot answer | `new ChatSession(model)` + `Request` / `MessageItem` → `ProcessRequestAsync` |
| 4b. Streaming chat | `chat.SetStreaming(true)` → `ProcessStreamingRequestAsync`, yields `TextItem` tokens |
| 5. OpenAI endpoint (`--web`) | `StartWebServiceAsync()` + the official `OpenAI` .NET SDK pointed at `http://127.0.0.1:<port>/v1` |
| Optional GPU/NPU (`--eps`) | `DiscoverEps()`, `DownloadAndRegisterEpsAsync()` |

`ChatSession` is **stateful**: it remembers earlier turns natively, so each new request only
carries the new user message. (The older `model.GetChatClientAsync()` / `ChatMessage` API still
exists but is marked `[Obsolete]` in SDK 2.5.)

## Requirements

- .NET 8 SDK or later (this project targets `net10.0`)
- Windows, macOS or Linux (x64 / ARM64)
- Disk space for models. `qwen2.5-0.5b` is under 1 GB. Models are cached in `~/.foundry_local_sample/cache/models`.

## Run it

```powershell
cd FoundryLocalSample
dotnet run                      # qwen2.5-0.5b: one-shot answer, then interactive streaming chat
dotnet run -- --list            # show every model in the catalog (and which are cached)
dotnet run -- phi-4-mini        # use a different model alias
dotnet run -- --web             # also start the OpenAI-compatible endpoint and call it via the OpenAI SDK
dotnet run -- --eps             # first download GPU/NPU execution providers (large, one-time)
```

In the chat, an empty line or `exit` quits, and Ctrl+C cancels a running answer.

## Packages

| Package | Why |
|---|---|
| `Microsoft.AI.Foundry.Local` (2.5.0) | The SDK. It pulls in the native runtime (`Microsoft.AI.Foundry.Local.Runtime`), including WinML on Windows |
| `OpenAI` | Only for the `--web` demo (calling the local OpenAI-compatible endpoint) |
| `Microsoft.Extensions.Logging.Console` | Console logger for SDK diagnostics |

## Tips and gotchas

- The **first run is slow** because the model has to download. After that it's served from the cache.
- `Model not found` means the alias isn't available for your device. Run `dotnet run -- --list`.
- Without `--eps` the models run on CPU. Use `--eps` once for GPU/NPU acceleration via WinML.
- Reasoning models (`deepseek-r1-*`, `phi-4-*reasoning`, `qwen3*`) also stream `TextItem`s with
  `Type == TextItemType.Reasoning`. The sample shows only the final answer text.
- The web service binds to `127.0.0.1:0` (a random free port), and the port is read back from
  `manager.Urls`. Set a fixed URL in `Configuration.Web.Urls` if other processes need a stable endpoint.
- Dispose every `ChatSession` before `model.UnloadAsync()`, and dispose the manager at the end.

## Further reading

- https://www.foundrylocal.ai/
- Docs: https://learn.microsoft.com/azure/foundry-local/
- Samples: https://github.com/microsoft-foundry/foundry-samples (`samples/csharp/foundry-local`)
