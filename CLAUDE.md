# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore          # restore NuGet packages
dotnet build            # build (0 warnings expected — Nullable is enabled)
dotnet run              # run all four demos sequentially
```

There are no tests. The app requires `ANTHROPIC_API_KEY` to run (reads from environment; `.env` is present but not auto-loaded — see below).

## Architecture

Single-project .NET 8 console app using the official **`Anthropic`** NuGet package (not the community `Anthropic.SDK`).

**Entry point:** `Program.cs` — checks `ANTHROPIC_API_KEY`, creates `AnthropicClient`, then runs each example via a `RunDemo` helper that wraps calls in try/catch.

**Examples/** — four static classes, one per demo pattern:

| File | Pattern | Key SDK surface |
|---|---|---|
| `BasicChat.cs` | Single-turn | `client.Messages.Create` |
| `StreamingChat.cs` | Streaming | `client.Messages.CreateStreaming` (async stream of `RawMessageStreamEvent`) |
| `MultiTurnConversation.cs` | History | `List<MessageParam>` grown across turns; assistant reply appended as plain string |
| `ToolUse.cs` | Agentic loop | Manual `while` loop; breaks on `StopReason == "end_turn"` |

## SDK Patterns

- Content blocks: `response.Content.Select(b => b.Value).OfType<TextBlock>()` — `.Value` unwraps the `ContentBlock` union.
- Streaming: narrow with `TryPickContentBlockDelta` → `delta.Delta.TryPickText`.
- Tool results: `TextBlockParam`, `ToolUseBlockParam`, `ToolResultBlockParam` all implicitly convert to `ContentBlockParam`. No `.ToParam()` helper exists — reconstruct manually.
- `MessageParam.Content` accepts either a `string` or `List<ContentBlockParam>` (implicit conversions).
- `Tool` implicitly converts to `ToolUnion`.
- Tool input parsing: `toolUse.Input["key"].GetString()` — `Input` is `IReadOnlyDictionary<string, JsonElement>`.
- `DataTable.Compute` is used for arithmetic evaluation in `ToolUse.cs` (no external dependency).

## Environment / API Key

`.env` contains the key but .NET does not load it automatically. Either set `ANTHROPIC_API_KEY` in the shell, or add `DotNetEnv` (`dotnet add package DotNetEnv`) and call `DotNetEnv.Env.Load()` at the top of `Program.cs`.

## Models

- `Model.ClaudeHaiku4_5` — used for Basic/Streaming/MultiTurn demos
- `Model.ClaudeSonnet4_6` — used for ToolUse (better tool reasoning)
