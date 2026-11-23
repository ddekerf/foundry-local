# Foundry Local .NET samples

Basic .NET samples for [Foundry Local](https://www.foundrylocal.ai/), Microsoft's runtime for running
open-source LLMs (Phi, Qwen, Mistral, DeepSeek-R1, gpt-oss, Whisper, …) entirely on your own machine:
no cloud, no API key, no data leaving the device.

| Project | What it shows |
|---|---|
| [FoundryLocalSample](FoundryLocalSample/) | The Foundry Local C# SDK: catalog, download/load a model, one-shot and streaming chat with `ChatSession`, and the OpenAI-compatible web service called via the OpenAI .NET SDK |
| [FoundryLocalAgent](FoundryLocalAgent/) | A **Microsoft Agent Framework** agent with C# tools and multi-turn memory, running on a Foundry Local model |

## Quick start

Requires the .NET 8+ SDK. The projects target `net10.0`.

```powershell
cd FoundryLocalSample
dotnet run              # downloads qwen2.5-0.5b on first run, then chat

cd ../FoundryLocalAgent
dotnet run              # downloads qwen2.5-1.5b on first run, agent with tools
```

Each project's README has details, options and tips. Built with `Microsoft.AI.Foundry.Local` 2.5.0
and `Microsoft.Agents.AI.OpenAI` 1.28.0.
