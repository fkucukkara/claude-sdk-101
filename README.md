# ClaudeSDK101

[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com)
[![NuGet](https://img.shields.io/nuget/v/Anthropic)](https://www.nuget.org/packages/Anthropic)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 8 demo project for the official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp), covering four common Claude API usage patterns.

## What It Demonstrates

| Example | Description |
|---|---|
| `BasicChat` | Single-turn request and response |
| `StreamingChat` | Token-by-token streamed output |
| `MultiTurnConversation` | Stateful conversation with history |
| `ToolUse` | Agentic tool/function calling loop |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An [Anthropic API key](https://console.anthropic.com/)

## Getting Started

```bash
git clone https://github.com/your-username/ClaudeSDK101.git
cd ClaudeSDK101

# Set your API key
export ANTHROPIC_API_KEY="sk-ant-..."      # Linux / macOS
$env:ANTHROPIC_API_KEY = "sk-ant-..."     # PowerShell

dotnet run
```

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
└── ClaudeSDK101.csproj
```

## License

MIT
