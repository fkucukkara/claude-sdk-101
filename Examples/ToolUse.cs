using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Tool use: defines a get_current_datetime tool and runs an agentic loop until
/// Claude produces a final answer (stop_reason == "end_turn").
///
/// This is a great teaching example because Claude genuinely cannot know the
/// current time — so the tool call is clearly necessary, not just convenient.
/// </summary>
public static class ToolUse
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating tool use with a current date/time tool...\n");

        // Define the tool. The optional "format" parameter lets Claude ask for
        // a specific .NET format string (e.g. "dddd, MMMM d yyyy h:mm tt").
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
            }
        ];

        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = "What day of the week is it today, and what is the exact time right now? Please use the datetime tool." }
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

                    // Execute the tool — get the format string if Claude supplied one
                    string? fmt = toolUse.Input.TryGetValue("format", out var fmtEl)
                        ? fmtEl.GetString()
                        : null;

                    var result = string.IsNullOrWhiteSpace(fmt)
                        ? DateTime.Now.ToString()
                        : DateTime.Now.ToString(fmt);

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
}
