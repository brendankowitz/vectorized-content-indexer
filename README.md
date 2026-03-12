# 🔍 ZeroProximity.VectorizedContentIndexer

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-ZeroProximity.VectorizedContentIndexer-blue?logo=nuget)](https://www.nuget.org/packages/ZeroProximity.VectorizedContentIndexer)

*A high-performance hybrid content indexing library for .NET — combining BM25 keyword search, semantic vector search, and RRF fusion with a fully embedded ONNX model. Zero config. Works offline.*

---

## 📋 Overview

**ZeroProximity.VectorizedContentIndexer** brings production-grade hybrid search to any .NET 9 application without requiring external services, API keys, or model downloads. It ships an embedded MiniLM-L6-v2 ONNX model (384 dimensions, 22.7M parameters) and a custom binary vector index format designed for memory-efficient, high-throughput retrieval.

### Why this library?

| Capability | ZeroProximity.VectorizedContentIndexer |
|---|---|
| Embedding model | Embedded MiniLM-L6-v2 — no download, no API key |
| Vector index format | Custom AJVI binary format — append-only, memory-mapped, Float16 |
| Storage efficiency | ~825 bytes/vector at Float16 (384 dimensions) |
| Search modes | Lexical (BM25), Semantic, and Hybrid (RRF) |
| Document model | Generic `ISearchable` / `IDocument` — works with any content type |
| Hierarchical docs | Built-in parent-child relationship support |
| GPU acceleration | DirectML support for 10–20x faster embedding generation |
| Time-based ranking | Exponential temporal decay (configurable half-life) |
| Dependencies | All Apache 2.0 / MIT — fully permissive |

---

## ✨ Key Features

### Search Modes

- **Lexical Search** — Lucene.NET BM25 keyword search with inverted index; fast and precise for exact term matching
- **Semantic Search** — Dense vector search using ONNX-generated embeddings; finds conceptually similar content even without shared keywords
- **Hybrid Search** — Reciprocal Rank Fusion (RRF) combines lexical and semantic rankings into a single, balanced result set

### Document Model

- **`ISearchable`** — Minimal interface: provide text and a timestamp, the library handles the rest
- **`IDocument`** — Extended interface for richer content with ID, metadata, and field control
- **`IHierarchicalDocument<TChild>`** — First-class support for parent-child structures (e.g., a session containing messages), enabling child-level indexing with parent-level context retrieval

### Performance

- Parallel lexical + semantic query execution in hybrid mode
- Memory-mapped AJVI index for low-latency random reads at scale
- SIMD cosine similarity (System.Numerics intrinsics)
- Float16 storage cuts vector memory footprint by ~50% vs Float32
- DirectML GPU acceleration for batch embedding workloads

### Thread Safety

- Concurrent reads fully supported
- Writes are serialized — safe for multi-threaded indexing pipelines

---

## 📦 Installation

```bash
dotnet add package ZeroProximity.VectorizedContentIndexer --prerelease
```

> This package is currently in prerelease (`1.0.0-beta.1`). The `--prerelease` flag is required until the stable release.

---

## 🚀 Quick Start

### Basic: Hybrid Search with ISearchable

```csharp
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Models;

// Define your searchable content type
public record DocumentChunk : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => CreatedAt;
}

// Initialize the embedded ONNX embedding provider (no download required)
IEmbeddingProvider embeddings = await EmbeddingProviderFactory.CreateAsync();

// Compose a hybrid search engine
ISearchEngine<DocumentChunk> searchEngine = new HybridSearcher<DocumentChunk>(
    luceneEngine: new LuceneSearchEngine<DocumentChunk>("./index/lucene"),
    vectorEngine: new VectorSearchEngine<DocumentChunk>("./index/vector", embeddings),
    lexicalWeight: 0.5,
    semanticWeight: 0.5
);

// Index a document
await searchEngine.IndexAsync(new DocumentChunk
{
    Id = "doc1",
    Content = "How to optimize async performance in C#",
    CreatedAt = DateTime.UtcNow
});

// Search — hybrid mode by default
IReadOnlyList<SearchResult<DocumentChunk>> results =
    await searchEngine.SearchAsync("async optimization", maxResults: 10);

foreach (SearchResult<DocumentChunk> result in results)
{
    Console.WriteLine($"[{result.Score:F3}] {result.Document.Content}");
}
```

### RAG Pipeline

Retrieve semantically relevant context chunks to augment an LLM prompt:

```csharp
// Retrieve the top 5 most semantically relevant chunks
IReadOnlyList<SearchResult<DocumentChunk>> chunks =
    await searchEngine.SearchAsync(userQuery, maxResults: 5, mode: SearchMode.Semantic);

string context = string.Join("\n\n", chunks.Select(r => r.Document.Content));

string prompt = $"""
    Context:
    {context}

    Question: {userQuery}

    Answer:
    """;
```

### Hierarchical Documents (IHierarchicalDocument)

Index parent documents whose children are individually searchable, and expand context at retrieval time:

```csharp
public class Message
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required DateTime SentAt { get; init; }
}

public class ConversationSession : IHierarchicalDocument<Message>
{
    public required string Id { get; init; }
    public required IReadOnlyList<Message> Messages { get; init; }

    // Each message is indexed individually
    public IReadOnlyList<Message> GetChildren() => Messages;

    // Retrieve N messages of context before a matched message
    public IReadOnlyList<Message> GetChildrenBefore(string messageId, int count) =>
        Messages
            .TakeWhile(m => m.Id != messageId)
            .TakeLast(count)
            .ToList();
}

// The engine indexes each Message as a searchable unit,
// but returns the parent ConversationSession with surrounding context
var sessionEngine = new HybridSearcher<ConversationSession>(
    luceneEngine: new LuceneSearchEngine<ConversationSession>("./index/lucene"),
    vectorEngine: new VectorSearchEngine<ConversationSession>("./index/vector", embeddings)
);

await sessionEngine.IndexAsync(session);

IReadOnlyList<SearchResult<ConversationSession>> hits =
    await sessionEngine.SearchAsync("authentication error", maxResults: 5);
```

---

## 🔍 Search Modes

| Mode | Enum Value | Best For |
|---|---|---|
| Lexical | `SearchMode.Lexical` | Exact term matching, keyword queries, known identifiers |
| Semantic | `SearchMode.Semantic` | Conceptual similarity, natural language queries, paraphrase matching |
| Hybrid | `SearchMode.Hybrid` | General-purpose search — balances precision and recall via RRF |

```csharp
// Explicit mode selection
var results = await searchEngine.SearchAsync(query, maxResults: 10, mode: SearchMode.Semantic);
```

---

## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                        ISearchEngine<T>                          │
└───────────────────────────┬──────────────────────────────────────┘
                            │
              ┌─────────────▼─────────────┐
              │      HybridSearcher<T>    │
              │       (RRF Fusion)        │
              └──────┬────────────┬───────┘
                     │            │
        ┌────────────▼──┐   ┌─────▼──────────────┐
        │LuceneSearch   │   │VectorSearchEngine<T>│
        │Engine<T>      │   │(Semantic Search)    │
        │(BM25 Keyword) │   └─────────┬───────────┘
        └───────┬───────┘             │
                │               ┌─────▼──────────────┐
        ┌───────▼───────┐       │   AJVI Index        │
        │  Lucene.NET   │       │   (Memory-Mapped,   │
        │  Inverted     │       │    Float16 / F32)   │
        │  Index        │       └─────────┬───────────┘
        └───────────────┘                 │
                                 ┌────────▼────────────┐
                                 │  IEmbeddingProvider  │
                                 ├──────────────────────┤
                                 │  OnnxEmbeddingProvider│
                                 │  (MiniLM-L6-v2,      │
                                 │   384 dims, DirectML) │
                                 └──────────────────────┘
```

| Component | Role |
|---|---|
| `ISearchable` / `IDocument` | Minimal interfaces for indexable content |
| `IEmbeddingProvider` | Pluggable embedding generation (ONNX, hash-based, or custom) |
| `LuceneSearchEngine<T>` | BM25 keyword search backed by Lucene.NET |
| `VectorSearchEngine<T>` | Semantic search using the AJVI custom vector index |
| `HybridSearcher<T>` | RRF fusion layer that merges lexical and semantic result sets |
| `AJVI Index` | Append-only binary vector store with memory-mapped I/O and SIMD cosine similarity |
| `DecayCalculator` | Exponential temporal decay (90-day half-life by default) for freshness boosting |

---

## ⚡ Performance

Benchmarks measured on typical developer hardware (CPU: modern x64; GPU: DirectML-compatible discrete GPU).

### Indexing

| Operation | CPU | GPU (DirectML) |
|---|---|---|
| Embed single document | ~15 ms | ~2 ms |
| Index 100 documents | ~1.5 s | ~300 ms |
| Lucene index 1,000 documents | ~50 ms | — |

### Search

| Operation | Dataset Size | Latency |
|---|---|---|
| Lexical (BM25) | 10K documents | ~20 ms |
| Semantic (AJVI, Float16) | 100K vectors | ~80 ms |
| Hybrid (parallel RRF) | 10K documents | ~30 ms |

### Storage

| Format | Bytes per Vector (384 dims) |
|---|---|
| AJVI Float16 | ~825 bytes |
| AJVI Float32 | ~1,593 bytes |
| Lucene (per document) | ~500–2,000 bytes (content-dependent) |

### Scalability

- **Under 100K vectors** — Excellent performance with brute-force nearest neighbor
- **100K–1M vectors** — Good performance; review [performance tuning guidance](docs/advanced/performance-tuning.md)
- **Over 1M vectors** — Approximate nearest neighbor support is a planned enhancement

---

## ⚙️ Configuration

Register and configure the search engine via `IOptions<SearchEngineOptions>`:

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.IndexPath         = "./index";
    options.DefaultMode       = SearchMode.Hybrid;
    options.Precision         = VectorPrecision.Float16;  // ~50% storage savings vs Float32
    options.LexicalWeight     = 0.5;
    options.SemanticWeight    = 0.5;
    options.RrfK              = 60;    // Reciprocal Rank Fusion constant
    options.DecayHalfLifeDays = 90.0;  // Exponential temporal decay half-life
});
```

---

## 📋 Requirements

- **.NET** 9.0 or later
- **Platforms** — Windows, Linux, macOS
- **GPU** (optional) — DirectML-compatible GPU for accelerated embedding generation

### Dependencies

| Package | Version | License |
|---|---|---|
| Lucene.Net | 4.8.0-beta00016 | Apache 2.0 |
| Microsoft.ML.OnnxRuntime.DirectML | 1.20.0 | MIT |
| Microsoft.ML.Tokenizers | 1.0.0 | MIT |
| System.Numerics.Tensors | 10.0.1 | MIT |

---

## 📚 Documentation

| Document | Description |
|---|---|
| [Getting Started](docs/getting-started.md) | Step-by-step tutorial for your first integration |
| [API Reference](docs/api/README.md) | Complete API surface reference |
| [Architecture Overview](docs/architecture.md) | Component design, data flow, and index internals |
| [Hierarchical Documents](docs/advanced/hierarchical-documents.md) | Parent-child document indexing and context expansion |
| [Custom Field Mapping](docs/advanced/custom-field-mapping.md) | Controlling how document fields are indexed |
| [Performance Tuning](docs/advanced/performance-tuning.md) | Scaling guidance and optimization strategies |
| [Temporal Decay](docs/advanced/temporal-decay.md) | Configuring freshness-based relevance ranking |
| [Migration Guide](docs/migration.md) | Migrating from agent-session-search-tools |
| [Samples](samples/) | Runnable RAG and Agent Session examples |

---

## 🤝 Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

- [Open an issue](https://github.com/brendankowitz/vectorized-content-indexer/issues) to report bugs or request features
- [Start a discussion](https://github.com/brendankowitz/vectorized-content-indexer/discussions) for questions and ideas

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for full terms.

---

## 🙏 Acknowledgments

### Embedding Model

**[sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)**
384 dimensions, 22.7M parameters. Apache 2.0 License. Trained by [Nils Reimers](https://www.nreimers.de/).

### Research

- **Reciprocal Rank Fusion (RRF)** — Cormack, Clarke, and Buettcher. [*Reciprocal Rank Fusion outperforms Condorcet and individual Rank Learning Methods*](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf). SIGIR 2009.

### Inspiration

- **[Lucene.NET](https://lucenenet.apache.org/)** — The foundation of the lexical search layer
- **[Elasticsearch](https://www.elastic.co/)** — Hybrid search implementation patterns
- **[Vespa.ai](https://vespa.ai/)** — Multi-phase retrieval architecture
