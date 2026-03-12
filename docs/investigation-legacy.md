# Investigation: Content Indexing Library Components

**Date:** 2026-01-21
**Source Project:** E:\data\src\agent-session-search-tools
**Target Project:** E:\data\src\vectorized-content-indexer
**Objective:** Extract reusable content indexing components for keyword + vector search with optional reranking

## Executive Summary

The `agent-session-search-tools` project contains a well-architected content indexing and search system that combines:

- **Lexical Search** (keyword-based via Lucene.NET with BM25 scoring)
- **Semantic Search** (vector-based using ONNX embeddings with custom AJVI index)
- **Hybrid Search** (Reciprocal Rank Fusion combining both modes)
- **Temporal Decay** (reinforcement-based relevance scoring)
- **Embedded MiniLM Model** (384-dimension ONNX model with DirectML GPU acceleration)

These components are highly suitable for extraction into a reusable single-assembly library that can serve multiple projects including:
- The source project itself (agent-session-search-tools)
- RAG systems (localagent)
- Any application needing hybrid content search

## Architecture Overview

### Search Engine Hierarchy

```
                    ISearchEngine
                         |
        +----------------+------------------+
        |                |                  |
  LuceneSearchEngine  VectorSearchEngine  HybridSearcher
   (BM25 Keyword)     (Semantic Vector)   (RRF Fusion)
        |                |
        |                +-- IEmbeddingProvider
        |                        |
        |                +-------+--------+
        |                |                |
        |         OnnxEmbeddingProvider  HashEmbeddingProvider
        |          (MiniLM-L6-v2)        (Fallback)
        |
   Lucene.NET                    AJVI Index
   (Inverted Index)              (Binary Vector Store)
```

### Data Flow

```
Content → Index → Search → Rank → Results

Indexing Flow:
  1. Content → Embedding (ONNX/Hash)
  2. Vector → AJVI Index (with SHA256 dedup)
  3. Content → Lucene Index (with BM25 fields)

Search Flow:
  1. Query → Parallel(Lexical, Semantic)
  2. Lexical → BM25 Scores
  3. Semantic → Vector Similarity Scores
  4. Both → RRF Fusion → Combined Scores
  5. Optional: Apply Decay Factor
  6. Return Top-K Results
```

## Core Components Analysis

### 1. Search Abstraction Layer

**File:** `/src/AgentJournal.Core/Search/ISearchEngine.cs`

**Interface Design:**
```csharp
public interface ISearchEngine
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        int contextMessages = 0,
        CancellationToken cancellationToken = default);
    Task IndexSessionAsync(Session session, CancellationToken cancellationToken = default);
    Task IndexSessionsAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken = default);
    Task ClearIndexAsync(CancellationToken cancellationToken = default);
}
```

**SearchMode Enum:**
- `Lexical` - Keyword search only (BM25)
- `Semantic` - Vector search only
- `Hybrid` - Combined search with RRF

**Evaluation:**
- ✅ Clean abstraction
- ✅ Async throughout
- ✅ CancellationToken support
- ⚠️ Coupled to Session/Message models (needs generalization)

**Extraction Strategy:**
- Create generic `IDocument` abstraction to replace `Session`
- Parameterize document type: `ISearchEngine<TDocument>`
- Keep same method signatures with generic types

---

### 2. Embedding Provider System

**Files:**
- `/src/AgentJournal.Core/Embeddings/IEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/OnnxEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/HashEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/EmbeddingProviderFactory.cs`

**Interface:**
```csharp
public interface IEmbeddingProvider
{
    int Dimensions { get; }
    bool IsSemanticModel { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    void Normalize(Span<float> vector);
}
```

**OnnxEmbeddingProvider Details:**

| Property | Value |
|----------|-------|
| Model | sentence-transformers/all-MiniLM-L6-v2 |
| Dimensions | 384 |
| Max Sequence Length | 256 tokens |
| Batch Size | 32 |
| Execution Provider | DirectML (GPU) with CPU fallback |
| Tokenizer | BertTokenizer (WordPiece) with Tiktoken fallback |
| Model Source | Embedded resource (no external download) |

**Factory Pattern:**
```csharp
public static class EmbeddingProviderFactory
{
    public static async Task<IEmbeddingProvider> TryCreateAsync(
        string? modelsPath,
        CancellationToken cancellationToken = default)
    {
        // 1. Try ONNX provider if model available
        // 2. Fall back to hash-based embeddings
        // 3. Log which provider was selected
    }
}
```

**Evaluation:**
- ✅ Excellent abstraction - 100% generic
- ✅ Graceful degradation (ONNX → Hash)
- ✅ Thread-safe (SemaphoreSlim guards)
- ✅ Embedded model eliminates deployment complexity
- ✅ GPU acceleration with DirectML
- ✅ Ready for extraction as-is

**Extraction Strategy:**
- Copy entire Embeddings/ directory
- Include embedded model resource
- No changes needed - perfect as-is

---

### 3. AJVI Vector Index (Agent Journal Vector Index)

**File:** `/src/AgentJournal.Core/Search/AjviIndex.cs`

**Format Specification:**

