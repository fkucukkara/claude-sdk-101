using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>Multi-turn: conversation history is accumulated across three turns.</summary>
public static class MultiTurnConversation
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Starting a multi-turn conversation...\n");

        var messages = new List<MessageParam>();

        await Turn(client, messages, "What is the difference between a stack and a queue?");
        await Turn(client, messages, "Give me a real-world analogy for each.");
        await Turn(client, messages, "Which one would you use for undo/redo functionality and why?");
    }

    private static async Task Turn(AnthropicClient client, List<MessageParam> messages, string userText)
    {
        messages.Add(new() { Role = Role.User, Content = userText });
        Console.WriteLine($"You: {userText}");

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeHaiku4_5,
            MaxTokens = 512,
            System   = "You are a helpful programming tutor. Keep answers concise.",
            Messages = messages
        });

        var reply = string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        Console.WriteLine($"Claude: {reply}\n");

        // Append assistant reply as plain text — keeps history lean for the demo
        messages.Add(new() { Role = Role.Assistant, Content = reply });
    }
}
