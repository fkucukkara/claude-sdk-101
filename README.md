# ClaudeSDK101

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![NuGet](https://img.shields.io/nuget/v/Anthropic)](https://www.nuget.org/packages/Anthropic)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 10 demo project for the official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp), covering four common Claude API usage patterns.

## What It Demonstrates

| Example | Description |
|---|---|
| `BasicChat` | Single-turn request and response |
| `StreamingChat` | Token-by-token streamed output |
| `MultiTurnConversation` | Stateful conversation with history |
| `ToolUse` | Agentic tool/function calling loop |

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
| ToolUse | `claude-sonnet-4-6` | Better reasoning for tool calls |

Swap any model for `claude-opus-4-6` for maximum capability.

## Project Structure

```
ClaudeSDK101/
├── Program.cs                      # Entry point — runs all four demos
├── Examples/
│   ├── BasicChat.cs
│   ├── StreamingChat.cs
│   ├── MultiTurnConversation.cs
│   └── ToolUse.cs
├── .env                            # API key (git-ignored)
└── ClaudeSDK101.csproj
```

## License

MIT
