# ClaudeSDK101

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![NuGet](https://img.shields.io/nuget/v/Anthropic)](https://www.nuget.org/packages/Anthropic)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 10 demo project for the official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp), covering six Claude API usage patterns ordered from simple to advanced.

## What It Demonstrates

| # | Example | Description |
|---|---|---|
| 1 | `BasicChat` | Single-turn request and response |
| 2 | `MultiTurnConversation` | Stateful conversation with accumulated history |
| 3 | `StreamingChat` | Token-by-token streamed output |
| 4 | `ToolUse` | Agentic tool/function calling loop |
| 5 | `MainSubAgentSystem` | **Code-driven** orchestration: sequential sub-agents with working memory, context propagation, and adaptive plan (triage agent conditionally spawns deep-dives) |
| 6 | `MultiAgentSystem` | **LLM-driven** orchestration: coordinator decides workflow via tool calls to specialist agents |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An [Anthropic API key](https://console.anthropic.com/)

## Getting Started

```bash
git clone https://github.com/your-username/ClaudeSDK101.git
cd ClaudeSDK101
```

Add your API key to `.env`:

```env
ANTHROPIC_API_KEY=sk-ant-...
```

Then run:

```bash
dotnet run
```

The app loads `.env` automatically via [DotNetEnv](https://github.com/motdotla/dotnet-env).

## Models Used

| Example | Model | Why |
|---|---|---|
| BasicChat, Streaming, MultiTurn | `claude-haiku-4-5` | Fast and cost-effective |
| ToolUse, coordinator & synthesis roles | `claude-sonnet-4-6` | Better reasoning for tool calls and orchestration |
| Sub-agent & triage workers (demos 5 & 6) | `claude-haiku-4-5` | Focused, single-file tasks need speed not depth |
| Deep-dive agent (demo 5, conditional) | `claude-sonnet-4-6` | Escalates to stronger model when triage flags a critical bug |

Swap any model for `claude-opus-4-6` for maximum capability.

## Project Structure

```
ClaudeSDK101/
├── Program.cs                      # Entry point — runs all six demos sequentially
├── Examples/
│   ├── 1-BasicChat.cs              # Single-turn
│   ├── 2-MultiTurnConversation.cs  # History management
│   ├── 3-StreamingChat.cs          # Token streaming
│   ├── 4-ToolUse.cs                # Agentic tool loop
│   ├── 5-MainSubAgentSystem.cs     # Parallel sub-agents (code-driven)
│   └── 6-MultiAgentSystem.cs       # Specialist pipeline (LLM-driven)
├── .env                            # API key (git-ignored)
└── ClaudeSDK101.csproj
```

## License

MIT
