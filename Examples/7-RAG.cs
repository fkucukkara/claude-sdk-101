using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;

namespace ClaudeSDK101.Examples;

/// <summary>
/// Retrieval-Augmented Generation (RAG): ground Claude's answers in a private
/// knowledge base that was never part of its training data.
///
/// The three stages of RAG:
///   1. Index    — chunk documents and build a searchable in-memory store
///   2. Retrieve — score all chunks against the query, return top-K
///   3. Generate — inject retrieved chunks as context in the user prompt
///
/// Why RAG instead of fine-tuning?
///   Fine-tuning bakes knowledge into model weights at training time — expensive,
///   slow to update, and opaque. RAG keeps knowledge external and version-controlled:
///   update a document and the model sees fresh facts on the very next query.
///
/// Retrieval here uses cosine similarity over term-frequency vectors — no embedding
/// API, no vector database, no extra dependencies. For production, replace the
/// retriever with a proper embedding model (e.g. voyage-3, text-embedding-3-small)
/// and a vector store (pgvector, Pinecone, Qdrant, etc.).
/// The generate step is identical regardless of how retrieval is implemented.
///
/// Flow:
///   BuildIndex()  — tokenise + normalise each chunk into a TF vector (once at startup)
///   Retrieve()    — cosine-score all chunks against the query, return top-K
///   Augment       — wrap chunks + query into a grounded prompt
///   Generate      — Claude answers using ONLY the supplied context
/// </summary>
public static class Rag
{
    // Simulated private knowledge base — docs Claude has never seen.
    // In production: load from files, a database, or a document store.
    private static readonly string[] KnowledgeBase =
    [
        "AcmeCorp's deployment pipeline uses blue-green deployments. " +
        "The staging environment mirrors production exactly. " +
        "Deployments require sign-off from two senior engineers before going live.",

        "The payments service runs on Kubernetes in us-east-1 and eu-west-1. " +
        "It uses circuit breakers (Polly) with a 30-second timeout and 5 retries. " +
        "SLA is 99.95% uptime, measured as a rolling 30-day average.",

        "All internal APIs require mTLS authentication using certificates issued by " +
        "the internal CA. Public-facing APIs use OAuth 2.0 with JWT tokens. " +
        "Token lifetime is 15 minutes; refresh tokens expire after 7 days.",

        "The on-call rotation runs Monday to Monday. The primary on-call engineer " +
        "is paged for P1 and P2 incidents. P3 and P4 are handled during business hours. " +
        "Runbooks are stored in Confluence under /runbooks.",

        "AcmeCorp's data retention policy: production logs are kept for 90 days in " +
        "Elasticsearch, then archived to S3 Glacier. PII must be encrypted at rest " +
        "using AES-256 and deleted within 30 days of a user deletion request.",

        "The order service uses event sourcing with EventStoreDB. Events are immutable " +
        "and append-only. Projections rebuild read-model state from the event log. " +
        "Snapshots are taken every 500 events to speed up replay.",

        "Database migrations are managed with FluentMigrator. All migrations must be " +
        "backwards-compatible for one release cycle to support zero-downtime deployments. " +
        "Breaking schema changes require a three-phase migration plan.",

        "AcmeCorp engineering uses trunk-based development. Feature branches live for at " +
        "most two days. Feature flags (LaunchDarkly) gate incomplete work in production. " +
        "The main branch is always deployable.",

        "Incident severity levels: P1 = customer-facing outage, P2 = degraded service, " +
        "P3 = internal tool failure, P4 = cosmetic or non-blocking issue. " +
        "P1 must be acknowledged within 5 minutes and resolved within 1 hour.",

        "The search service uses Elasticsearch 8.x with a custom relevance model. " +
        "Queries pass through a rewrite layer that expands synonyms and corrects spelling. " +
        "Index refresh interval is 30 seconds."
    ];

