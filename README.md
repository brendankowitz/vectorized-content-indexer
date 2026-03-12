# ZeroProximity.VectorizedContentIndexer

A high-performance hybrid content indexing library for .NET that combines keyword search (Lucene.NET BM25), vector search (ONNX embeddings), and hybrid search (RRF fusion) with an embedded MiniLM-L6-v2 model.

## Features

- **Hybrid Search** - Best-of-both-worlds combining BM25 keyword search with semantic vector search using Reciprocal Rank Fusion (RRF)
- **Embedded ML Model** - Ships with MiniLM-L6-v2 (384 dimensions) ONNX model - no external downloads or API keys required
- **GPU Acceleration** - DirectML support for 10-20x faster embeddings on compatible hardware
- **Custom Vector Index (AJVI)** - Memory-mapped binary format with Float16 precision support for 50% storage savings
- **Flexible Document Model** - Generic interfaces (ISearchable, IDocument) work with any content type
- **Hierarchical Documents** - Built-in support for parent-child relationships (e.g., sessions containing messages)
- **Temporal Relevance** - Exponential decay with configurable half-life for time-based relevance boosting
- **Thread-Safe** - Concurrent reads with serialized writes
- **Production-Ready** - Extracted and refined from agent-session-search-tools

## Quick Start

### Installation

```bash
dotnet add package ZeroProximity.VectorizedContentIndexer
```

### Basic Usage

```csharp
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Models;

// Define your searchable content
public record DocumentChunk : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => CreatedAt;
}

// Initialize embedding provider (uses embedded ONNX model)
var embeddings = await EmbeddingProviderFactory.CreateAsync();

// Create hybrid search engine
var searchEngine = new HybridSearcher<DocumentChunk>(
    luceneEngine: new LuceneSearchEngine<DocumentChunk>("./index/lucene"),
    vectorEngine: new VectorSearchEngine<DocumentChunk>("./index/vector", embeddings),
    lexicalWeight: 0.5,
    semanticWeight: 0.5
);

// Index documents
await searchEngine.IndexAsync(new DocumentChunk
{
    Id = "doc1",
    Content = "How to optimize async performance in C#",
    CreatedAt = DateTime.UtcNow
});

// Search with hybrid mode (default)
var results = await searchEngine.SearchAsync("async optimization", maxResults: 10);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3} - {result.Document.Content}");
}
```

## Use Cases

### RAG Systems
Build retrieval-augmented generation pipelines with semantic document chunking and precise context retrieval.

```csharp
var chunks = await searchEngine.SearchAsync(userQuery, maxResults: 5, mode: SearchMode.Semantic);
var context = string.Join("\n\n", chunks.Select(r => r.Document.Content));
var prompt = $"Context:\n{context}\n\nQuestion: {userQuery}\n\nAnswer:";
```

### Semantic Search
Find conceptually similar content even when exact keywords don't match.

```csharp
var results = await searchEngine.SearchAsync(
    "handling authentication errors",
    mode: SearchMode.Semantic
);
```

### Agent Conversation History
Index and search agent sessions with message-level granularity and context expansion.

```csharp
public class SessionDocument : IHierarchicalDocument<Message>
{
    // Session contains multiple messages
    public IReadOnlyList<Message> GetChildren() => session.Messages;
    public IReadOnlyList<Message> GetChildrenBefore(string messageId, int count) { ... }
}
```

### Knowledge Management
Combine exact keyword matching with semantic similarity for comprehensive search.

```csharp
// Hybrid search finds both exact matches AND semantically similar content
var results = await searchEngine.SearchAsync("database migration", mode: SearchMode.Hybrid);
```

## Documentation

