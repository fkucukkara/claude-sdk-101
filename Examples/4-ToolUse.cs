using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Tool use: defines two tools (get_current_datetime, get_random_number) and runs an
/// agentic loop until Claude produces a final answer (stop_reason == "end_turn").
///
/// Demonstrates multi-tool dispatch via toolUse.Name — each tool name routes to its
/// own implementation. Claude genuinely cannot know the current time or generate a
/// truly random number, so both tool calls are clearly necessary.
/// </summary>
public static class ToolUse
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating multi-tool use (datetime + random number)...\n");

        List<ToolUnion> tools =
        [
            new Tool
            {
                Name        = "get_current_datetime",
                Description = "Returns the current local date and time. " +
                              "Call this whenever you need to know today's date or the current time.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["format"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "string",
                            description = "Optional .NET date/time format string, " +
                                          "e.g. \"dddd, MMMM d yyyy h:mm tt\". " +
                                          "Defaults to full date and time if omitted."
                        })
                    }
                    // No required fields — "format" is optional
                }
            },
            new Tool
            {
                Name        = "get_random_number",
                Description = "Returns a random integer between min and max (inclusive).",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["min"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "integer",
                            description = "The minimum value (inclusive)."
                        }),
                        ["max"] = JsonSerializer.SerializeToElement(new
                        {
                            type        = "integer",
                            description = "The maximum value (inclusive)."
                        })
                    },
                    Required = ["min", "max"]
                }
            }
        ];

        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = "What day of the week is it today? Also, pick a random number between 1 and 100 for me." }
        };

        // Agentic loop: call → execute tools → call again until end_turn
        while (true)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model     = Model.ClaudeSonnet4_6,
                MaxTokens = 1024,
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
                    // Reconstruct the assistant's tool-use block so we can append it to history
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID    = toolUse.ID,
                        Name  = toolUse.Name,
                        Input = toolUse.Input
                    });

                    // Dispatch to the correct implementation based on tool name
                    var result = toolUse.Name switch
                    {
                        "get_current_datetime" => ExecuteGetDatetime(toolUse),
                        "get_random_number"    => ExecuteGetRandomNumber(toolUse),
                        _                      => throw new Exception($"Unknown tool: {toolUse.Name}")
                    };

                    Console.WriteLine($"  [Tool '{toolUse.Name}' called → \"{result}\"]");

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content   = result
                    });
                }
            }

            // Always append the assistant turn (may contain both text and tool-use blocks)
            messages.Add(new() { Role = Role.Assistant, Content = assistantContent });

            if (response.StopReason == "tool_use")
                // Feed the tool results back as the next user turn
                messages.Add(new() { Role = Role.User, Content = toolResults });
            else
                break; // stop_reason == "end_turn" — Claude has given its final answer
        }
    }

    private static string ExecuteGetDatetime(ToolUseBlock toolUse)
    {
        string? fmt = toolUse.Input.TryGetValue("format", out var fmtEl)
            ? fmtEl.GetString()
            : null;

        return string.IsNullOrWhiteSpace(fmt)
            ? DateTime.Now.ToString()
            : DateTime.Now.ToString(fmt);
    }

    private static string ExecuteGetRandomNumber(ToolUseBlock toolUse)
    {
        int min = toolUse.Input["min"].GetInt32();
        int max = toolUse.Input["max"].GetInt32();
        return Random.Shared.Next(min, max + 1).ToString();
    }
}
