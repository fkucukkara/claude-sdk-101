using Anthropic;
using ClaudeSDK101.Examples;
using DotNetEnv;

Env.TraversePath().Load(); // load .env file, traversing up from cwd

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("ERROR: ANTHROPIC_API_KEY environment variable is not set.");
    return 1;
}

var client = new AnthropicClient { ApiKey = apiKey };

await RunDemo("1. Basic Chat", () => BasicChat.RunAsync(client));
await RunDemo("2. Streaming Chat", () => StreamingChat.RunAsync(client));
await RunDemo("3. Multi-Turn Conversation", () => MultiTurnConversation.RunAsync(client));
await RunDemo("4. Tool Use (DateTime)", () => ToolUse.RunAsync(client));
await RunDemo("5. Main / Sub-Agent System", () => MainSubAgentSystem.RunAsync(client));
await RunDemo("6. Multi-Agent System", () => MultiAgentSystem.RunAsync(client));

return 0;

static async Task RunDemo(string title, Func<Task> demo)
{
    Console.WriteLine($"\n{"=".PadRight(50, '=')}");
    Console.WriteLine($"  {title}");
    Console.WriteLine($"{"=".PadRight(50, '=')}");
    try { await demo(); }
    catch (Exception ex) { Console.WriteLine($"[ERROR] {ex.Message}"); }
}
