# Getting Started with ZeroProximity.VectorizedContentIndexer

This guide will walk you through the basics of using the VectorizedContentIndexer library, from installation to your first working search index.

## Table of Contents

1. [Installation](#installation)
2. [Basic Concepts](#basic-concepts)
3. [Your First Search Index](#your-first-search-index)
4. [Indexing Documents](#indexing-documents)
5. [Searching](#searching)
6. [Common Patterns](#common-patterns)
7. [Next Steps](#next-steps)

## Installation

Install the NuGet package in your .NET project:

```bash
dotnet add package ZeroProximity.VectorizedContentIndexer
```

Or via Package Manager Console:

```powershell
Install-Package ZeroProximity.VectorizedContentIndexer
```

### Requirements

- .NET 9.0 or later
- Approximately 100MB disk space (includes embedded ONNX model)

## Basic Concepts

### The ISearchable Interface

At its core, any content you want to index must implement `ISearchable`:

```csharp
public interface ISearchable
{
    string Id { get; }
    string GetSearchableText();
    DateTime GetTimestamp();
}
```

This minimal interface allows the library to:
- **Index** your content for both keyword and semantic search
- **Deduplicate** using the unique ID
- **Apply temporal decay** based on the timestamp

### Search Modes

The library supports three search modes:

1. **Lexical** - Classic BM25 keyword search (exact terms, stemming, proximity)
2. **Semantic** - Vector similarity search (conceptual matching, synonyms, paraphrases)
3. **Hybrid** - Reciprocal Rank Fusion combining both (best of both worlds)

### Architecture Layers

```
Your Domain Model (Session, Document, etc.)
         ↓
    ISearchable Adapter
         ↓
┌────────┴────────┐
│  LuceneSearch   │  VectorSearch  │
│  (Keywords)     │  (Embeddings)  │
└────────┬────────┘
         ↓
    HybridSearcher (RRF Fusion)
         ↓
     Search Results
```

## Your First Search Index

Let's build a simple document search from scratch.

### Step 1: Define Your Document Model

```csharp
using ZeroProximity.VectorizedContentIndexer.Models;

public sealed record Article : ISearchable
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string Author { get; init; }
    public required DateTime PublishedAt { get; init; }

    // ISearchable implementation
    public string GetSearchableText() => $"{Title}\n\n{Content}";
    public DateTime GetTimestamp() => PublishedAt;
}
```

### Step 2: Initialize the Embedding Provider

```csharp
using ZeroProximity.VectorizedContentIndexer.Embeddings;

// Creates ONNX embedding provider with embedded MiniLM-L6-v2 model
// Falls back to hash-based provider if ONNX fails
var embeddings = await EmbeddingProviderFactory.CreateAsync();

Console.WriteLine($"Using: {embeddings.GetType().Name}");
// Output: "OnnxEmbeddingProvider" (if successful)
```

### Step 3: Create Search Engines

```csharp
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Search.Lucene;
using ZeroProximity.VectorizedContentIndexer.Search.Vector;

// Lucene for keyword search
var luceneEngine = new LuceneSearchEngine<Article>(
    indexPath: "./data/index/lucene",
    mapper: new DefaultLuceneDocumentMapper<Article>()
);

// Vector search for semantic similarity
var vectorEngine = new VectorSearchEngine<Article>(
    indexPath: "./data/index/vector",
    embedder: embeddings,
    precision: VectorPrecision.Float16  // 50% storage savings
);

// Hybrid search combining both
var searchEngine = new HybridSearcher<Article>(
    luceneEngine: luceneEngine,
    vectorEngine: vectorEngine,
    lexicalWeight: 0.5,
    semanticWeight: 0.5,
    rrfK: 60
);
```

### Step 4: Index Some Documents

```csharp
var articles = new[]
{
    new Article
    {
        Id = "1",
        Title = "Getting Started with Async/Await in C#",
        Content = "Async and await keywords make asynchronous programming in C# much easier...",
        Author = "John Doe",
        PublishedAt = DateTime.Parse("2024-01-15")
    },
    new Article
    {
        Id = "2",
        Title = "Understanding LINQ Performance",
        Content = "LINQ queries can impact performance if not used carefully. This guide covers optimization strategies...",
        Author = "Jane Smith",
        PublishedAt = DateTime.Parse("2024-02-20")
    },
    new Article
    {
        Id = "3",
        Title = "Best Practices for Dependency Injection",
        Content = "Dependency injection is a fundamental pattern in modern C# development...",
        Author = "Bob Johnson",
        PublishedAt = DateTime.Parse("2024-03-10")
    }
};

// Index all articles
await searchEngine.IndexManyAsync(articles);

Console.WriteLine($"Indexed {await searchEngine.GetCountAsync()} articles");
```

## Indexing Documents

### Single Document Indexing

```csharp
await searchEngine.IndexAsync(article);
```

### Batch Indexing

Batch indexing is more efficient for multiple documents:

```csharp
var articles = LoadArticlesFromDatabase();
await searchEngine.IndexManyAsync(articles);
```

### Update vs. Insert

The library automatically handles updates. Indexing a document with an existing ID will replace the old version:

```csharp
// Index original
await searchEngine.IndexAsync(new Article { Id = "1", Title = "Original" });

// Update (same ID)
await searchEngine.IndexAsync(new Article { Id = "1", Title = "Updated" });

// Only one document with Id="1" exists
```

### Deleting Documents

```csharp
// Delete single document
await searchEngine.DeleteAsync("article-id");

// Delete multiple documents
await searchEngine.DeleteManyAsync(new[] { "id1", "id2", "id3" });

// Clear entire index
await searchEngine.ClearAsync();
```

## Searching

### Basic Search (Hybrid Mode)

```csharp
var results = await searchEngine.SearchAsync("async programming patterns");

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Title: {result.Document.Title}");
    Console.WriteLine($"Author: {result.Document.Author}");
    Console.WriteLine($"Highlight: {result.Highlight}");
    Console.WriteLine("---");
}
```

### Search Mode Comparison

```csharp
var query = "asynchronous code optimization";

// Lexical: Finds documents with exact keywords
var lexical = await searchEngine.SearchAsync(query, mode: SearchMode.Lexical);

// Semantic: Finds conceptually similar documents
var semantic = await searchEngine.SearchAsync(query, mode: SearchMode.Semantic);

// Hybrid: Best of both
var hybrid = await searchEngine.SearchAsync(query, mode: SearchMode.Hybrid);
```

#### When to Use Each Mode

**Lexical (BM25)**:
- Searching for specific terms, code, or identifiers
- Boolean queries (AND, OR, NOT)
- Exact phrase matching
- Fast search on large datasets

**Semantic (Vector)**:
- Finding similar concepts
- Handling synonyms and paraphrases
- Cross-lingual search (with multilingual models)
- RAG context retrieval

**Hybrid (RRF)**:
- General-purpose search
- Best precision and recall
- When you want both exact matches and similar content

### Limiting Results

```csharp
// Get top 5 results
var results = await searchEngine.SearchAsync("query", maxResults: 5);

// Get top 20 results
var results = await searchEngine.SearchAsync("query", maxResults: 20);
```

### Understanding Search Results

```csharp
public record SearchResult<TDocument>
{
    public TDocument Document { get; init; }       // Your original document
    public double Score { get; init; }              // Relevance score (higher is better)
    public string? Highlight { get; init; }         // Highlighted snippet
    public double? DecayFactor { get; init; }       // Temporal decay multiplier (if enabled)
}
```

## Common Patterns

### Pattern 1: RAG (Retrieval-Augmented Generation)

```csharp
public async Task<string> AskQuestionWithContext(string userQuestion)
{
    // Retrieve relevant context
    var results = await searchEngine.SearchAsync(
        userQuestion,
        maxResults: 5,
        mode: SearchMode.Semantic
    );

    // Build context from top results
    var context = string.Join("\n\n", results.Select(r =>
        $"Source: {r.Document.Title}\n{r.Document.Content}"));

    // Send to LLM
    var prompt = $"""
        Answer the user's question based on the provided context.

        ## Context
        {context}

        ## Question
        {userQuestion}

        ## Answer
        """;

    return await CallLLM(prompt);
}
```

### Pattern 2: Faceted Search with Metadata

For more complex filtering, implement `IDocument`:

```csharp
public sealed record Article : IDocument
{
    // ... ISearchable properties ...

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["Author"] = Author,
        ["Category"] = Category,
        ["Tags"] = string.Join(",", Tags),
        ["PublishedYear"] = PublishedAt.Year
    };
}

// Then create a custom Lucene mapper to enable field-specific queries
// See: docs/advanced/custom-field-mapping.md
```

### Pattern 3: Session-Scoped Search

```csharp
// Create separate search engines for different contexts
public class MultiIndexService
{
    private readonly Dictionary<string, ISearchEngine<Article>> _indexes = new();
    private readonly IEmbeddingProvider _embeddings;

    public async Task<ISearchEngine<Article>> GetOrCreateIndex(string userId)
    {
        if (!_indexes.TryGetValue(userId, out var engine))
        {
            engine = new VectorSearchEngine<Article>(
                indexPath: $"./data/users/{userId}/index",
                embedder: _embeddings
            );
            _indexes[userId] = engine;
        }
        return engine;
    }
}
```

### Pattern 4: Incremental Indexing

```csharp
public async Task IndexNewArticles()
{
    var lastIndexedTime = await GetLastIndexedTimestamp();
    var newArticles = await database.GetArticlesSince(lastIndexedTime);

    if (newArticles.Any())
    {
        await searchEngine.IndexManyAsync(newArticles);
        await SaveLastIndexedTimestamp(DateTime.UtcNow);
        Console.WriteLine($"Indexed {newArticles.Count} new articles");
    }
}
```

### Pattern 5: Disposing Resources

```csharp
// Search engines implement IAsyncDisposable
await using var searchEngine = new HybridSearcher<Article>(...);

// Use the search engine
await searchEngine.IndexAsync(article);

// Automatically disposed at end of scope
```

## Next Steps

Now that you've mastered the basics, explore advanced topics:

1. **[Hierarchical Documents](advanced/hierarchical-documents.md)** - Parent-child relationships (sessions → messages)
2. **[Custom Field Mapping](advanced/custom-field-mapping.md)** - Fine-grained Lucene control for filtering and boosting
3. **[Temporal Decay](advanced/temporal-decay.md)** - Boost recent content automatically
4. **[Performance Tuning](advanced/performance-tuning.md)** - Optimize for your specific workload
5. **[API Reference](api/README.md)** - Complete API documentation
6. **[Architecture](architecture.md)** - Deep dive into component design

## Troubleshooting

### Issue: ONNX model fails to load

```csharp
// The factory gracefully falls back to hash-based provider
var embeddings = await EmbeddingProviderFactory.CreateAsync();

if (embeddings is HashEmbeddingProvider)
{
    Console.WriteLine("Warning: Using hash-based embeddings (semantic search quality reduced)");
    // Consider investigating ONNX runtime issues
}
```

### Issue: Out of memory during indexing

```csharp
// Index in smaller batches
const int batchSize = 100;
for (int i = 0; i < articles.Count; i += batchSize)
{
    var batch = articles.Skip(i).Take(batchSize);
    await searchEngine.IndexManyAsync(batch);

    // Optional: Force garbage collection between batches
    GC.Collect();
}
```

### Issue: Slow search performance

```csharp
// 1. Optimize the index (merges segments)
await searchEngine.OptimizeAsync();

// 2. Reduce max results
var results = await searchEngine.SearchAsync(query, maxResults: 10); // Instead of 100

// 3. Use lexical-only for large indexes
var results = await searchEngine.SearchAsync(query, mode: SearchMode.Lexical);
```

## Sample Code

Complete working examples are available in the [samples](../samples/) directory:

- **[RagExample](../samples/RagExample/)** - Pure semantic search for RAG
- **[AgentSessionExample](../samples/AgentSessionExample/)** - Hybrid search with hierarchical documents

## Questions?

- Check the [API Documentation](api/README.md)
- See [Architecture Overview](architecture.md)
- Ask on [GitHub Discussions](https://github.com/yourusername/vectorized-content-indexer/discussions)
