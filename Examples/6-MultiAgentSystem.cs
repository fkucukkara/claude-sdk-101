using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Multi-agent pipeline: a coordinator (Sonnet) orchestrates three specialized agents
/// (WebSearch → Documentation → Synthesizer) via tool use. Each agent is a separate
/// Messages.Create call with a focused system prompt — no real HTTP calls are made;
/// Claude simulates domain knowledge for each sub-agent role.
///
/// Why separate API calls instead of one long prompt?
///   Each agent gets its own context window, system prompt, and model tier. A single
///   call would force one model to wear all hats, lose the ability to tune prompts
///   per role, and blow up context with unrelated conversation history.
///
/// Architecture:
///   Coordinator  ──tool──▶  WebSearch Agent    (researches the topic)
///                ──tool──▶  Documentation Agent (structures the findings)
///                ──tool──▶  Synthesizer Agent   (produces the final report)
///
/// Key patterns demonstrated:
///   • Coordinator runs the standard agentic loop (identical to ToolUse.cs)
///   • Leaf agents are single Messages.Create calls — no tools, no loop
///   • switch expression returns Task&lt;string&gt;, awaited outside the switch
///     (C# does not allow async lambdas as switch expression arms)
///   • Unknown tool and empty-result errors are returned as structured tool results,
///     not thrown — keeps the coordinator loop alive so it can recover
///   • Sequential here is a domain constraint (each step feeds the next), not a
///     pattern requirement — independent agents can be dispatched with Task.WhenAll
/// </summary>
public static class MultiAgentSystem
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating multi-agent coordinator pipeline...\n");

        // Three tools — each routes to a specialized leaf agent
        List<ToolUnion> tools =
        [
            new Tool
            {
                Name        = "call_websearch_agent",
                Description = "Calls the WebSearch agent to research a topic. Returns raw findings.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["query"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "string",
                            description = "The research query to investigate."
                        })
                    },
                    Required = ["query"]
                }
            },
            new Tool
            {
                Name        = "call_documentation_agent",
                Description = "Calls the Documentation agent to structure raw findings into organized documentation.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["findings"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "string",
                            description = "Raw research findings to be structured."
                        })
                    },
                    Required = ["findings"]
                }
            },
            new Tool
            {
                Name        = "call_synthesizer_agent",
                Description = "Calls the Synthesizer agent to produce a polished final report from structured documentation.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["documentation"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "string",
                            description = "Structured documentation to synthesize into a final report."
                        })
                    },
                    Required = ["documentation"]
                }
            }
        ];

        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = "What are the key benefits and trade-offs of using event sourcing in distributed systems?" }
        };

        // Coordinator agentic loop — identical structure to ToolUse.cs
        while (true)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model     = Model.ClaudeSonnet4_6,
                MaxTokens = 2048,
                System    = "You are a research coordinator with three specialist agents available: " +
                            "WebSearch (researches topics), Documentation (structures findings), " +
                            "and Synthesizer (produces final reports). " +
                            "Use them as the task demands — you decide which agents to call and in what order. " +
                            "Not every task requires all three agents.",
                Tools     = tools,
                Messages  = messages
            });

            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults      = [];

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                    if (!string.IsNullOrWhiteSpace(text.Text))
                        Console.WriteLine($"Claude: {text.Text}");
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID    = toolUse.ID,
                        Name  = toolUse.Name,
                        Input = toolUse.Input
                    });

                    // switch returns Task<string>; await is placed outside the switch
                    // to avoid the async-lambda-in-switch-expression limitation
                    var agentResult = await (toolUse.Name switch
                    {
                        "call_websearch_agent"     => DispatchWebSearchAsync(client, toolUse),
                        "call_documentation_agent" => DispatchDocumentationAsync(client, toolUse),
                        "call_synthesizer_agent"   => DispatchSynthesizerAsync(client, toolUse),
                        // Return a structured error instead of throwing — keeps the loop alive
                        // so the coordinator can decide whether to retry, skip, or abort.
                        _                          => Task.FromResult($"{{\"error\": \"Unknown agent: {toolUse.Name}\"}}")
                    });

                    // Guard against empty responses (API timeout, content filtered, etc.).
                    // The coordinator receives a structured error it can reason about rather
                    // than silently propagating an empty string to the next agent.
                    if (string.IsNullOrWhiteSpace(agentResult))
                        agentResult = "{\"error\": \"Agent returned no content\"}";

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content   = agentResult
                    });
                }
            }

            // Always append the full assistant turn (text + tool-use blocks mixed)
            messages.Add(new() { Role = Role.Assistant, Content = assistantContent });

            if (response.StopReason == "tool_use")
                // Feed tool results back as the next user turn
                messages.Add(new() { Role = Role.User, Content = toolResults });
            else
                break; // StopReason == "end_turn" — coordinator has delivered its final answer
        }
    }

    // ── Dispatch helpers ────────────────────────────────────────────────────────
    // Each helper extracts the relevant input parameter, prints the agent boundary,
    // and delegates to the leaf agent implementation.

    private static async Task<string> DispatchWebSearchAsync(AnthropicClient client, ToolUseBlock toolUse)
    {
        var query = toolUse.Input["query"].GetString() ?? "";
        Console.WriteLine("  → Calling WebSearch agent...");
        return await RunWebSearchAgentAsync(client, query);
    }

    private static async Task<string> DispatchDocumentationAsync(AnthropicClient client, ToolUseBlock toolUse)
    {
        var findings = toolUse.Input["findings"].GetString() ?? "";
        Console.WriteLine("  → Calling Documentation agent...");
        return await RunDocumentationAgentAsync(client, findings);
    }

    private static async Task<string> DispatchSynthesizerAsync(AnthropicClient client, ToolUseBlock toolUse)
    {
        var documentation = toolUse.Input["documentation"].GetString() ?? "";
        Console.WriteLine("  → Calling Synthesizer agent...");
        return await RunSynthesizerAgentAsync(client, documentation);
    }

    // ── Leaf agent implementations ───────────────────────────────────────────────
    // Leaf agents are single Messages.Create calls with no tools and no loop.
    // They use the simple LINQ extraction pattern — tool-use blocks are never expected.

    // NOTE: WebSearch agent simulates search using Claude's knowledge — no real web API is called.
    private static async Task<string> RunWebSearchAgentAsync(AnthropicClient client, string query)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model     = Model.ClaudeHaiku4_5,
            MaxTokens = 1024,
            System    = "You are a research agent. When given a query, provide factual, detailed findings on the topic. Simulate thorough research results.",
            Messages  = [new() { Role = Role.User, Content = query }]
        });

        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    private static async Task<string> RunDocumentationAgentAsync(AnthropicClient client, string findings)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model     = Model.ClaudeHaiku4_5,
            MaxTokens = 1024,
            System    = "You are a technical writer. Structure the provided findings into clear, organized documentation with sections and bullet points.",
            Messages  = [new() { Role = Role.User, Content = findings }]
        });

        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    private static async Task<string> RunSynthesizerAgentAsync(AnthropicClient client, string documentation)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model     = Model.ClaudeSonnet4_6,
            MaxTokens = 1024,
            System    = "You are a synthesis expert. Produce a polished, executive-level final report from the provided documentation. Be concise and highlight key insights.",
            Messages  = [new() { Role = Role.User, Content = documentation }]
        });

        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }
}