- [Getting Started Guide](docs/getting-started.md) - Step-by-step tutorial
- [API Documentation](docs/api/README.md) - Complete API reference
- [Architecture Overview](docs/architecture.md) - Component design and data flow
- [Advanced Topics](docs/advanced/) - Hierarchical documents, custom field mapping, performance tuning
- [Migration Guide](docs/migration.md) - Migrating from agent-session-search-tools
- [Samples](samples/) - RAG and Agent Session examples

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     ISearchEngine<T>                        │
├─────────────────────────────────────────────────────────────┤
│  HybridSearcher<T>  │  LuceneSearchEngine<T>  │  VectorSearchEngine<T>  │
│  (RRF Fusion)       │  (BM25 Keyword)         │  (Semantic Search)      │
└──────────┬──────────┴──────────┬──────────────┴──────────┬──────────────┘
           │                     │                          │
           │              ┌──────▼──────┐          ┌────────▼────────┐
           │              │ Lucene.NET  │          │ AJVI Index      │
           │              │ Index       │          │ (Memory-Mapped) │
           │              └─────────────┘          └────────┬────────┘
           │                                                │
           └────────────────────────────────────────────────▼────────┐
                                                    │ IEmbeddingProvider │
                                                    ├────────────────────┤
                                                    │ OnnxEmbedding      │
                                                    │ (MiniLM-L6-v2)     │
                                                    └────────────────────┘
```

### Key Components

1. **ISearchable / IDocument** - Minimal interfaces for indexable content
2. **IEmbeddingProvider** - Pluggable embedding generation (ONNX, Hash, or custom)
3. **LuceneSearchEngine** - BM25 keyword search with Lucene.NET
4. **VectorSearchEngine** - Semantic search using AJVI custom vector index
5. **HybridSearcher** - RRF fusion combining lexical and semantic results
6. **AJVI Index** - Custom binary format with Float16 support and memory-mapped I/O
7. **DecayCalculator** - Temporal relevance decay for fresh content boosting

## Performance

### Benchmarks (on typical hardware)

**Indexing:**
- Embed single document: ~15ms CPU, ~2ms GPU (DirectML)
- Index 100 documents: ~1.5s CPU, ~300ms GPU
- Lucene index 1000 documents: ~50ms

**Searching:**
- Lexical search (10K documents): ~20ms
- Semantic search (100K vectors): ~80ms
- Hybrid search (10K documents): ~30ms (parallel execution)

**Storage:**
- AJVI Float16: ~825 bytes/vector (384 dimensions)
- AJVI Float32: ~1,593 bytes/vector
- Lucene: ~500-2000 bytes/document (varies by content)

### Scalability

- **< 100K vectors**: Excellent performance with brute-force search
- **100K - 1M vectors**: Good performance, consider optimization strategies
- **> 1M vectors**: Consider approximate nearest neighbor algorithms (future enhancement)

## Configuration

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.IndexPath = "./index";
    options.DefaultMode = SearchMode.Hybrid;
    options.Precision = VectorPrecision.Float16;  // 50% storage savings
    options.LexicalWeight = 0.5;
    options.SemanticWeight = 0.5;
    options.RrfK = 60;  // Reciprocal rank fusion constant
    options.DecayHalfLifeDays = 90.0;  // Temporal decay
});
```

## Requirements

- .NET 9.0 or later
- Windows, Linux, or macOS
- Optional: DirectML-compatible GPU for acceleration

## Dependencies

- Lucene.Net 4.8.0-beta00016
- Microsoft.ML.OnnxRuntime.DirectML 1.20.0
- Microsoft.ML.Tokenizers 1.0.0
- System.Numerics.Tensors 10.0.1

All dependencies use permissive licenses (Apache 2.0 / MIT).

## License

MIT License - see LICENSE file for details.

## Credits

This library extracts and generalizes production-ready search components from [agent-session-search-tools](https://github.com/yourusername/agent-session-search-tools), refined for broader use cases.

### Model Attribution

- **Embedding Model**: [sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
  - 384 dimensions, 22.7M parameters
  - Apache 2.0 License
  - Trained by [Nils Reimers](https://www.nreimers.de/)

### Inspired By

- **Reciprocal Rank Fusion (RRF)**: [Cormack et al., SIGIR 2009](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf)
- **Elasticsearch**: Hybrid search implementation
- **Vespa.ai**: Multi-phase retrieval patterns

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Support

- Issues: [GitHub Issues](https://github.com/yourusername/vectorized-content-indexer/issues)
- Discussions: [GitHub Discussions](https://github.com/yourusername/vectorized-content-indexer/discussions)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release notes.
