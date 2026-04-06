using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Main/sub-agent system: host-driven orchestration with working memory, sequential
/// execution, context propagation, and adaptive plan.
///
/// These four traits distinguish this pattern from MultiAgentSystem (coordinator):
///
///   1. Working memory  — findings accumulate in a list; each sub-agent can see
///                        what earlier agents discovered.
///   2. Sequential      — sub-agents run one at a time so prior findings are available
///                        before the next agent starts. (Coordinators run in parallel.)
///   3. Context-aware   — sub-agents receive the overall goal + prior findings so they
///                        can reason in relation to the broader review, not in isolation.
///   4. Adaptive plan   — after each sub-agent, a triage agent decides whether a
///                        deep-dive is warranted and spawns one if so. The plan grows
///                        at runtime based on what agents discover.
///
/// Flow:
///   Phase 1 — Main agent establishes the overall goal  (1 API call)
///   Phase 2 — Sequential sub-agents, memory grows      (1+ calls per file)
///             └─ Triage after each file                (1 call per file)
///             └─ Deep-dive if critical issue found     (1 call, conditional)
///   Phase 3 — Main agent synthesizes complete memory   (1 API call)
/// </summary>
public static class MainSubAgentSystem
{
    // Accumulates findings as sub-agents complete — the "working memory".
    // Each entry is visible to all subsequent sub-agents.
    private record Finding(string FileName, string Review);

    // The "codebase" for this demo — three short C# snippets with distinct issues.
    private static readonly Dictionary<string, string> SampleFiles = new()
    {
        ["OrderService.cs"] = """
            public class OrderService
            {
                private readonly IOrderRepository _repo;

                public OrderService(IOrderRepository repo) => _repo = repo;

                public decimal GetOrderTotal(int orderId)
                {
                    var order = _repo.FindById(orderId);
                    // BUG: no null check — throws NullReferenceException when order is not found
                    return order.Items.Sum(i => i.Price * i.Quantity);
                }
            }
            """,

        ["PaymentProcessor.cs"] = """
            public class PaymentProcessor
            {
                public bool ProcessPayment(decimal amount, string cardNumber)
                {
                    if (amount <= 0) return false;
                    if (cardNumber.Length != 16) return false;   // magic number: 16

                    decimal fee   = amount * 0.029m;             // magic number: 0.029
                    decimal total = amount + fee + 0.30m;        // magic number: 0.30

                    return ChargeCard(cardNumber, total);
                }

                private bool ChargeCard(string card, decimal total) => true;
            }
            """,

        ["UserRepository.cs"] = """
            public class UserRepository : IUserRepository
            {
                private readonly DbContext _db;

                public UserRepository(DbContext db) => _db = db;

                public async Task<User?> FindByIdAsync(int id)
                    => await _db.Users.FindAsync(id);

                public async Task<IEnumerable<User>> GetActiveUsersAsync()
                    => await _db.Users.Where(u => u.IsActive).ToListAsync();
            }
            """
    };

    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating main/sub-agent sequential pipeline with working memory...\n");

        // ── Phase 1: Main agent establishes the overall goal ─────────────────────
        // The plan is not a fixed list of steps — it is a goal statement that every
        // subsequent sub-agent will receive as context. This is what makes them
        // context-aware rather than isolated workers.
        Console.WriteLine("Phase 1 — Main agent establishing review goal...");

        var fileList = string.Join(", ", SampleFiles.Keys);

