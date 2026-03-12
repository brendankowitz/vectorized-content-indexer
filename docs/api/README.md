# API Documentation

Complete API reference for ZeroProximity.VectorizedContentIndexer.

## Table of Contents

1. [Core Interfaces](#core-interfaces)
2. [Search Engines](#search-engines)
3. [Embedding Providers](#embedding-providers)
4. [Models and Results](#models-and-results)
5. [Configuration](#configuration)
6. [Utilities](#utilities)

## Core Interfaces

### ISearchable

The minimal interface for indexable content.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Models;

public interface ISearchable
{
    string Id { get; }
    string GetSearchableText();
    DateTime GetTimestamp();
}
```

**Properties:**

- `Id` - Unique identifier for deduplication and retrieval
- `GetSearchableText()` - Returns the text to be indexed and searched
- `GetTimestamp()` - Returns the timestamp for temporal decay and sorting

**Example:**

```csharp
public record BlogPost : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime PublishedAt { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => PublishedAt;
}
```

---

### IDocument

Extended interface with metadata support for advanced Lucene field mapping.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Models;

public interface IDocument : ISearchable
{
    IDictionary<string, object> GetMetadata();
}
```

**Methods:**

- `GetMetadata()` - Returns a dictionary of field names to values for Lucene indexing

**Supported Metadata Types:**

- `string` - Text fields (analyzed or exact match)
- `int`, `long`, `double` - Numeric fields for range queries
- `bool` - Boolean filters
- `DateTime` - Date/time values
- `IEnumerable<T>` - Multi-valued fields

**Example:**

```csharp
public class Article : IDocument
{
    public string Id => article.Id;
    public string GetSearchableText() => $"{article.Title}\n{article.Body}";
    public DateTime GetTimestamp() => article.PublishedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["Author"] = article.Author,
        ["Category"] = article.Category,
        ["Tags"] = string.Join(",", article.Tags),
        ["ViewCount"] = article.ViewCount,
        ["IsFeatured"] = article.IsFeatured
    };
}
```

---

### IHierarchicalDocument\<TChild\>

Interface for documents containing child documents (parent-child relationships).

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Models;

public interface IHierarchicalDocument<TChild> : IDocument
    where TChild : ISearchable
{
    IReadOnlyList<TChild> GetChildren();
    TChild? GetChildById(string childId);
    IReadOnlyList<TChild> GetChildrenBefore(string childId, int count);
    IReadOnlyList<TChild> GetChildrenAfter(string childId, int count);
}
```

**Methods:**

- `GetChildren()` - Returns all child documents
- `GetChildById(string)` - Retrieves a specific child by ID (optional)
- `GetChildrenBefore(string, int)` - Gets N children before the specified child (optional, for context expansion)
- `GetChildrenAfter(string, int)` - Gets N children after the specified child (optional, for context expansion)

**Example:**

```csharp
public class SessionDocument : IHierarchicalDocument<Message>
{
    private readonly Session _session;

    public IReadOnlyList<Message> GetChildren() => _session.Messages;

    public Message? GetChildById(string childId) =>
        _session.Messages.FirstOrDefault(m => m.Id == childId);

    public IReadOnlyList<Message> GetChildrenBefore(string childId, int count)
    {
        var index = _session.Messages.FindIndex(m => m.Id == childId);
        return index > 0
            ? _session.Messages.Take(index).TakeLast(count).ToList()
            : Array.Empty<Message>();
    }

    public IReadOnlyList<Message> GetChildrenAfter(string childId, int count)
    {
        var index = _session.Messages.FindIndex(m => m.Id == childId);
        return index >= 0 && index < _session.Messages.Count - 1
            ? _session.Messages.Skip(index + 1).Take(count).ToList()
            : Array.Empty<Message>();
    }
}
```

See [Hierarchical Documents](../advanced/hierarchical-documents.md) for detailed usage.

---

## Search Engines

### ISearchEngine\<TDocument\>

The core search engine interface.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search;

public interface ISearchEngine<TDocument> : IAsyncDisposable
    where TDocument : ISearchable
{
    Task IndexAsync(TDocument document, CancellationToken cancellationToken = default);
    Task IndexManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken = default);
    Task<int> DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}
```

**Implementations:**

1. `LuceneSearchEngine<T>` - BM25 keyword search
2. `VectorSearchEngine<T>` - Semantic vector search
3. `HybridSearcher<T>` - RRF fusion of both

---

### LuceneSearchEngine\<TDocument\>

BM25 keyword search using Lucene.NET.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search.Lucene;

public sealed class LuceneSearchEngine<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    public LuceneSearchEngine(
        string indexPath,
        ILuceneDocumentMapper<TDocument>? mapper = null,
        ILogger<LuceneSearchEngine<TDocument>>? logger = null)
}
```

**Constructor Parameters:**

- `indexPath` - Directory path for Lucene index
- `mapper` - Custom document mapper (defaults to `DefaultLuceneDocumentMapper<T>`)
- `logger` - Optional logger for diagnostics

**Features:**

- Full-text search with BM25 ranking
- Query syntax: `"exact phrase"`, `term1 AND term2`, `term1 OR term2`, `NOT term`
- Stemming and stop word removal
- Field boosting (via custom mapper)
- Thread-safe concurrent reads

**Example:**

```csharp
var luceneEngine = new LuceneSearchEngine<Article>(
    indexPath: "./data/lucene",
    mapper: new CustomArticleMapper(),
    logger: loggerFactory.CreateLogger<LuceneSearchEngine<Article>>()
);

await luceneEngine.IndexAsync(article);

// Boolean query
var results = await luceneEngine.SearchAsync("async AND performance");

// Phrase query
var results = await luceneEngine.SearchAsync("\"dependency injection\"");
```

---

### VectorSearchEngine\<TDocument\>

Semantic search using ONNX embeddings and AJVI index.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search.Vector;

public sealed class VectorSearchEngine<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    public VectorSearchEngine(
        string indexPath,
        IEmbeddingProvider embedder,
        VectorPrecision precision = VectorPrecision.Float16,
        ILogger<VectorSearchEngine<TDocument>>? logger = null)
}
```

**Constructor Parameters:**

- `indexPath` - Directory path for AJVI index
- `embedder` - Embedding provider (ONNX, Hash, or custom)
- `precision` - Vector precision (Float16 or Float32)
- `logger` - Optional logger

**Features:**

- Semantic similarity search via cosine similarity
- Float16 precision (50% storage savings)
- Memory-mapped file I/O
- SHA256 content deduplication
- Brute-force search (suitable for < 1M vectors)

**Example:**

```csharp
var embeddings = await EmbeddingProviderFactory.CreateAsync();

var vectorEngine = new VectorSearchEngine<Article>(
    indexPath: "./data/vector",
    embedder: embeddings,
    precision: VectorPrecision.Float16
);

await vectorEngine.IndexAsync(article);

// Semantic search (finds conceptually similar content)
var results = await vectorEngine.SearchAsync("how to improve async performance");
```

---

### HybridSearcher\<TDocument\>

Combines Lucene and Vector search using Reciprocal Rank Fusion (RRF).

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search;

public sealed class HybridSearcher<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    public HybridSearcher(
        ISearchEngine<TDocument> luceneEngine,
        ISearchEngine<TDocument> vectorEngine,
        double lexicalWeight = 0.5,
        double semanticWeight = 0.5,
        int rrfK = 60,
        ILogger<HybridSearcher<TDocument>>? logger = null)
}
```

**Constructor Parameters:**

- `luceneEngine` - BM25 search engine
- `vectorEngine` - Semantic search engine
- `lexicalWeight` - Weight for lexical results (0.0 - 1.0)
- `semanticWeight` - Weight for semantic results (0.0 - 1.0)
- `rrfK` - RRF constant (typically 60, controls rank decay)
- `logger` - Optional logger

**RRF Formula:**

```
score(d) = lexicalWeight × (1 / (k + lexical_rank(d))) +
           semanticWeight × (1 / (k + semantic_rank(d)))
```

**Features:**

- Best-of-both-worlds: exact matches + semantic similarity
- Robust to score distribution differences
- No score normalization required
- Parallel search execution

**Example:**

```csharp
var hybridSearcher = new HybridSearcher<Article>(
    luceneEngine: luceneEngine,
    vectorEngine: vectorEngine,
    lexicalWeight: 0.6,      // Favor keyword matches slightly
    semanticWeight: 0.4,
    rrfK: 60
);

// Hybrid search
var results = await hybridSearcher.SearchAsync("async optimization", mode: SearchMode.Hybrid);

// Override mode at search time
var lexicalOnly = await hybridSearcher.SearchAsync("query", mode: SearchMode.Lexical);
var semanticOnly = await hybridSearcher.SearchAsync("query", mode: SearchMode.Semantic);
```

---

## Embedding Providers

### IEmbeddingProvider

Interface for text embedding generation.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

public interface IEmbeddingProvider
{
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<float[][]> EmbedManyAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
}
```

**Properties:**

- `Dimensions` - Number of dimensions in generated vectors (e.g., 384 for MiniLM-L6-v2)

**Methods:**

- `EmbedAsync(string)` - Generates embedding for a single text
- `EmbedManyAsync(IEnumerable<string>)` - Batch embedding generation

**Implementations:**

1. `OnnxEmbeddingProvider` - ONNX Runtime with MiniLM-L6-v2
2. `HashEmbeddingProvider` - Fallback hash-based embeddings (development only)

---

### EmbeddingProviderFactory

Factory for creating embedding providers with graceful degradation.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

public static class EmbeddingProviderFactory
{
    public static async Task<IEmbeddingProvider> CreateAsync(
        string? modelPath = null,
        CancellationToken cancellationToken = default);

    public static async Task<IEmbeddingProvider> TryCreateAsync(
        string? modelPath = null,
        CancellationToken cancellationToken = default);
}
```

**Methods:**

- `CreateAsync()` - Creates ONNX provider, throws if unavailable
- `TryCreateAsync()` - Creates ONNX provider, falls back to HashEmbeddingProvider on failure

**Example:**

```csharp
// Recommended: Graceful fallback
var embeddings = await EmbeddingProviderFactory.TryCreateAsync();

if (embeddings is OnnxEmbeddingProvider)
{
    Console.WriteLine("Using ONNX embeddings (high quality)");
}
else
{
    Console.WriteLine("Warning: Using hash-based embeddings (lower quality)");
}

// Strict: Require ONNX
var embeddings = await EmbeddingProviderFactory.CreateAsync();
// Throws if ONNX unavailable
```

---

### OnnxEmbeddingProvider

High-quality embeddings using ONNX Runtime and MiniLM-L6-v2.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

public sealed class OnnxEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 384;

    public OnnxEmbeddingProvider(
        string? modelPath = null,
        bool useGpu = true)
}
```

**Constructor Parameters:**

- `modelPath` - Path to ONNX model file (uses embedded model if null)
- `useGpu` - Enable DirectML GPU acceleration (default: true)

**Features:**

- MiniLM-L6-v2 (384 dimensions, 22.7M parameters)
- DirectML GPU acceleration (10-20x faster)
- Automatic CPU fallback
- Thread-safe with internal locking
- Mean pooling with attention mask

**Performance:**

- CPU: ~15ms per embedding
- GPU (DirectML): ~2ms per embedding
- Batch (32 texts): ~300ms CPU, ~50ms GPU

---

### HashEmbeddingProvider

Development fallback using deterministic hashing.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

public sealed class HashEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 384;

    public HashEmbeddingProvider(int dimensions = 384)
}
```

**Features:**

- Deterministic embeddings (same text → same vector)
- No ML dependencies
- Fast (~1ms per embedding)
- NOT suitable for production semantic search

**Use Cases:**

- Development/testing when ONNX unavailable
- Keyword-only search (lexical mode)
- Placeholder until real embeddings available

---

## Models and Results

### SearchResult\<TDocument\>

Result from a search operation.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Models;

public sealed record SearchResult<TDocument>(
    TDocument Document,
    double Score,
    string? Highlight = null,
    double? DecayFactor = null
) where TDocument : ISearchable;
```

**Properties:**

- `Document` - The original document that matched
- `Score` - Relevance score (higher is better, typically 0.0 - 1.0)
- `Highlight` - Text snippet showing match context (optional)
- `DecayFactor` - Temporal decay multiplier if decay is enabled (optional)

**Example:**

```csharp
var results = await searchEngine.SearchAsync("query");

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Document: {result.Document.GetSearchableText()}");

    if (result.Highlight != null)
        Console.WriteLine($"Highlight: {result.Highlight}");

    if (result.DecayFactor != null)
        Console.WriteLine($"Decay: {result.DecayFactor:F3}");
}
```

---

### SearchMode

Enumeration of search modes.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search;

public enum SearchMode
{
    Lexical,    // BM25 keyword search only
    Semantic,   // Vector similarity search only
    Hybrid      // RRF fusion of both (default)
}
```

**Usage:**

```csharp
// Explicit mode selection
var results = await engine.SearchAsync("query", mode: SearchMode.Semantic);

// Default is Hybrid
var results = await engine.SearchAsync("query");
```

---

### VectorPrecision

Enumeration of vector storage precision.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search;

public enum VectorPrecision
{
    Float16,    // 16-bit floating point (50% storage savings)
    Float32     // 32-bit floating point (standard precision)
}
```

**Comparison:**

| Precision | Bytes per Vector (384 dims) | Quality Impact |
|-----------|---------------------------|----------------|
| Float16   | 768 bytes (384 × 2)       | Minimal (< 1%) |
| Float32   | 1,536 bytes (384 × 4)     | None           |

**Recommendation:** Use Float16 unless you observe quality degradation.

---

## Configuration

### SearchEngineOptions

Configuration options for search engines.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Search;

public sealed class SearchEngineOptions
{
    public string IndexPath { get; set; } = "./data/index";
    public SearchMode DefaultMode { get; set; } = SearchMode.Hybrid;
    public VectorPrecision Precision { get; set; } = VectorPrecision.Float16;
    public double LexicalWeight { get; set; } = 0.5;
    public double SemanticWeight { get; set; } = 0.5;
    public int RrfK { get; set; } = 60;
    public bool ApplyDecay { get; set; } = false;
    public double DecayHalfLifeDays { get; set; } = 90.0;
}
```

**Properties:**

- `IndexPath` - Base directory for index files
- `DefaultMode` - Default search mode
- `Precision` - Vector precision (Float16 or Float32)
- `LexicalWeight` - Weight for BM25 results in hybrid mode
- `SemanticWeight` - Weight for vector results in hybrid mode
- `RrfK` - Reciprocal rank fusion constant
- `ApplyDecay` - Enable temporal relevance decay
- `DecayHalfLifeDays` - Half-life for exponential decay (if enabled)

**Example (ASP.NET Core):**

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.IndexPath = Configuration["Search:IndexPath"];
    options.DefaultMode = SearchMode.Hybrid;
    options.Precision = VectorPrecision.Float16;
    options.LexicalWeight = 0.6;  // Favor keywords slightly
    options.SemanticWeight = 0.4;
    options.ApplyDecay = true;
    options.DecayHalfLifeDays = 30.0;  // Boost content < 30 days old
});
```

---

## Utilities

### DecayCalculator

Calculates temporal relevance decay.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Utilities;

public static class DecayCalculator
{
    public static double CalculateDecayFactor(
        DateTime timestamp,
        DateTime now,
        double halfLifeDays);

    public static DecayCategory ClassifyDecay(double decayFactor);
}

public enum DecayCategory
{
    Fresh,      // decay > 0.9 (very recent)
    Good,       // 0.7 - 0.9
    Aging,      // 0.4 - 0.7
    Decaying,   // 0.1 - 0.4
    Expiring    // < 0.1 (very old)
}
```

**Formula:**

```
decay = 0.5 ^ (age_days / half_life_days)
```

**Example:**

```csharp
var now = DateTime.UtcNow;
var documentAge = now - document.GetTimestamp();

var decay = DecayCalculator.CalculateDecayFactor(
    document.GetTimestamp(),
    now,
    halfLifeDays: 90.0
);

var category = DecayCalculator.ClassifyDecay(decay);

Console.WriteLine($"Decay: {decay:F3} ({category})");
// Output: "Decay: 0.707 (Good)"
```

See [Temporal Decay](../advanced/temporal-decay.md) for detailed usage.

---

### ContentUtils

Utility methods for content validation and sanitization.

```csharp
namespace ZeroProximity.VectorizedContentIndexer.Utilities;

public static class ContentUtils
{
    public static void ValidatePath(string path, string? basePath = null);
    public static void ValidateFileSize(string filePath, long maxSizeBytes = 10_485_760);
    public static string SanitizeQuery(string query);
    public static string NormalizeText(string text);
}
```

**Methods:**

- `ValidatePath()` - Prevents directory traversal attacks
- `ValidateFileSize()` - Ensures files don't exceed size limits
- `SanitizeQuery()` - Escapes special characters in queries
- `NormalizeText()` - Normalizes whitespace and casing

---

## Extension Methods

### Dependency Injection Extensions

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class VectorizedContentIndexerServiceCollectionExtensions
{
    public static IServiceCollection AddVectorizedContentIndexer<TDocument>(
        this IServiceCollection services,
        Action<SearchEngineOptions>? configure = null)
        where TDocument : ISearchable;
}
```

**Example:**

```csharp
services.AddVectorizedContentIndexer<Article>(options =>
{
    options.IndexPath = "./data/articles";
    options.DefaultMode = SearchMode.Hybrid;
    options.Precision = VectorPrecision.Float16;
});

// Inject ISearchEngine<Article>
public class ArticleService
{
    private readonly ISearchEngine<Article> _searchEngine;

    public ArticleService(ISearchEngine<Article> searchEngine)
    {
        _searchEngine = searchEngine;
    }
}
```

---

## See Also

- [Getting Started Guide](../getting-started.md)
- [Architecture Overview](../architecture.md)
- [Advanced Topics](../advanced/)
- [Sample Applications](../../samples/)
