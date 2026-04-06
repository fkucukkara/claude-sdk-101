using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>Single-turn request: send a message, get a response.</summary>
public static class BasicChat
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Sending a single message to Claude...\n");

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5,
            MaxTokens = 512,
            System = "You are a helpful assistant. Be concise.",
            Messages = [new() { Role = Role.User, Content = "Explain what a Large Language Model is in 2-3 sentences." }]
        });

        foreach (var text in response.Content.Select(b => b.Value).OfType<TextBlock>())
            Console.WriteLine($"Claude: {text.Text}");

        Console.WriteLine($"\n[Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out]");
    }
}
