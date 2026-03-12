// ============================================================================
// RAG Example - Retrieval-Augmented Generation with Vector Search
// ============================================================================
//
// This sample demonstrates how to use ZeroProximity.VectorizedContentIndexer
// for RAG (Retrieval-Augmented Generation) scenarios. RAG is a technique where
// relevant documents are retrieved from a knowledge base and provided as context
// to an LLM to ground its responses in factual information.
//
// Key concepts demonstrated:
// - Loading and chunking documents
// - Creating a vector search engine
// - Indexing document chunks
// - Semantic search for relevant context
// - Building LLM prompts with retrieved context
//
// Run with: dotnet run --project samples/RagExample
// ============================================================================

using RagExample;
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Search.Vector;

Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine("RAG Example - Vector Search for Retrieval-Augmented Generation");
Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine();

// ============================================================================
// Step 1: Initialize the Embedding Provider
// ============================================================================
// The embedding provider converts text into dense vector representations.
// By default, the factory uses a hash-based fallback when ONNX models
// are not available. For production, you would use ONNX models for
// true semantic understanding.

Console.WriteLine("[Step 1] Initializing embedding provider...");
var embeddings = await EmbeddingProviderFactory.CreateAsync();
Console.WriteLine($"         Provider: {embeddings.ModelName}");
Console.WriteLine($"         Dimensions: {embeddings.Dimensions}");
Console.WriteLine();

// ============================================================================
// Step 2: Create the Vector Search Engine
// ============================================================================
// The VectorSearchEngine stores document embeddings in an AJVI index file
// for efficient similarity search. We use Float16 precision for a good
// balance between accuracy and storage efficiency.

Console.WriteLine("[Step 2] Creating vector search engine...");

// Use a temporary directory for this demo (in production, use a persistent path)
var indexPath = Path.Combine(Path.GetTempPath(), "rag-example-index", Guid.NewGuid().ToString("N")[..8]);
Console.WriteLine($"         Index path: {indexPath}");

await using var searchEngine = new VectorSearchEngine<DocumentChunk>(
    indexPath,
    embeddings,
    VectorPrecision.Float16);

// Initialize the engine (creates or opens the index)
await searchEngine.InitializeAsync();
Console.WriteLine("         Engine initialized successfully.");
Console.WriteLine();

// ============================================================================
// Step 3: Load and Chunk Sample Documents
// ============================================================================
// In a real RAG application, you would:
// 1. Load documents from files, databases, or APIs
// 2. Split them into overlapping chunks for better retrieval
// 3. Add metadata for filtering (source, date, category, etc.)

Console.WriteLine("[Step 3] Loading and chunking documents...");

var sampleDocumentsPath = Path.Combine(AppContext.BaseDirectory, "SampleDocuments");
var chunks = new List<DocumentChunk>();

// Check if sample documents exist
if (!Directory.Exists(sampleDocumentsPath))
{
    Console.WriteLine("         Sample documents not found, using inline examples...");
    chunks = CreateInlineSampleChunks();
}
else
{
    var files = Directory.GetFiles(sampleDocumentsPath, "*.txt");
    Console.WriteLine($"         Found {files.Length} document files.");

    foreach (var file in files)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        var content = await File.ReadAllTextAsync(file);

        // Simple chunking: split by double newlines (paragraphs)
        var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i].Trim();
            if (paragraph.Length > 50) // Skip very short paragraphs
            {
                chunks.Add(DocumentChunk.Create(fileName, paragraph, chunks.Count));
            }
        }

        Console.WriteLine($"         - {fileName}: {paragraphs.Length} paragraphs");
    }
}

Console.WriteLine($"         Total chunks to index: {chunks.Count}");
Console.WriteLine();

// ============================================================================
// Step 4: Index Document Chunks
// ============================================================================
// Indexing converts each chunk's text content into a vector embedding
// and stores it in the search index. This is typically done once when
// documents are added to your knowledge base.

Console.WriteLine("[Step 4] Indexing document chunks...");
var sw = System.Diagnostics.Stopwatch.StartNew();

// Index all chunks in batch for efficiency
await searchEngine.IndexManyAsync(chunks);

sw.Stop();
Console.WriteLine($"         Indexed {chunks.Count} chunks in {sw.ElapsedMilliseconds}ms");
Console.WriteLine();

// ============================================================================
// Step 5: Perform Semantic Search (RAG Retrieval)
// ============================================================================
// When a user asks a question, we search for relevant document chunks
// to provide as context to the LLM. The search finds semantically similar
// content even when exact keywords don't match.

Console.WriteLine("[Step 5] Performing semantic search for RAG context...");
Console.WriteLine();

// Example queries that might be asked of an LLM with RAG
var queries = new[]
{
    "How do I optimize async performance in C#?",
    "What are best practices for error handling?",
    "How can I improve memory usage in my application?",
    "What is dependency injection and why should I use it?"
};