        var planResponse = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeSonnet4_6,
            MaxTokens = 128,
            System   = "You are a code review lead. State the overall review goal in 1-2 sentences. Be specific about what to watch for.",
            Messages = [new() { Role = Role.User, Content = $"We are reviewing a payment service PR. Files: {fileList}" }]
        });

        var overallGoal = string.Join("", planResponse.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        Console.WriteLine($"  Goal: {overallGoal}\n");

        // ── Phase 2: Sequential sub-agents with working memory ───────────────────
        // Intentionally sequential — each sub-agent receives prior findings so it
        // can reason about consistency, dependencies, and related issues across files.
        // Parallel execution (Task.WhenAll) would make prior findings unavailable
        // at spawn time, removing context propagation and adaptive plan entirely.
        Console.WriteLine("Phase 2 — Sequential sub-agents with working memory...\n");

        var memory = new List<Finding>();

        foreach (var (fileName, code) in SampleFiles)
        {
            // Build a summary of everything found so far — this is the working memory
            // passed into each new sub-agent so it has situational awareness.
            var priorFindings = memory
                .Select(f => $"  • {f.FileName}: {Truncate(f.Review, 120)}")
                .ToList();

            Console.WriteLine($"  → Spawning sub-agent for '{fileName}'...");
            var review = await RunSubAgentAsync(client, fileName, code, overallGoal, priorFindings);
            memory.Add(new Finding(fileName, review));
            Console.WriteLine($"  [Sub-agent complete — '{fileName}' added to working memory]");

            // Adaptive plan: a lightweight triage agent decides whether this finding
            // is critical enough to warrant a deep-dive. The set of agents is not
            // fixed at startup — it grows based on what the agents discover.
            if (await NeedsDeepDiveAsync(client, fileName, review))
            {
                Console.WriteLine($"  ⚠ Critical issue detected — spawning deep-dive agent for '{fileName}'...");
                var deepDive = await RunDeepDiveAgentAsync(client, fileName, review, overallGoal, memory);
                memory.Add(new Finding($"{fileName} (deep-dive)", deepDive));
                Console.WriteLine($"  [Deep-dive complete — added to working memory]");
            }

            Console.WriteLine();
        }

        // ── Phase 3: Main agent synthesizes the complete working memory ──────────
        // The main agent never saw the raw source files. It works only from the
        // distilled findings, keeping its context lean regardless of codebase size.
        Console.WriteLine("Phase 3 — Main agent synthesizing complete working memory...\n");

        var memoryDump = string.Join("\n\n", memory.Select(f => $"### {f.FileName}\n{f.Review}"));

        var synthesisResponse = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeSonnet4_6,
            MaxTokens = 1024,
            System   = "You are a code review lead. Synthesize the findings into a prioritized action list " +
                       "with severity levels (Critical / Warning / Info) and concrete next steps.",
            Messages =
            [
                new()
                {
                    Role    = Role.User,
                    Content = $"Overall goal: {overallGoal}\n\nSub-agent findings:\n\n{memoryDump}\n\nProvide a consolidated report."
                }
            ]
        });

        var synthesis = string.Join("", synthesisResponse.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        Console.WriteLine($"Claude: {synthesis}");
    }

    // Sub-agent: reviews one file with awareness of the overall goal and prior findings.
    // The priorFindings parameter is what separates this from an isolated coordinator worker.
    private static async Task<string> RunSubAgentAsync(
        AnthropicClient client,
        string fileName,
        string code,
        string overallGoal,
        List<string> priorFindings)
    {
        var priorContext = priorFindings.Count > 0
            ? $"Prior findings from other files:\n{string.Join("\n", priorFindings)}\n\n"
            : string.Empty;

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeHaiku4_5,
            MaxTokens = 512,
            System   = "You are a code reviewer. Identify bugs, smells, and improvements. " +
                       "Note any issues that relate to or compound findings from other files. Be concise.",
            Messages =
            [
                new()
                {
                    Role    = Role.User,
                    Content = $"Overall goal: {overallGoal}\n\n" +
                              priorContext +
                              $"Review this file ({fileName}):\n\n```csharp\n{code}\n```"
                }
            ]
        });

        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    // Triage agent: classifies severity with a minimal prompt (Haiku for speed).
    // Returns true if the finding is critical enough to warrant a deep-dive.
    // This is the adaptive plan gate — the total number of agents is not known upfront.
    private static async Task<bool> NeedsDeepDiveAsync(AnthropicClient client, string fileName, string review)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeHaiku4_5,
            MaxTokens = 5,
            System   = "You are a triage agent. Reply with only YES or NO.",
            Messages =
            [
                new()
                {
                    Role    = Role.User,
                    Content = $"Does this review describe a critical runtime bug " +
                              $"(e.g. NullReferenceException, data corruption, security flaw) " +
                              $"that requires deeper analysis?\n\nFile: {fileName}\nReview: {review}"
                }
            ]
        });

        var answer = string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return answer.Contains("YES", StringComparison.OrdinalIgnoreCase);
    }

    // Deep-dive agent: escalates to Sonnet for a critical finding.
    // Receives full working memory so it can assess blast radius across all reviewed files.
    private static async Task<string> RunDeepDiveAgentAsync(
        AnthropicClient client,
        string fileName,
        string initialReview,
        string overallGoal,
        IReadOnlyList<Finding> memory)
    {
        var priorContext = memory
            .Where(f => f.FileName != fileName)
            .Select(f => $"  • {f.FileName}: {Truncate(f.Review, 150)}");

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model    = Model.ClaudeSonnet4_6, // escalate — deep analysis warrants a stronger model
            MaxTokens = 512,
            System   = "You are a senior engineer specializing in critical bug analysis. " +
                       "Be concise but thorough on blast radius and the recommended fix.",
            Messages =
            [
                new()
                {
                    Role    = Role.User,
                    Content = $"Overall goal: {overallGoal}\n\n" +
                              $"A critical issue was found in {fileName}.\n\n" +
                              $"Initial review:\n{initialReview}\n\n" +
                              $"Other files reviewed so far:\n{string.Join("\n", priorContext)}\n\n" +
                              $"Assess: What is the blast radius? Which callers are at risk? What is the fix?"
                }
            ]
        });

        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    // Trims a review to a short snippet for inclusion in working memory summaries.
    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