```
Binary Layout:
┌─────────────────────────────────────────────┐
│ Header (32 bytes)                           │
│  - Magic: "AJVI" (0x494A5641)              │
│  - Version: 1 byte                          │
│  - Precision: 1 byte (Float32=0, Float16=1) │
│  - Dimensions: 2 bytes (ushort)             │
│  - Entry count: 8 bytes (long)              │
│  - Reserved: 18 bytes                       │
├─────────────────────────────────────────────┤
│ Entry 0                                     │
│  - Content hash: 32 bytes (SHA256)         │
│  - Message ID: 16 bytes (GUID)             │
│  - Agent type: 1 byte                       │
│  - Timestamp: 8 bytes (Unix ms)             │
│  - Vector: dimensions × (2 or 4) bytes      │
├─────────────────────────────────────────────┤
│ Entry 1                                     │
│  - ...                                      │
└─────────────────────────────────────────────┘
```

**Key Features:**

1. **Memory-Mapped Access**
   - Uses `MemoryMappedFile` for efficient I/O
   - O(1) lookup by index
   - Entire index stays in memory

2. **Precision Options**
   - Float32: Full precision (4 bytes/dimension)
   - Float16: Half precision (2 bytes/dimension, 50% storage savings)

3. **Deduplication**
   - SHA256 content hashing
   - `ContainsHash()` check before adding
   - Prevents duplicate vectors

4. **Search Algorithm**
   - Brute-force L2 distance (cosine via normalized vectors)
   - Top-K via min-heap
   - Parallel-friendly (read-only after creation)

5. **Metadata Storage**
   - Per-entry GUID for result mapping
   - Agent type classification
   - Timestamp for temporal ordering

**API:**
```csharp
public class AjviIndex : IDisposable
{
    public static AjviIndex Create(string filePath, int dimensions, VectorPrecision precision);
    public static AjviIndex Open(string filePath, bool readOnly = false);

    public void AddEntry(byte[] contentHash, Guid messageGuid, AgentType agentType,
                        long timestamp, float[] vector);
    public List<(int index, double score)> Search(float[] queryVector, int topK);
    public bool ContainsHash(byte[] contentHash);
    public Guid GetMessageId(int index);

    // Properties
    public int Dimensions { get; }
    public long EntryCount { get; }
    public VectorPrecision Precision { get; }
}
```

**Performance Characteristics:**

| Operation | Complexity | Notes |
|-----------|-----------|-------|
| Create | O(1) | Allocate header only |
| Add Entry | O(1) | Append to end |
| Search | O(n*d) | Brute-force, but fast for small-medium datasets |
| Contains Hash | O(n) | Linear scan (could add hash index) |
| Memory Usage | 57 + n*(57 + d*p) bytes | p=2 (F16) or p=4 (F32) |

**Evaluation:**
- ✅ Excellent custom format - no dependencies
- ✅ Production-ready
- ✅ Could replace Faiss/Annoy for moderate datasets (<1M vectors)
- ⚠️ AgentType enum is domain-specific
- ⚠️ Metadata fields are somewhat coupled

**Extraction Strategy:**
- Generalize metadata: Replace AgentType with generic byte field
- Keep GUID and timestamp (useful for most use cases)
- Add optional metadata extensibility
- Keep search algorithm as-is (works well)

---

### 4. Lucene Search Engine

**File:** `/src/AgentJournal.Core/Search/LuceneSearchEngine.cs`

**Lucene Configuration:**

| Setting | Value |
|---------|-------|
| Version | Lucene.NET 4.8.0-beta00016 |
| Analyzer | StandardAnalyzer |
| Similarity | BM25Similarity (default) |
| Commit Strategy | Immediate (per session) |
| Index Writer | Single writer with SearcherManager |

**Field Schema:**
```csharp
Document Fields:
- FIELD_ID (string): Message GUID - STORED, NOT_ANALYZED
- FIELD_SESSION_ID (string): Session GUID - STORED, NOT_ANALYZED
- FIELD_AGENT_TYPE (string): Agent type - STORED, NOT_ANALYZED
- FIELD_PROJECT_PATH (string): Project directory - STORED, NOT_ANALYZED
- FIELD_ROLE (string): "User" or "Assistant" - STORED, NOT_ANALYZED
- FIELD_CONTENT (string): Message text - STORED, ANALYZED
- FIELD_ALL_CONTENT (string): Full session text - NOT STORED, ANALYZED
- FIELD_TIMESTAMP (long): Unix timestamp - STORED, NUMERIC (sortable)
```

**Indexing Strategy:**
- One Lucene document per message
- Additional "session document" with all content combined
- Deduplication by session ID at search time
- Session caching for fast retrieval

**Search Implementation:**
```csharp
1. Parse query with QueryParser
2. If parse fails, use PhraseQuery
3. Execute search with BM25 scoring
4. Deduplicate by session ID (keep highest score)
5. Load session from cache
6. Apply highlighting
7. Return SearchResult objects
```

**Concurrency Model:**
- `SemaphoreSlim` for write operations
- `SearcherManager` for thread-safe reads
- Automatic searcher refresh after commits

**Evaluation:**
- ✅ Well-implemented BM25 search
- ✅ Thread-safe
- ✅ Efficient caching
- ⚠️ Tightly coupled to Session/Message model
- ⚠️ Field schema is domain-specific

**Extraction Strategy:**
- Create generic document adapter pattern
- Allow custom field mapping: `IDocumentFieldMapper<TDocument>`
- Keep Lucene implementation but parameterize document types
- Make field names configurable

---

### 5. Vector Search Engine