foreach (var query in queries)
{
    Console.WriteLine($"Query: \"{query}\"");
    Console.WriteLine("-".PadRight(60, '-'));

    // Search for top 3 most relevant chunks
    var results = await searchEngine.SearchAsync(
        query,
        maxResults: 3,
        mode: SearchMode.Semantic);

    if (results.Count == 0)
    {
        Console.WriteLine("  No relevant documents found.");
    }
    else
    {
        Console.WriteLine($"  Found {results.Count} relevant chunk(s):");
        Console.WriteLine();

        foreach (var result in results)
        {
            var truncatedContent = result.Document.Content.Length > 120
                ? result.Document.Content[..120] + "..."
                : result.Document.Content;

            Console.WriteLine($"  [{result.Score:F4}] Source: {result.Document.SourceDocument}");
            Console.WriteLine($"           {truncatedContent}");
            Console.WriteLine();
        }
    }

    // ============================================================================
    // Step 6: Build LLM Prompt with Retrieved Context
    // ============================================================================
    // This demonstrates how to construct a prompt that includes the retrieved
    // context. In production, you would send this to an LLM API.

    if (results.Count > 0)
    {
        var context = string.Join("\n\n---\n\n", results.Select(r =>
            $"Source: {r.Document.SourceDocument}\n{r.Document.Content}"));

        var llmPrompt = $"""
            You are a helpful assistant. Answer the user's question based on the provided context.
            If the context doesn't contain relevant information, say so.

            ## Context

            {context}

            ## User Question

            {query}

            ## Your Answer

            """;

        Console.WriteLine("  [LLM Prompt Preview]");
        Console.WriteLine($"  Prompt length: {llmPrompt.Length} characters");
        Console.WriteLine($"  Context chunks: {results.Count}");
        Console.WriteLine();
    }

    Console.WriteLine();
}

// ============================================================================
// Step 7: Show Index Statistics
// ============================================================================

Console.WriteLine("[Step 7] Index statistics...");
var stats = await searchEngine.GetStatsAsync();
Console.WriteLine($"         Entries: {stats.EntryCount}");
Console.WriteLine($"         Dimensions: {stats.Dimensions}");
Console.WriteLine($"         Precision: {stats.Precision}");
Console.WriteLine($"         Size: {stats.SizeMB:F2} MB");
Console.WriteLine($"         Cached documents: {stats.CachedDocuments}");
Console.WriteLine();

// ============================================================================
// Cleanup
// ============================================================================

Console.WriteLine("[Cleanup] Removing temporary index...");
try
{
    Directory.Delete(indexPath, recursive: true);
    Console.WriteLine("         Done.");
}
catch
{
    Console.WriteLine("         Could not delete temp directory (files may be in use).");
}

Console.WriteLine();
Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine("RAG Example Complete!");
Console.WriteLine("=".PadRight(70, '='));

// ============================================================================
// Helper: Create inline sample documents when files aren't available
// ============================================================================
static List<DocumentChunk> CreateInlineSampleChunks()
{
    var chunks = new List<DocumentChunk>();

    // C# Best Practices chunks
    var csharpBestPractices = new[]
    {
        "Always prefer readonly fields and immutable objects when possible. Immutability makes your code easier to reason about and helps prevent bugs caused by unintended state changes. Use records for immutable data types in C# 9+.",
        "Use dependency injection to manage object lifetimes and dependencies. This promotes loose coupling, makes testing easier, and follows the SOLID principles. The built-in Microsoft.Extensions.DependencyInjection container is suitable for most applications.",
        "Handle exceptions at the appropriate level. Don't catch exceptions you can't handle meaningfully. Log exceptions with enough context for debugging, and use exception filters (when clause) to selectively catch specific cases.",
        "Follow consistent naming conventions: PascalCase for public members and types, camelCase for private fields (optionally prefixed with underscore), and SCREAMING_CASE for constants. Consistent naming improves code readability.",
        "Write self-documenting code with clear variable and method names. Use XML documentation comments for public APIs. Comments should explain 'why', not 'what' - the code itself should be clear enough to explain what it does."
    };

    for (int i = 0; i < csharpBestPractices.Length; i++)
    {
        chunks.Add(DocumentChunk.Create("csharp-best-practices", csharpBestPractices[i], i));
    }

    // .NET Performance chunks
    var dotnetPerformance = new[]
    {
        "Minimize allocations in hot paths by using Span<T>, stackalloc for small arrays, and ArrayPool<T> for larger buffers. Every allocation has a cost - the allocation itself, initialization, and eventual garbage collection.",
        "Use StringBuilder for string concatenation in loops. Each string concatenation creates a new string object, leading to O(n^2) allocation behavior. StringBuilder amortizes this to O(n).",
        "Profile before optimizing. Use tools like BenchmarkDotNet for microbenchmarks and dotnet-trace/PerfView for production profiling. Optimize based on data, not intuition - the bottleneck is often not where you expect.",
        "Consider using value types (structs) for small, immutable data that is frequently created and destroyed. Structs avoid heap allocation and garbage collection overhead, but be careful with boxing and large struct sizes.",
        "Use async/await efficiently: avoid async void (except for event handlers), don't block on async code (no .Result or .Wait()), and use ConfigureAwait(false) in library code to avoid capturing synchronization context."
    };

    for (int i = 0; i < dotnetPerformance.Length; i++)
    {
        chunks.Add(DocumentChunk.Create("dotnet-performance", dotnetPerformance[i], i));
    }

    // Async Programming chunks
    var asyncProgramming = new[]
    {
        "Async programming in C# allows you to write non-blocking code that scales better under load. Use async/await for I/O-bound operations like file access, database queries, and HTTP requests.",
        "The Task type represents an asynchronous operation. Task<T> represents an operation that returns a value. Use ValueTask<T> for operations that frequently complete synchronously to avoid Task allocation overhead.",
        "Cancellation tokens allow you to cancel long-running operations cooperatively. Pass CancellationToken to async methods and check IsCancellationRequested periodically or call ThrowIfCancellationRequested().",
        "Avoid blocking the thread pool by never calling .Result or .Wait() on incomplete tasks. This can lead to thread pool exhaustion and deadlocks, especially in ASP.NET applications with synchronization contexts.",
        "Use Task.WhenAll() to run multiple independent async operations concurrently. This is much more efficient than awaiting each task sequentially. For operations that might fail, consider Task.WhenAny() with proper error handling."
    };

    for (int i = 0; i < asyncProgramming.Length; i++)
    {
        chunks.Add(DocumentChunk.Create("async-programming", asyncProgramming[i], i));
    }

    return chunks;
}
