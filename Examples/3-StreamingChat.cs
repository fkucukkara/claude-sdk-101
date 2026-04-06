using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>Streaming: tokens are printed as they arrive.</summary>
public static class StreamingChat
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Streaming a response from Claude...\n");
        Console.Write("Claude: ");

        await foreach (var streamEvent in client.Messages.CreateStreaming(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5,
            MaxTokens = 300,
            Messages = [new() { Role = Role.User, Content = "Write a short poem about software development." }]
        }))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                delta.Delta.TryPickText(out var text))
            {
                Console.Write(text.Text);
            }
        }

        Console.WriteLine("\n");
    }
}