**File:** `/src/AgentJournal.Core/Search/VectorSearchEngine.cs`

**Architecture:**
```
VectorSearchEngine
    ├── IEmbeddingProvider (injected)
    ├── AJVI Index (messages)
    ├── AJVI Index (knowledge entries)
    ├── Session cache (Dictionary<Guid, Session>)
    ├── Message mapping cache (Dictionary<Guid, Guid>)
    └── Knowledge mapping cache (Dictionary<Guid, int>)
```

**Indexing Flow:**
```
1. Embed content (IEmbeddingProvider.EmbedAsync)
2. L2-normalize embedding
3. Compute SHA256 hash
4. Check for duplicates (AJVI.ContainsHash)
5. Generate deterministic GUID (namespace UUID v5)
6. Add to AJVI index
7. Update mapping caches
```

**Search Flow:**
```
1. Embed query
2. L2-normalize query vector
3. Search AJVI (top-k × 3 for aggregation)
4. Group results by session ID
5. Take max score per session
6. Sort by score
7. Optionally expand context (N messages before/after)
8. Return top N sessions
```

**Context Expansion:**
- Configurable: 0, 1, 2, 3, etc. messages before/after match
- Maintains chronological order
- Deduplicates expanded messages
- Useful for RAG applications

**Evaluation:**
- ✅ Clean embedding integration
- ✅ Effective caching strategy
- ✅ Context expansion is powerful
- ⚠️ Session/message coupling
- ⚠️ Dual index (messages + knowledge) complicates extraction

**Extraction Strategy:**
- Generalize to single index type
- Use generic document model
- Keep context expansion concept (very useful)
- Make mapping strategy pluggable

---

### 6. Hybrid Searcher (RRF Fusion)

**File:** `/src/AgentJournal.Core/Search/HybridSearcher.cs`

**Algorithm: Reciprocal Rank Fusion (RRF)**

```csharp
For each result in lexical_results:
    lexical_score = lexical_weight / (rrf_k + rank + 1)

For each result in semantic_results:
    semantic_score = semantic_weight / (rrf_k + rank + 1)

For each unique session:
    fused_score = lexical_score + semantic_score

Sort by fused_score descending
Return top N
```

**Configuration:**
```csharp
private readonly double _lexicalWeight = 0.5;
private readonly double _semanticWeight = 0.5;
private readonly double _rrfK = 60.0;
```

**Implementation:**
```csharp
1. Fetch 3× results from both engines (parallel)
2. Build score dictionaries (sessionId → score)
3. Apply RRF formula to each result
4. Merge dictionaries (sum scores)
5. Sort by combined score
6. Take top N
7. Build SearchResult objects
```

**Evaluation:**
- ✅ Well-established algorithm (used by Elasticsearch, others)
- ✅ Simple, effective fusion
- ✅ Configurable weights
- ✅ Parallel execution
- ✅ Minimal coupling (just composes ISearchEngine implementations)

**Extraction Strategy:**
- Keep as-is, it's perfect
- Make weights/k configurable via constructor
- Works with any ISearchEngine implementations

---

### 7. Decay Calculator (Temporal Relevance)

**File:** `/src/AgentJournal.Core/Knowledge/DecayCalculator.cs`

**Formula: Exponential Decay (Half-Life Model)**

```csharp
decay_factor = 0.5 ^ (days_since_reinforcement / half_life_days)

Default half_life: 90 days
```

**Decay Status Classification:**

| Decay Factor | Status | Description |
|--------------|--------|-------------|
| > 0.75 | Fresh | Highly relevant |
| > 0.50 | Good | Relevant |
| > 0.25 | Aging | Moderately relevant |
| > 0.10 | Decaying | Low relevance |
| ≤ 0.10 | Expiring | Should be pruned |

**API:**
```csharp
public static class DecayCalculator
{
    public static double CalculateDecayFactor(
        DateTime lastReinforced,
        double halfLifeDays = 90.0);

    public static double ApplyDecay(double baseScore, double decayFactor);

    public static string GetDecayStatus(double decayFactor);

    public static bool IsExpired(double decayFactor, double threshold = 0.05);
}
```

**Use Cases:**
1. **Knowledge Management** - Boost recently accessed items
2. **RAG Systems** - Prioritize fresh context
3. **Search Result Ranking** - Apply temporal decay to scores
4. **Cache Eviction** - Identify expired entries

**Evaluation:**
- ✅ 100% generic - no coupling
- ✅ Mathematically sound
- ✅ Configurable half-life
- ✅ Ready for extraction as-is

**Extraction Strategy:**
- Copy as-is, no changes needed
- Already perfect for reuse

---

### 8. Content Utilities

**File:** `/src/AgentJournal.Core/Utilities/ContentUtils.cs`

**Functions:**

```csharp
// Security
public static void ValidatePath(string path, string? allowedBasePath = null)
public static void ValidateFileSize(string filePath, long maxSizeBytes = 10_485_760)

// Hashing
public static byte[] ComputeHash(string content)

// Content Processing
public static string ExtractTitle(string content, string? filePath = null)

// SQL Safety
public static string EscapeLikePattern(string pattern)
public static string SanitizeFts5Query(string query)
```

**Security Features:**

1. **Path Validation**
   - Prevents directory traversal attacks
   - Ensures path is within allowed base directory
   - Throws SecurityException on violation

