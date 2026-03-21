using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Tool use: defines a calculator tool and runs an agentic loop until Claude
/// produces a final answer (stop_reason == "end_turn").
/// </summary>
public static class ToolUse
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating tool use with a calculator...\n");

        // Define the tool with a JSON Schema for its input
        List<ToolUnion> tools =
        [
            new Tool
            {
                Name        = "calculate",
                Description = "Evaluate a math expression and return the numeric result.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["expression"] = JsonSerializer.SerializeToElement(
                            new { type = "string", description = "Arithmetic expression, e.g. '(10 + 5) * 3'" })
                    },
                    Required = ["expression"]
                }
            }
        ];

        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = "What is (144 / 12) + (7 * 8)? Use the calculator tool." }
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
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID    = toolUse.ID,
                        Name  = toolUse.Name,
                        Input = toolUse.Input
                    });

                    var expression = toolUse.Input["expression"].GetString() ?? "";
                    var result     = Evaluate(expression);

                    Console.WriteLine($"  [Tool '{toolUse.Name}': {expression} = {result}]");

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content   = result
                    });
                }
            }

            messages.Add(new() { Role = Role.Assistant, Content = assistantContent });

            if (response.StopReason == "tool_use")
                messages.Add(new() { Role = Role.User, Content = toolResults });
            else
                break; // end_turn — Claude has the final answer
        }
    }

    // Uses DataTable.Compute for safe, dependency-free arithmetic evaluation
    private static string Evaluate(string expression)
    {
        try
        {
            var result = new System.Data.DataTable().Compute(expression, null);
            return result?.ToString() ?? "Error";
        }
        catch
        {
            return "Invalid expression";
        }
    }
}
