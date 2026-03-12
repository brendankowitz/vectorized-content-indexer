# Architecture Overview

This document provides a deep dive into the architecture of ZeroProximity.VectorizedContentIndexer, covering component design, data flow, binary formats, and performance characteristics.

## Table of Contents

1. [High-Level Architecture](#high-level-architecture)
2. [Component Details](#component-details)
3. [Data Flow Diagrams](#data-flow-diagrams)
4. [AJVI Binary Format](#ajvi-binary-format)
5. [Thread Safety Model](#thread-safety-model)
6. [Memory Management](#memory-management)
7. [Extensibility Points](#extensibility-points)
8. [Performance Characteristics](#performance-characteristics)

## High-Level Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Application Layer                            │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────────┐   │
│  │ RAG System  │  │ Agent Memory │  │ Document Search Service │   │
│  └──────┬──────┘  └──────┬───────┘  └────────┬────────────────┘   │
└─────────┼─────────────────┼───────────────────┼─────────────────────┘
          │                 │                   │
          └─────────────────┼───────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────────────────┐
│                  ISearchEngine<TDocument>                            │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                    HybridSearcher<T>                           │ │
│  │  ┌──────────────────────────┬──────────────────────────────┐  │ │
│  │  │  LuceneSearchEngine<T>   │  VectorSearchEngine<T>       │  │ │
│  │  │  (BM25 Keyword Search)   │  (Semantic Vector Search)    │  │ │
│  │  └──────────┬───────────────┴───────────┬──────────────────┘  │ │
│  └─────────────┼─────────────────────────────┼──────────────────────┘ │
└────────────────┼─────────────────────────────┼───────────────────────┘
                 │                             │
      ┌──────────▼──────────┐       ┌──────────▼───────────┐
      │  Lucene.NET Index   │       │  AJVI Vector Index   │
      │  ┌───────────────┐  │       │  ┌────────────────┐  │
      │  │ Segments      │  │       │  │ Memory-Mapped  │  │
      │  │ Term Dict     │  │       │  │ Binary File    │  │
      │  │ Posting Lists │  │       │  │ (Float16/32)   │  │
      │  └───────────────┘  │       │  └────────────────┘  │
      └─────────────────────┘       └──────────┬───────────┘
                                               │
                                    ┌──────────▼───────────┐
                                    │  IEmbeddingProvider  │
                                    │  ┌────────────────┐  │
                                    │  │ ONNX Runtime   │  │
                                    │  │ MiniLM-L6-v2   │  │
                                    │  │ DirectML GPU   │  │
                                    │  └────────────────┘  │
                                    └──────────────────────┘
```

### Layer Responsibilities

**Application Layer:**
- Domain-specific search logic
- Document adapters (ISearchable, IDocument)
- Result processing and filtering

**Search Engine Layer:**
- Query processing and routing
- Index management (add, delete, optimize)
- Result ranking and fusion

**Storage Layer:**
- Lucene.NET: Inverted index for keyword search
- AJVI: Custom vector index for semantic search
- Memory-mapped I/O for efficient access

**Embedding Layer:**
- ONNX Runtime inference
- Tokenization and tensor operations
- GPU acceleration (DirectML)

---

## Component Details

### 1. Document Abstraction Layer

#### ISearchable

Minimal contract for indexable content.

```csharp
// Provides: ID, searchable text, timestamp
public interface ISearchable
{
    string Id { get; }
    string GetSearchableText();
    DateTime GetTimestamp();
}
```

**Design Rationale:**
- Minimal surface area reduces coupling
- Works with any domain model via adapter pattern
- Timestamp enables temporal decay without additional metadata

#### IDocument

Extended contract with metadata support.

```csharp
// Adds: Metadata dictionary for Lucene field mapping
public interface IDocument : ISearchable
{
    IDictionary<string, object> GetMetadata();
}
```

**Design Rationale:**
- Metadata as dictionary provides flexibility
- Type-safe values (string, int, DateTime, etc.)
- Enables advanced Lucene features (filtering, faceting, field boosting)

#### IHierarchicalDocument\<TChild\>

Support for parent-child relationships.

```csharp
// Adds: Child navigation and context expansion
public interface IHierarchicalDocument<TChild> : IDocument
{
    IReadOnlyList<TChild> GetChildren();
    IReadOnlyList<TChild> GetChildrenBefore(string childId, int count);
    IReadOnlyList<TChild> GetChildrenAfter(string childId, int count);
}
```

**Design Rationale:**
- Lazy evaluation (GetChildren() only called when needed)
- Optional methods (context expansion) have default implementations
- Preserves sequential ordering for message-like structures

---

### 2. Embedding Providers

#### IEmbeddingProvider

```csharp
public interface IEmbeddingProvider
{
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedManyAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
```

**Implementations:**

1. **OnnxEmbeddingProvider**
   - ONNX Runtime with DirectML
   - MiniLM-L6-v2 embedded model
   - Mean pooling with attention mask
   - Thread-safe with SemaphoreSlim

2. **HashEmbeddingProvider**
   - Deterministic hash-based embeddings
   - Development/testing fallback
   - Fast but not semantic

**Factory Pattern:**

```csharp
EmbeddingProviderFactory.TryCreateAsync()
    ├─ Try ONNX → Success: return OnnxEmbeddingProvider
    └─ Catch exception → Fallback: return HashEmbeddingProvider
```

**Design Rationale:**
- Graceful degradation for development
- Pluggable providers (could add OpenAI, Cohere, etc.)
- Batch operations for efficiency

---

### 3. Search Engines

#### LuceneSearchEngine\<T\>

**Responsibilities:**
- BM25 keyword search
- Boolean query parsing
- Field mapping via ILuceneDocumentMapper
- Index optimization (segment merging)

**Key Components:**

```
IndexWriter (single writer lock)
    ├─ Add/Update documents
    ├─ Delete documents
    └─ Commit changes

SearcherManager (thread-safe reads)
    ├─ Acquire searcher
    ├─ Execute query
    └─ Release searcher

ILuceneDocumentMapper<T>
    ├─ MapToLuceneDocument(T) → Lucene.Document
    └─ MapFromLuceneDocument(Document) → T
```

**Thread Safety:**
- Single writer (SemaphoreSlim)
- Multiple concurrent readers (SearcherManager)
- Refresh on write completion

#### VectorSearchEngine\<T\>

**Responsibilities:**
- Semantic vector search
- AJVI index management
- Cosine similarity scoring
- Float16/Float32 precision support

**Key Components:**

```
AjviIndex (custom binary format)
    ├─ Memory-mapped file access
    ├─ SHA256 content deduplication
    ├─ O(1) append operation
    └─ O(n*d) brute-force search

IEmbeddingProvider
    ├─ Generate embeddings
    └─ Normalize vectors
```

**Search Algorithm:**

```csharp
1. Embed query → q_vector (384 dims)
2. For each indexed vector v:
     similarity = cosine(q_vector, v) = dot(q, v) / (||q|| × ||v||)
3. Sort by similarity descending
4. Return top-K results
```

**Design Rationale:**
- Brute-force suitable for < 1M vectors
- Memory-mapped I/O avoids loading entire index
- SHA256 prevents duplicate content indexing

#### HybridSearcher\<T\>

**Responsibilities:**
- Reciprocal Rank Fusion (RRF)
- Parallel search execution
- Score normalization

**RRF Algorithm:**

```csharp
// For each document d:
lexical_rank = rank in BM25 results (1-based)
semantic_rank = rank in vector results (1-based)

score(d) = lexicalWeight / (rrfK + lexical_rank) +
           semanticWeight / (rrfK + semantic_rank)

// Documents appearing in both results get highest scores
```

**Execution Flow:**

```
SearchAsync(query)
    ├─ Task.WhenAll
    │   ├─ luceneEngine.SearchAsync(query)
    │   └─ vectorEngine.SearchAsync(query)
    ├─ Compute RRF scores
    ├─ Merge and deduplicate
    └─ Sort by combined score
```

**Design Rationale:**
- RRF robust to score distribution differences
- Parallel execution minimizes latency
- Weights configurable per use case

---

## Data Flow Diagrams

### Indexing Flow

```
Application
    │
    ├─ document: ISearchable
    │
    ▼
HybridSearcher.IndexAsync(document)
    │
    ├──────────────────────┬─────────────────────┐
    │                      │                     │
    ▼                      ▼                     ▼
LuceneSearchEngine    VectorSearchEngine    (parallel)
    │                      │
    ├─ Map to Lucene.Doc   ├─ GetSearchableText()
    │  (via IDocumentMapper)│  │
    │                      ├─ EmbedAsync(text)
    │                      │  │ (ONNX Runtime)
    │                      │  │
    ▼                      ▼  ▼
IndexWriter            AjviIndex.AddEntry
    │                      │
    ├─ Add to segment      ├─ Compute SHA256
    │                      ├─ Check duplicate
    ├─ Update posting lists├─ Normalize vector
    │                      ├─ Convert Float16/32
    ├─ Store fields        ├─ Memory-map append
    │                      │
    ▼                      ▼
Lucene Index File      AJVI Binary File
```

### Search Flow (Hybrid Mode)

```
Application
    │
    ├─ query: string
    │
    ▼
HybridSearcher.SearchAsync(query)
    │
    ├──────────────────────┬─────────────────────┐
    │                      │                     │
    ▼                      ▼                     ▼
LuceneSearchEngine    VectorSearchEngine    (parallel)
    │                      │
    ├─ Parse query         ├─ EmbedAsync(query)
    │  (QueryParser)       │  │
    │                      │  ▼
    ▼                      │  Normalized query vector
IndexSearcher          │
    │                      │
    ├─ BM25 scoring        ├─ AJVI.SearchAsync
    │                      │  │
    │                      │  ├─ For each vector:
    │                      │  │   cosine_similarity
    │                      │  │
    ▼                      ▼  ▼
Lexical Results        Semantic Results
(ranked by BM25)       (ranked by cosine)
    │                      │
    └──────────┬───────────┘
               │
               ▼
    Reciprocal Rank Fusion
        │
        ├─ Compute RRF scores
        ├─ Merge by document ID
        ├─ Apply temporal decay (if enabled)
        │
        ▼
    Sorted Combined Results
        │
        ▼
    Application
```

---

## AJVI Binary Format

### File Structure

```
┌────────────────────────────────────────────────────────────┐
│  Header (64 bytes)                                         │
├────────────────────────────────────────────────────────────┤
│  Magic:    "AJVI" (4 bytes)                                │
│  Version:  1 (4 bytes, int32)                              │
│  Precision: 0=Float16, 1=Float32 (4 bytes, int32)          │
│  Dimensions: 384 (4 bytes, int32)                          │
│  EntryCount: N (8 bytes, int64)                            │
│  Reserved: (40 bytes, zeros)                               │
├────────────────────────────────────────────────────────────┤
│  Entry 0                                                   │
│    ├─ ContentHash: SHA256 (32 bytes)                       │
│    ├─ DocumentId: GUID (16 bytes)                          │
│    ├─ Metadata: (9 bytes)                                  │
│    │   ├─ AgentType: byte                                  │
│    │   └─ Timestamp: int64 (ticks)                         │
│    └─ Vector: float16[384] or float32[384]                 │
│         (768 or 1536 bytes)                                │
├────────────────────────────────────────────────────────────┤
│  Entry 1                                                   │
│    ├─ ...                                                  │
├────────────────────────────────────────────────────────────┤
│  ...                                                       │
├────────────────────────────────────────────────────────────┤
│  Entry N-1                                                 │
└────────────────────────────────────────────────────────────┘
```

### Entry Size Calculation

**Float16:**
```
32 (SHA256) + 16 (GUID) + 1 (AgentType) + 8 (Timestamp) + (384 × 2) = 825 bytes
```

**Float32:**
```
32 (SHA256) + 16 (GUID) + 1 (AgentType) + 8 (Timestamp) + (384 × 4) = 1,593 bytes
```

### File Operations

#### Append Entry (O(1))

```csharp
1. Compute SHA256(searchableText)
2. Check if hash exists in index (duplicate check)
3. If duplicate: return existing entry
4. Encode vector to Float16/Float32
5. Memory-map file, extend by entry_size
6. Write entry at offset: header_size + (entry_count × entry_size)
7. Update header entry_count
8. Flush to disk
```

#### Search (O(n*d))

```csharp
1. Embed query → query_vector (d dimensions)
2. Memory-map entire file (read-only)
3. For each entry (parallel):
     a. Read vector from memory-map
     b. Decode Float16/32 → float[]
     c. Compute cosine_similarity(query_vector, entry_vector)
4. Sort by similarity descending
5. Return top-K entries
```

### Memory-Mapped I/O

**Benefits:**
- OS manages paging (no manual buffer management)
- Shared memory across processes
- Fast random access
- Low memory footprint (only accessed pages loaded)

**Trade-offs:**
- Requires 64-bit address space for large indexes (> 4GB)
- File system cache performance critical
- Not suitable for network file systems

---

## Thread Safety Model

### LuceneSearchEngine

```
┌─────────────────────────────────────────┐
│  IndexWriter (single writer)            │
│  ├─ SemaphoreSlim (count: 1)            │
│  ├─ Write operations serialized         │
│  └─ Commit on success                   │
├─────────────────────────────────────────┤
│  SearcherManager (concurrent readers)   │
│  ├─ Acquire: lock-free                  │
│  ├─ Search: parallel safe               │
│  └─ Release: ref-counted                │
└─────────────────────────────────────────┘
```

**Pattern:**
- Write locks prevent concurrent writes
- Reads continue during writes (snapshot isolation)
- Searcher refresh after write completion

### VectorSearchEngine

```
┌─────────────────────────────────────────┐
│  AjviIndex                              │
│  ├─ Write: SemaphoreSlim (count: 1)    │
│  ├─ Read: Memory-mapped (concurrent OK) │
│  └─ Memory barriers on header update   │
├─────────────────────────────────────────┤
│  IEmbeddingProvider                     │
│  ├─ ONNX Session: SemaphoreSlim         │
│  │   (ONNX Runtime not thread-safe)    │
│  └─ Tokenizer: thread-safe              │
└─────────────────────────────────────────┘
```

**Pattern:**
- Write serialization via SemaphoreSlim
- Read-only memory-mapped access (no locks needed)
- ONNX inference serialized (provider limitation)

### HybridSearcher

```
┌─────────────────────────────────────────┐
│  Parallel Search Execution              │
│  ├─ Task.WhenAll (no shared state)     │
│  ├─ Each engine thread-safe            │
│  └─ Merge after completion             │
└─────────────────────────────────────────┘
```

**Pattern:**
- No shared mutable state
- Functional composition of results
- Thread-safe by design

---

## Memory Management

### Embedding Provider

**ONNX Model Loading:**
```
Embedded Resource (23MB compressed)
    ↓ Extract to temp
    ↓ Load into ONNX Runtime
    ↓ ~100MB RAM (model + session state)
```

**Optimization:**
- Singleton pattern (one model per process)
- Reuse across all search engines
- DirectML uses GPU memory (VRAM)

### Lucene Index

**Memory Usage:**
```
IndexWriter: 16MB default RAM buffer
    ├─ Flush to disk when full
    └─ Configurable via RAMBufferSizeMB

IndexSearcher: Variable
    ├─ Term dictionary: ~10-50KB per 1K documents
    ├─ Field cache: ~bytes_per_doc × num_docs
    └─ Query-dependent allocations
```

**Optimization:**
- Searcher pooling (SearcherManager)
- Periodic index optimization (segment merging)
- LRU cache for frequently accessed terms

### AJVI Index

**Memory Usage:**
```
Memory-Mapped File
    ├─ Virtual address space: file_size
    ├─ Physical RAM: accessed_pages × page_size
    └─ OS managed paging
```

**Example (100K vectors, Float16):**
```
File size: 64 + (100,000 × 825) = 82.5MB
RAM usage: Only pages accessed during search
    - Full scan: ~82.5MB resident
    - Top-K search: ~10-20MB resident (early termination)
```

**Optimization:**
- Sequential access patterns maximize cache hits
- SIMD operations for similarity computation
- Early termination for top-K (partial scan)

---

## Extensibility Points

### 1. Custom Embedding Providers

```csharp
public class CustomEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 1536;  // OpenAI ada-002

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        // Call external API, use different model, etc.
        var response = await openAiClient.CreateEmbeddingAsync(text);
        return response.Data[0].Embedding;
    }
}
```

### 2. Custom Lucene Document Mappers

```csharp
public class CustomMapper : ILuceneDocumentMapper<Article>
{
    public Document MapToLuceneDocument(Article article)
    {
        var doc = new Document();

        // Custom field configuration
        doc.Add(new TextField("Title", article.Title, Field.Store.YES)
        {
            Boost = 2.0f  // Boost title matches
        });

        doc.Add(new StringField("Category", article.Category, Field.Store.YES));

        // Numeric range query support
        doc.Add(new Int32Field("ViewCount", article.ViewCount, Field.Store.YES));

        return doc;
    }
}
```

### 3. Custom Search Result Processing

```csharp
public static class SearchExtensions
{
    public static async Task<IReadOnlyList<SearchResult<T>>> SearchWithFiltersAsync<T>(
        this ISearchEngine<T> engine,
        string query,
        Func<T, bool> filter)
        where T : ISearchable
    {
        var results = await engine.SearchAsync(query, maxResults: 100);
        return results.Where(r => filter(r.Document)).ToList();
    }
}
```

### 4. Multi-Tenant Index Strategy

```csharp
public class TenantSearchService<T> where T : ISearchable
{
    private readonly ConcurrentDictionary<string, ISearchEngine<T>> _engines = new();
    private readonly IEmbeddingProvider _embeddings;

    public ISearchEngine<T> GetEngineForTenant(string tenantId)
    {
        return _engines.GetOrAdd(tenantId, tid =>
            new HybridSearcher<T>(
                new LuceneSearchEngine<T>($"./indexes/{tid}/lucene"),
                new VectorSearchEngine<T>($"./indexes/{tid}/vector", _embeddings)
            ));
    }
}
```

---

## Performance Characteristics

### Indexing Performance

| Operation | Documents | Time (CPU) | Time (GPU) | Notes |
|-----------|-----------|------------|------------|-------|
| Single document | 1 | 20ms | 5ms | Includes embedding |
| Batch (10) | 10 | 180ms | 40ms | ~10% overhead reduction |
| Batch (100) | 100 | 1.6s | 350ms | ~15% overhead reduction |
| Batch (1000) | 1000 | 15s | 3.2s | Optimal batch size |

**Bottlenecks:**
1. Embedding generation (70% of time)
2. Lucene document mapping (20%)
3. Disk I/O (10%)

### Search Performance

| Index Size | Lexical | Semantic | Hybrid | Notes |
|------------|---------|----------|--------|-------|
| 1K docs | 5ms | 10ms | 12ms | Both fast |
| 10K docs | 15ms | 80ms | 85ms | Semantic linear growth |
| 100K docs | 25ms | 750ms | 770ms | Brute-force limit approaching |
| 1M docs | 40ms | 7.5s | 7.5s | Need approximate search |

**Search Breakdown (100K docs):**
- Query embedding: 15ms
- Lucene search: 10ms
- Vector search: 750ms
  - Memory-map access: 50ms
  - Similarity computation: 700ms (parallelize-able)
- RRF fusion: 10ms

### Storage Efficiency

| Precision | Dimensions | Bytes/Vector | 100K Vectors | 1M Vectors |
|-----------|------------|--------------|--------------|------------|
| Float16 | 384 | 825 | 78.7 MB | 787 MB |
| Float32 | 384 | 1,593 | 151.9 MB | 1.5 GB |

**Compression Ratio:**
- Float16 saves 48% storage vs. Float32
- Quality impact: < 1% reduction in similarity scores
- Recommended for production use

### Scalability Limits

**Lucene:**
- Practical: 10M+ documents
- Maximum: 2.1B documents (int32 limit)
- Performance: O(log n) for most queries

**AJVI Vector Index:**
- Practical: 100K - 1M vectors (brute-force)
- Performance: O(n × d) for search
- Beyond 1M: Consider HNSW or IVF algorithms

**Memory:**
- 100K vectors (Float16): ~80MB index + 100MB model = 180MB total
- 1M vectors (Float16): ~800MB index + 100MB model = 900MB total

---

## Design Principles

### 1. Composition Over Inheritance

```csharp
// HybridSearcher composes engines, doesn't inherit
public class HybridSearcher<T>
{
    private readonly ISearchEngine<T> _luceneEngine;
    private readonly ISearchEngine<T> _vectorEngine;
}
```

### 2. Interface Segregation

```csharp
// ISearchable: Minimal contract
// IDocument: Extends with metadata
// IHierarchicalDocument: Further extends with navigation
```

### 3. Dependency Inversion

```csharp
// Depend on IEmbeddingProvider abstraction
// Not on OnnxEmbeddingProvider implementation
public VectorSearchEngine(IEmbeddingProvider embedder) { ... }
```

### 4. Open/Closed Principle

```csharp
// Open for extension via custom implementations
// Closed for modification (interfaces stable)
public interface ISearchEngine<T> { ... }
```

---

## See Also

- [API Documentation](api/README.md)
- [Performance Tuning](advanced/performance-tuning.md)
- [Custom Field Mapping](advanced/custom-field-mapping.md)
- [Getting Started](getting-started.md)