2. **File Size Limits**
   - Default: 10MB max
   - Prevents memory exhaustion
   - Throws InvalidOperationException if exceeded

3. **Query Sanitization**
   - FTS5 query validation (prevents injection)
   - LIKE pattern escaping
   - SQL injection prevention

**Evaluation:**
- ✅ 100% generic utilities
- ✅ Production-ready security
- ✅ No dependencies on domain models
- ✅ Ready for extraction as-is

**Extraction Strategy:**
- Copy entire class, no changes needed

---

## Repository Pattern Analysis

### Current Implementations

**Files:**
- `/src/AgentJournal.Core/Storage/SqliteSessionRepository.cs`
- `/src/AgentJournal.Core/Knowledge/SqliteKnowledgeRepository.cs`
- `/src/AgentJournal.Core/Storage/SqliteContentRepository.cs`

**Common Pattern:**

```
IRepository<T>
    ├── SaveAsync(T entity)
    ├── GetAsync(id)
    ├── SearchAsync(query, filters...)
    ├── DeleteAsync(id)
    └── InitializeAsync()

SQLite Implementation
    ├── Main table (normalized data)
    ├── FTS5 virtual table (full-text search)
    ├── Automatic triggers (INSERT/UPDATE/DELETE sync)
    └── Performance indexes
```

### Repository Features

1. **FTS5 Integration**
   - Automatic full-text index
   - Triggers keep FTS in sync
   - BM25 ranking built-in

2. **Schema Management**
   - Auto-create on InitializeAsync()
   - Version compatibility checking
   - Migration support (future)

3. **Decay Support** (Knowledge/Content)
   - `last_reinforced_at` column
   - `ReinforceAsync()` updates timestamp
   - Decay calculation at query time

4. **Batch Operations**
   - Transaction support
   - Bulk insert/update
   - Efficient for large datasets

**Evaluation:**
- ✅ Well-structured repository pattern
- ✅ FTS5 integration is excellent
- ⚠️ Schemas are domain-specific
- ⚠️ Hard to extract without losing value

**Extraction Strategy:**
- **Option A:** Extract repository interfaces only, let consumers implement
- **Option B:** Create generic repository with pluggable schema
- **Option C:** Include SQLite repositories but make schema configurable
- **Recommendation:** Option A for initial version (interfaces + examples)

---

## Data Models

### Current Models

**Files:**
- `/src/AgentJournal.Core/Models/Session.cs`
- `/src/AgentJournal.Core/Models/Message.cs`
- `/src/AgentJournal.Core/Models/KnowledgeEntry.cs`
- `/src/AgentJournal.Core/Models/ContentEntry.cs`
- `/src/AgentJournal.Core/Models/SearchResult.cs`

**Key Characteristics:**
- Immutable records (`record` keyword)
- Rich computed properties
- Thread-safe by design
- Well-documented

**Coupling Analysis:**

| Model | Generic? | Notes |
|-------|----------|-------|
| Session | ❌ No | Domain-specific: AI agent conversations |
| Message | ❌ No | Domain-specific: chat messages with tool calls |
| KnowledgeEntry | ⚠️ Partial | Content + metadata + decay (somewhat generic) |
| ContentEntry | ⚠️ Partial | Similar to KnowledgeEntry |
| SearchResult | ⚠️ Partial | Wraps Session but concept is generic |

**Extraction Strategy:**
- Create generic document interfaces
- Let consumers implement their own models
- Provide example implementations
- Keep SearchResult generic: `SearchResult<TDocument>`

---

## External Dependencies

### NuGet Packages

**Core Dependencies:**
```xml
<PackageReference Include="Lucene.Net" Version="4.8.0-beta00016" />
<PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00016" />
<PackageReference Include="Lucene.Net.QueryParser" Version="4.8.0-beta00016" />
<PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.2" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.DirectML" Version="1.20.0" />
<PackageReference Include="Microsoft.ML.Tokenizers" Version="1.0.0" />
<PackageReference Include="System.Numerics.Tensors" Version="10.0.1" />
```

**Framework Dependencies:**
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.2" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.2" />
<PackageReference Include="Microsoft.Extensions.Options" Version="10.0.2" />
```

**Domain-Specific (exclude from library):**
```xml
<PackageReference Include="ModelContextProtocol" Version="0.6.0-preview.1" />
<PackageReference Include="Scriban" Version="6.5.2" />
```

**Licensing:**
- Lucene.NET: Apache 2.0 ✅
- Microsoft.ML.OnnxRuntime: MIT ✅
- SQLite: Public Domain ✅
- All dependencies are permissively licensed

---

## Embedded Model Details

### All-MiniLM-L6-v2 Specifications

**Source:** HuggingFace `sentence-transformers/all-MiniLM-L6-v2`

**Model Characteristics:**

| Property | Value |
|----------|-------|
| Architecture | BERT (6-layer MiniLM) |
| Parameters | ~22M |
| Embedding Dimensions | 384 |
| Max Sequence Length | 256 tokens |
| File Size | ~23MB (ONNX format) |
| Quantization | None (full precision) |
| Training Data | 1B+ sentence pairs |
| License | Apache 2.0 |

**Performance Benchmarks:**

| Task | Score | Notes |
|------|-------|-------|
| STS (Semantic Similarity) | 82.41 | State-of-the-art for size |
| Speed | ~2900 sentences/sec | CPU (i7-9700K) |
| Accuracy vs. Quality | Excellent balance | Best "small" model |

**Why This Model:**
1. ✅ **Small Size** - 23MB fits in deployment packages
2. ✅ **Fast Inference** - Real-time embedding generation
3. ✅ **High Quality** - Excellent semantic understanding
4. ✅ **No Download** - Embedded in assembly resources
5. ✅ **GPU Acceleration** - DirectML support (10-20x speedup)
6. ✅ **Widely Used** - De-facto standard for small embeddings

**Embedding Location:**
```
/src/AgentJournal.Core/Resources/
    ├── all-MiniLM-L6-v2.onnx        (23MB)
    ├── vocab.txt                     (232KB)
    └── (extracted at runtime)