    public static async Task RunAsync(AnthropicClient client)
    {
        Console.WriteLine("Demonstrating Retrieval-Augmented Generation (RAG)...\n");

        // Build the index once — in production this happens at startup or on document change.
        var index = BuildIndex(KnowledgeBase);
        Console.WriteLine($"Index built: {index.Count} chunks indexed\n");

        string[] questions =
        [
            "What authentication method do internal APIs use?",
            "How does the payments service handle failures?",
            "What is the response time requirement for a P1 incident?"
        ];

        foreach (var question in questions)
        {
            Console.WriteLine($"Q: {question}");

            // ── Stage 1: Retrieve top-K relevant chunks ───────────────────────────
            var topChunks = Retrieve(index, question, topK: 2);

            Console.WriteLine($"  Retrieved {topChunks.Count} chunk(s):");
            foreach (var chunk in topChunks)
                Console.WriteLine($"    [score {chunk.Score:F2}] {Truncate(chunk.Text, 80)}");

            // ── Stage 2: Augment prompt with retrieved context ─────────────────────
            // The context block is the only information Claude is allowed to use.
            // Grounding the prompt this way prevents hallucination: if the answer
            // is not in the retrieved chunks, Claude is instructed to say so.
            var contextBlock = string.Join(
                "\n\n",
                topChunks.Select((c, i) => $"[{i + 1}] {c.Text}"));

            var augmentedPrompt =
                $"Answer the question using ONLY the context provided below. " +
                $"If the context does not contain enough information to answer, say so explicitly.\n\n" +
                $"Context:\n{contextBlock}\n\n" +
                $"Question: {question}";

            // ── Stage 3: Generate a grounded answer ───────────────────────────────
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model     = Model.ClaudeHaiku4_5,
                MaxTokens = 256,
                System    = "You are a precise assistant that answers questions strictly based on the " +
                            "provided context. Never use prior knowledge or assumptions outside the context.",
                Messages  = [new() { Role = Role.User, Content = augmentedPrompt }]
            });

            var answer = string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            Console.WriteLine($"  A: {answer}\n");
        }
    }

    // ── Indexing ──────────────────────────────────────────────────────────────────
    // Tokenise each chunk and build an L2-normalised term-frequency vector.
    // Production replacement: call an embedding model, store float[] in a vector DB.

    private record IndexedChunk(string Text, Dictionary<string, double> Vector);

    private static List<IndexedChunk> BuildIndex(string[] chunks) =>
        chunks.Select(c => new IndexedChunk(c, TermFrequencyVector(c))).ToList();

    // ── Retrieval ─────────────────────────────────────────────────────────────────
    // Score every chunk against the query with cosine similarity, return top-K.
    // Production replacement: approximate nearest-neighbour search (HNSW, IVF, etc.).

    private record RetrievedChunk(string Text, double Score);

    private static List<RetrievedChunk> Retrieve(List<IndexedChunk> index, string query, int topK)
    {
        var queryVector = TermFrequencyVector(query);

        return index
            .Select(doc => new RetrievedChunk(doc.Text, CosineSimilarity(queryVector, doc.Vector)))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    // ── Vector helpers ────────────────────────────────────────────────────────────

    private static Dictionary<string, double> TermFrequencyVector(string text)
    {
        var tokens = Tokenise(text);
        var counts = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count());
        var norm   = Math.Sqrt(counts.Values.Sum(v => v * v));
        // L2-normalise so cosine similarity reduces to a simple dot product
        return norm == 0 ? counts : counts.ToDictionary(kv => kv.Key, kv => kv.Value / norm);
    }

    private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        double dot = 0;
        foreach (var (term, weight) in a)
            if (b.TryGetValue(term, out var w)) dot += weight * w;
        return dot; // both vectors are L2-normalised, so denominator is 1
    }

    private static string[] Tokenise(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+")
             .Select(m => m.Value)
             .Where(t => t.Length > 2 && !StopWords.Contains(t))
             .ToArray();

    // Common English stop words that add noise to similarity scoring
    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "are", "but", "not", "all", "can", "has",
        "its", "may", "new", "now", "use", "way", "who", "did", "let",
        "put", "say", "she", "too", "was", "per", "that", "this", "with",
        "from", "they", "been", "have", "will", "each", "more", "than"
    ];

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
