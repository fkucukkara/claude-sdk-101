using Anthropic;
using Anthropic.Models.Messages;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;
using System.Text.Json;

// Aliases resolve ambiguity between Anthropic and MCP protocol types
using AnthropicRole = Anthropic.Models.Messages.Role;
using AnthropicTool = Anthropic.Models.Messages.Tool;

namespace ClaudeSDK101.Examples;

// ── SERVER ────────────────────────────────────────────────────────────────────
// Creates and configures an MCP server with two tools over a stream transport.
// No DI or hosting required — just a transport and a tool collection.

public static class McpDemoServer
{
    public static McpServer Create(Stream input, Stream output)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new() { Name = "demo-server", Version = "1.0" },
            ToolCollection = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal)
        };

        options.ToolCollection.Add(McpServerTool.Create(
            () => DateTime.Now.ToString("HH:mm:ss"),
            new McpServerToolCreateOptions
            {
                Name = "GetTime",
                Description = "Returns the current local time as HH:mm:ss"
            }));

        options.ToolCollection.Add(McpServerTool.Create(
            (string text) => $"Echo: {text}",
            new McpServerToolCreateOptions
            {
                Name = "Echo",
                Description = "Echoes a message back to the caller"
            }));

        return McpServer.Create(new StreamServerTransport(input, output, null, null), options);
    }
}

// ── AGENT LOOP ────────────────────────────────────────────────────────────────
// Connects to the MCP server, discovers tools, converts them to Anthropic Tool
// objects, then runs a Claude agentic loop dispatching calls through MCP.

public static class McpAgentLoop
{
    public static async Task RunAsync(AnthropicClient anthropic, Stream readStream, Stream writeStream)
    {
        // serverInput = stream the client writes to (server reads from it)
        // serverOutput = stream the client reads from (server writes to it)
        await using var mcpClient = await McpClient.CreateAsync(
            new StreamClientTransport(writeStream, readStream, null));

        var mcpTools = await mcpClient.ListToolsAsync();

        Console.WriteLine($"Discovered {mcpTools.Count} MCP tool(s):");
        foreach (var t in mcpTools)
            Console.WriteLine($"  - {t.Name}: {t.Description}");
        Console.WriteLine();

        var anthropicTools = ToAnthropicTools(mcpTools);

        var messages = new List<MessageParam>
        {
            new() { Role = AnthropicRole.User,
                    Content = "What time is it right now? Also echo back the phrase 'MCP works!'." }
        };

        while (true)
        {
            var response = await anthropic.Messages.Create(new MessageCreateParams
            {
                Model = Model.ClaudeSonnet4_6,
                MaxTokens = 512,
                Tools = anthropicTools,
                Messages = messages
            });

            List<ContentBlockParam> assistantContent = [], toolResults = [];

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
                    { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });

                    var result = await CallMcpToolAsync(mcpClient, toolUse.Name, toolUse.Input);
                    Console.WriteLine($"  [MCP '{toolUse.Name}' → \"{result}\"]");

                    toolResults.Add(new ToolResultBlockParam
                    { ToolUseID = toolUse.ID, Content = result });
                }
            }

            messages.Add(new() { Role = AnthropicRole.Assistant, Content = assistantContent });

            if (response.StopReason == "tool_use")
                messages.Add(new() { Role = AnthropicRole.User, Content = toolResults });
            else
                break;
        }
    }

    // Converts MCP tool descriptors to Anthropic Tool objects by extracting the
    // JSON Schema properties and required fields from the MCP protocol schema.
    private static List<ToolUnion> ToAnthropicTools(IList<McpClientTool> mcpTools) =>
        mcpTools.Select(t =>
        {
            var schema = t.ProtocolTool.InputSchema;

            Dictionary<string, JsonElement>? props = null;
            if (schema.TryGetProperty("properties", out var propsEl))
                props = propsEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            List<string>? required = null;
            if (schema.TryGetProperty("required", out var reqEl))
                required = [.. reqEl.EnumerateArray().Select(e => e.GetString()!)];

            return (ToolUnion)new AnthropicTool
            {
                Name = t.ProtocolTool.Name,
                Description = t.ProtocolTool.Description ?? "",
                InputSchema = new() { Properties = props, Required = required }
            };
        }).ToList();

    // Routes Claude's tool call through the MCP client → server → tool implementation.
    private static async Task<string> CallMcpToolAsync(
        McpClient client, string toolName, IReadOnlyDictionary<string, JsonElement> input)
    {
        var args = input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var result = await client.CallToolAsync(toolName, args);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    }
}

// ── ENTRY POINT ───────────────────────────────────────────────────────────────
// Wires two in-process pipes and coordinates McpDemoServer and McpDemoClient.
// The server and client communicate over the pipes — no sockets or subprocesses.

public static class McpClientServer
{
    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating MCP client-server with Claude tool use...\n");

        Pipe clientToServer = new(), serverToClient = new();

        var server = McpDemoServer.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        var serverTask = server.RunAsync();

        await McpAgentLoop.RunAsync(
            client,
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());

        // Signal EOF on both pipe ends → server detects closed connection and exits
        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();

        try { await serverTask; }
        catch { } // OperationCanceledException expected when the server exits on EOF

        await server.DisposeAsync();
    }
}