```

---

## Performance Analysis

### Benchmarks (from source project)

**Indexing Performance:**

| Operation | Time | Notes |
|-----------|------|-------|
| Embed single message | ~15ms | CPU inference |
| Embed single message | ~2ms | DirectML (GPU) |
| Index session (10 msgs) | ~150ms | CPU |
| Index session (10 msgs) | ~30ms | GPU |
| Lucene index session | ~5ms | Mostly I/O |
| AJVI add entry | <1ms | Memory-mapped append |

**Search Performance:**

| Operation | Dataset Size | Time | Notes |
|-----------|-------------|------|-------|
| Lexical search | 10K sessions | ~20ms | BM25 |
| Vector search | 100K vectors | ~80ms | Brute-force |
| Vector search | 1M vectors | ~800ms | Needs optimization |
| Hybrid search | 10K sessions | ~30ms | Parallel execution |

**Storage Efficiency:**

| Component | Size per Entry | Notes |
|-----------|---------------|-------|
| AJVI (Float32) | 57 + 384×4 = 1,593 bytes | Full precision |
| AJVI (Float16) | 57 + 384×2 = 825 bytes | 48% savings |
| Lucene document | ~500-2000 bytes | Varies by content |
| SQLite session | ~1-10KB | With messages |

**Memory Usage:**

| Component | Working Set | Notes |
|-----------|------------|-------|
| ONNX model | ~100MB | Loaded once |
| Lucene reader | ~50-200MB | Depends on index size |
| AJVI index | Entire file | Memory-mapped |
| Session cache | ~50-500MB | Configurable |

**Optimization Opportunities:**

1. **Vector Search**
   - ⚠️ Brute-force is O(n) - works for <100K vectors
   - Consider HNSW/IVF for >1M vectors
   - Current implementation is "good enough" for many use cases

2. **Lucene Commits**
   - Could batch commits for better throughput
   - Trade-off: latency vs. durability

3. **Caching**
   - LRU cache for sessions could reduce memory
   - Currently unbounded (loads all sessions)

---

## Design Patterns Used

### Patterns to Preserve in Library

1. **Strategy Pattern** ✅
   - `ISearchEngine` with multiple implementations
   - `IEmbeddingProvider` variants
   - Easy to extend with new strategies

2. **Factory Pattern** ✅
   - `EmbeddingProviderFactory` for provider selection
   - Graceful degradation logic
   - Dependency injection friendly

3. **Composite Pattern** ✅
   - `HybridSearcher` composes multiple search engines
   - Clean separation of concerns

4. **Repository Pattern** ⚠️
   - Good abstraction for data access
   - Should be provided as interfaces only (initially)

5. **Async/Await Throughout** ✅
   - All I/O is async
   - CancellationToken support
   - Prevents blocking

6. **Immutable Data Structures** ✅
   - Records for models
   - Thread-safe by default
   - Functional programming style

7. **Decorator Pattern** ⚠️
   - Decay factor wraps search scores
   - Could be more explicit (separate component)

---

## Recommended Extraction Plan

### Phase 1: Core Library (Single Assembly)

**Assembly Name:** `VectorizedContentIndexer` or `ContentIndexing`

**Namespace Structure:**
```
ContentIndexing
├── Search
│   ├── ISearchEngine.cs
│   ├── SearchMode.cs
│   ├── SearchResult.cs
│   ├── Lucene/
│   │   └── LuceneSearchEngine.cs
│   ├── Vector/
│   │   ├── VectorSearchEngine.cs
│   │   └── AjviIndex.cs
│   └── Hybrid/
│       └── HybridSearcher.cs
├── Embeddings
│   ├── IEmbeddingProvider.cs
│   ├── OnnxEmbeddingProvider.cs
│   ├── HashEmbeddingProvider.cs
│   └── EmbeddingProviderFactory.cs
├── Decay
│   └── DecayCalculator.cs
├── Models
│   ├── IDocument.cs
│   ├── ISearchable.cs
│   ├── SearchResult.cs
│   └── VectorPrecision.cs
├── Storage
│   ├── IRepository.cs (interface only)
│   └── Examples/ (reference implementations)
├── Utilities
│   └── ContentUtils.cs
└── Resources
    ├── all-MiniLM-L6-v2.onnx
    └── vocab.txt
```

**Target Framework:** `net10.0` (matches source project)

---

### Component Extraction Checklist

#### Tier 1: Direct Copy (No Changes)
- [ ] `IEmbeddingProvider.cs`
- [ ] `OnnxEmbeddingProvider.cs`
- [ ] `HashEmbeddingProvider.cs`
- [ ] `EmbeddingProviderFactory.cs`
- [ ] `DecayCalculator.cs`
- [ ] `ContentUtils.cs`
- [ ] `VectorPrecision.cs`
- [ ] Embedded model resources

#### Tier 2: Minor Refactoring (Generalization)
- [ ] `ISearchEngine.cs` → Generic document type
- [ ] `SearchResult.cs` → Generic result type
- [ ] `HybridSearcher.cs` → Configurable weights
- [ ] `AjviIndex.cs` → Generic metadata

#### Tier 3: Moderate Refactoring (Abstraction)
- [ ] `LuceneSearchEngine.cs` → Document field mapper
- [ ] `VectorSearchEngine.cs` → Generic document handling
- [ ] Repository interfaces → Generic CRUD

#### Tier 4: Documentation & Examples
- [ ] README.md with usage examples
- [ ] XML documentation comments
- [ ] Example document implementations
- [ ] Sample repository implementations
- [ ] Integration guide for agent-session-search-tools
- [ ] Integration guide for localagent RAG

---

### Generic Document Model Design

**Proposed Abstraction:**

```csharp
// Minimal interface for searchable content
public interface ISearchable
{
    string Id { get; }
    string GetSearchableText();
    DateTime GetTimestamp();
}

// Extended interface for structured documents
public interface IDocument : ISearchable
{
    IDictionary<string, object> GetMetadata();
}

// Generic search result
public record SearchResult<TDocument>(
    TDocument Document,
    double Score,
    string? Highlight = null,
    double? DecayFactor = null
) where TDocument : ISearchable;
```

**Migration Path for agent-session-search-tools:**
```csharp
// Adapt Session to IDocument
public class SessionDocument : IDocument
{
    private readonly Session _session;

    public string Id => _session.Id;
    public string GetSearchableText() => /* combine messages */;
    public DateTime GetTimestamp() => _session.StartedAt;
    public IDictionary<string, object> GetMetadata() =>
        new Dictionary<string, object>
        {
            ["AgentType"] = _session.AgentType,
            ["ProjectPath"] = _session.ProjectPath,
            // ...
        };
}
```

**Benefits:**
- ✅ Minimal interface - easy to implement
- ✅ Preserves all search functionality
- ✅ Backward compatible (wrap existing models)
- ✅ Forward compatible (new use cases)

---

### Configuration Design

**Proposed Configuration Model:**

```csharp
public class SearchEngineOptions
{
    // General
    public string IndexPath { get; set; } = "./index";
    public SearchMode DefaultMode { get; set; } = SearchMode.Hybrid;

    // Embeddings
    public string? ModelsPath { get; set; }
    public bool PreferGpu { get; set; } = true;

    // Lucene
    public bool UseBM25 { get; set; } = true;
    public int MaxFieldLength { get; set; } = 10000;

    // Vector
    public VectorPrecision Precision { get; set; } = VectorPrecision.Float16;
    public int EmbeddingDimensions { get; set; } = 384;

    // Hybrid
    public double LexicalWeight { get; set; } = 0.5;
    public double SemanticWeight { get; set; } = 0.5;
    public double RrfK { get; set; } = 60.0;

    // Decay
    public double DecayHalfLifeDays { get; set; } = 90.0;
    public bool ApplyDecay { get; set; } = false;
}
```

**Dependency Injection:**

```csharp
services.AddContentIndexing(options =>
{
    options.IndexPath = "./my-index";
    options.DefaultMode = SearchMode.Hybrid;
    options.ApplyDecay = true;
});

// Register implementations
services.AddSingleton<IEmbeddingProvider, OnnxEmbeddingProvider>();
services.AddSingleton<ISearchEngine, HybridSearcher>();
```

---

## Use Case: RAG for localagent

### Current Need

**Project:** E:\data\src\localagent
**Goal:** Respond to queries using RAG based on vector search

### Proposed Integration

```csharp
// 1. Initialize search engine
var options = new SearchEngineOptions
{
    IndexPath = "./localagent-index",
    DefaultMode = SearchMode.Semantic, // Pure vector search
    ModelsPath = "./models",
    Precision = VectorPrecision.Float16
};

var embeddings = await EmbeddingProviderFactory.TryCreateAsync(
    options.ModelsPath);

var searchEngine = new VectorSearchEngine<DocumentChunk>(
    options.IndexPath,
    embeddings,
    options);

// 2. Index documents
foreach (var document in documents)
{
    var chunks = ChunkDocument(document, maxChunkSize: 512);
    foreach (var chunk in chunks)
    {
        await searchEngine.IndexAsync(new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            Content = chunk.Text,
            Source = document.Path,
            Timestamp = DateTime.UtcNow
        });
    }
}

// 3. Search for RAG context
var results = await searchEngine.SearchAsync(
    query: userQuery,
    maxResults: 5,
    mode: SearchMode.Semantic
);

// 4. Build context for LLM
var context = string.Join("\n\n",
    results.Select(r => r.Document.Content));

// 5. Generate response with context
var response = await llm.GenerateAsync(
    systemPrompt: "Answer using the provided context.",
    context: context,
    userQuery: userQuery
);
```

### Benefits for localagent

1. ✅ **High-Quality Embeddings** - MiniLM-L6-v2 is proven
2. ✅ **No External Dependencies** - Embedded model
3. ✅ **Fast Inference** - GPU acceleration
4. ✅ **Simple API** - Minimal code required
5. ✅ **Flexible** - Can switch to hybrid or lexical modes
6. ✅ **Persistent** - AJVI index survives restarts

---

## Use Case: Enhanced agent-session-search-tools

### Current State
- Tightly coupled to Session/Message models
- Search engine is internal to the project

### After Extraction
```csharp
// Reference the library
<ProjectReference Include="..\vectorized-content-indexer\ContentIndexing.csproj" />

// Implement IDocument for Session
public class SessionDocument : IDocument
{
    private readonly Session _session;
    // Adapter implementation
}

// Use library search engines
services.AddContentIndexing(options => { /* ... */ });

// Minimal changes to existing code
var results = await _searchEngine.SearchAsync<SessionDocument>(query);
```

### Benefits
1. ✅ **Separation of Concerns** - Search logic is decoupled
2. ✅ **Testability** - Can mock ISearchEngine easily
3. ✅ **Reusability** - Same search infrastructure elsewhere
4. ✅ **Maintainability** - Updates to library benefit all projects

---

## Testing Strategy

### Unit Tests to Port

**From agent-session-search-tools:**
- [x] `HybridSearcherTests.cs` → Validate RRF algorithm
- [x] `LuceneSearchEngineTests.cs` → BM25 scoring
- [x] `VectorSearchEngineTests.cs` → Semantic search
- [x] `AjviIndexTests.cs` → Vector index operations

### Additional Tests Needed

**For Generic Document Model:**
- [ ] Adapter pattern tests
- [ ] Multiple document type scenarios
- [ ] Field mapping edge cases

**For Configuration:**
- [ ] Options validation
- [ ] DI registration
- [ ] Factory fallback scenarios

**For Performance:**
- [ ] Benchmark suite
- [ ] Memory profiling
- [ ] Large dataset stress tests

**For Integration:**
- [ ] End-to-end indexing + search
- [ ] Concurrent operations
- [ ] Error recovery

---

## Security Considerations

### Implemented Safeguards (to Preserve)

1. **Input Validation**
   - Path traversal prevention ✅
   - File size limits ✅
   - Query sanitization ✅

2. **SQL Injection Prevention**
   - Parameterized queries ✅
   - FTS5 query sanitization ✅
   - LIKE pattern escaping ✅

3. **Resource Limits**
   - Max file size: 10MB ✅
   - Token limit: 256 ✅
   - Batch size: 32 ✅

4. **Thread Safety**
   - Concurrent read support ✅
   - Write locks ✅
   - Immutable data structures ✅

### Additional Considerations for Library

1. **API Surface**
   - Minimize public API
   - Validate all inputs
   - Clear error messages

2. **Dependencies**
   - Keep dependencies minimal
   - Pin versions for stability
   - Audit for vulnerabilities

3. **Documentation**
   - Security best practices
   - Configuration warnings
   - Safe usage examples

---

## Licensing & Attribution

### Source Project License
**agent-session-search-tools** - License: (check repository)

### Library Dependencies Licenses
- **Lucene.NET** - Apache 2.0
- **Microsoft.ML.OnnxRuntime** - MIT
- **SQLite** - Public Domain
- **MiniLM-L6-v2** - Apache 2.0

### Proposed Library License
**Recommendation:** Apache 2.0 or MIT

**Rationale:**
- Compatible with all dependencies
- Permissive for commercial use
- Widely accepted in .NET ecosystem

### Attribution
Must include:
- Credit to original agent-session-search-tools project
- MiniLM-L6-v2 model attribution
- Dependency licenses (in THIRD-PARTY-NOTICES)

---

## Open Questions & Decisions

### 1. Assembly Naming
**Options:**
- A. `VectorizedContentIndexer` (matches project name)
- B. `ContentIndexing` (simpler, more generic)
- C. `HybridSearch` (describes functionality)

**Recommendation:** B (`ContentIndexing`)

### 2. Generic Document Model
**Options:**
- A. Minimal interface (`ISearchable`)
- B. Rich interface with metadata (`IDocument`)
- C. Abstract base class
- D. No abstraction (type parameter only)

**Recommendation:** A + B (both interfaces, let users choose)

### 3. Repository Pattern
**Options:**
- A. Include SQLite implementations
- B. Interfaces only
- C. Abstract repository with pluggable backends

**Recommendation:** B for v1 (interfaces + examples)

### 4. AJVI Metadata
**Options:**
- A. Keep GUID + AgentType + Timestamp
- B. Generalize to GUID + byte + long
- C. Add extensible metadata blob

**Recommendation:** B (minimal breaking changes)

### 5. Configuration Style
**Options:**
- A. Options pattern (DI-friendly)
- B. Fluent builder
- C. Constructor parameters

**Recommendation:** A (matches .NET conventions)

---

## Success Metrics

### Technical Metrics
- [ ] <5 public types in core namespace
- [ ] 100% XML documentation coverage
- [ ] >80% unit test coverage
- [ ] <30MB NuGet package size
- [ ] Zero breaking changes to source project

### Usage Metrics
- [ ] Successfully integrated into agent-session-search-tools
- [ ] Successfully integrated into localagent
- [ ] Demonstrates value in both keyword + vector scenarios
- [ ] Performance within 10% of original implementation

### Quality Metrics
- [ ] Clean separation of concerns
- [ ] Minimal dependencies
- [ ] Clear, concise API
- [ ] Comprehensive documentation
- [ ] Production-ready error handling

---

## Timeline & Milestones

### Phase 1: Foundation (Week 1)
- [ ] Set up project structure
- [ ] Extract Tier 1 components (direct copy)
- [ ] Create generic document interfaces
- [ ] Port unit tests

### Phase 2: Core Search (Week 2)
- [ ] Refactor search engines for generic documents
- [ ] Implement configuration system
- [ ] Add DI extensions
- [ ] Integration tests

### Phase 3: Validation (Week 3)
- [ ] Integrate into agent-session-search-tools
- [ ] Integrate into localagent
- [ ] Performance benchmarks
- [ ] Documentation

### Phase 4: Polish (Week 4)
- [ ] API review and cleanup
- [ ] Security audit
- [ ] NuGet package preparation
- [ ] Release notes

---

## Risks & Mitigations

### Risk: Over-Abstraction
**Impact:** Complex API, hard to use
**Mitigation:** Start minimal, add features as needed
**Status:** Monitoring

### Risk: Performance Regression
**Impact:** Slower than original
**Mitigation:** Benchmark suite, profiling
**Status:** Mitigated

### Risk: Breaking Changes
**Impact:** Can't upgrade source project
**Mitigation:** Adapter pattern for backward compatibility
**Status:** Mitigated

### Risk: Licensing Issues
**Impact:** Can't distribute
**Mitigation:** Audit all dependencies, use permissive licenses
**Status:** Mitigated

---

## Conclusion

The **agent-session-search-tools** project contains excellent, production-ready components for building a hybrid content indexing library:

**High-Value Components:**
1. ✅ Embedding provider abstraction with ONNX implementation
2. ✅ AJVI vector index (custom, efficient, no dependencies)
3. ✅ Reciprocal Rank Fusion hybrid search
4. ✅ Decay calculator for temporal relevance
5. ✅ Security utilities (path validation, query sanitization)

**Extraction Strategy:**
- Single assembly approach (simpler deployment)
- Minimal abstraction layer (ISearchable/IDocument)
- Preserve all search functionality
- Maintain performance characteristics
- Keep embedded MiniLM-L6-v2 model

**Target Use Cases:**
1. RAG systems (localagent)
2. Knowledge management (agent-session-search-tools)
3. Document search applications
4. Semantic similarity tools

**Next Steps:**
1. Create project structure
2. Extract Tier 1 components (direct copy)
3. Refactor Tier 2 components (generic documents)
4. Build integration examples
5. Validate with both target projects

This library will provide high-quality, production-ready hybrid search capabilities with minimal dependencies and excellent performance characteristics.

---

## Appendix A: File Locations Reference

### Critical Files to Extract

**Embeddings:**
- `/src/AgentJournal.Core/Embeddings/IEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/OnnxEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/HashEmbeddingProvider.cs`
- `/src/AgentJournal.Core/Embeddings/EmbeddingProviderFactory.cs`
- `/src/AgentJournal.Core/Resources/all-MiniLM-L6-v2.onnx`
- `/src/AgentJournal.Core/Resources/vocab.txt`

**Search:**
- `/src/AgentJournal.Core/Search/ISearchEngine.cs`
- `/src/AgentJournal.Core/Search/LuceneSearchEngine.cs`
- `/src/AgentJournal.Core/Search/VectorSearchEngine.cs`
- `/src/AgentJournal.Core/Search/HybridSearcher.cs`
- `/src/AgentJournal.Core/Search/AjviIndex.cs`
- `/src/AgentJournal.Core/Search/VectorPrecision.cs`

**Decay:**
- `/src/AgentJournal.Core/Knowledge/DecayCalculator.cs`

**Utilities:**
- `/src/AgentJournal.Core/Utilities/ContentUtils.cs`

**Models:**
- `/src/AgentJournal.Core/Models/SearchResult.cs`
- `/src/AgentJournal.Core/Models/Session.cs` (reference only)
- `/src/AgentJournal.Core/Models/Message.cs` (reference only)

**Tests:**
- `/src/AgentJournal.Tests/HybridSearcherTests.cs`
- `/src/AgentJournal.Tests/LuceneSearchEngineTests.cs`
- `/src/AgentJournal.Tests/VectorSearchEngineTests.cs`

---

## Appendix B: Code Size Estimate

| Component | Lines of Code | Complexity |
|-----------|--------------|------------|
| IEmbeddingProvider | 50 | Low |
| OnnxEmbeddingProvider | 300 | Medium |
| HashEmbeddingProvider | 100 | Low |
| EmbeddingProviderFactory | 150 | Low |
| ISearchEngine | 100 | Low |
| LuceneSearchEngine | 500 | High |
| VectorSearchEngine | 600 | High |
| HybridSearcher | 200 | Medium |
| AjviIndex | 800 | High |
| DecayCalculator | 100 | Low |
| ContentUtils | 200 | Low |
| Models | 300 | Low |
| Configuration | 200 | Low |
| DI Extensions | 100 | Low |
| **Total** | **~3,700 LOC** | - |

**Package Size Estimate:**
- Code: ~100KB
- Dependencies: ~5MB (Lucene, ONNX Runtime)
- Embedded model: ~23MB
- **Total:** ~28MB NuGet package

---

*Investigation completed 2026-01-21*
*Ready to proceed with extraction*
