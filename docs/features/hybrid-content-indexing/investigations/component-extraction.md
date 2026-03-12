# Investigation: Component Extraction from agent-session-search-tools

**Feature**: hybrid-content-indexing
**Status**: In Progress
**Created**: 2026-01-21

## Approach

Extract production-ready search components from the `agent-session-search-tools` codebase into a single-assembly reusable library. The approach involves:

### What We Build

A unified .NET library (`ContentIndexing.dll`) containing:

1. **Embedding Layer**
   - `IEmbeddingProvider` interface for pluggable embedding models
   - `OnnxEmbeddingProvider` with embedded MiniLM-L6-v2 (384 dimensions)
   - `HashEmbeddingProvider` as fallback for non-semantic scenarios
   - Factory with graceful degradation (ONNX → Hash)

2. **Search Engines**
   - `ISearchEngine<TDocument>` interface supporting Lexical/Semantic/Hybrid modes
   - `LuceneSearchEngine` - BM25 keyword search with Lucene.NET
   - `VectorSearchEngine` - Semantic search using AJVI custom vector index
   - `HybridSearcher` - Reciprocal Rank Fusion (RRF) combining both

3. **Vector Index (AJVI)**
   - Custom binary format with memory-mapped file access
   - Float16 precision support (50% storage savings vs Float32)
   - SHA256 content deduplication
   - O(1) append, O(n*d) brute-force search (suitable for <1M vectors)

4. **Temporal Relevance**
   - `DecayCalculator` - Exponential decay using configurable half-life
   - Reinforcement tracking for boosting recently accessed content
   - Classification (Fresh/Good/Aging/Decaying/Expiring)

5. **Generic Document Model**
   - Minimal `ISearchable` interface (Id, GetSearchableText, GetTimestamp)
   - Extended `IDocument` interface with metadata support
   - `SearchResult<TDocument>` for type-safe results

6. **Utilities & Security**
   - Path traversal prevention
   - File size validation (10MB default limit)
   - FTS5 query sanitization
   - SQL injection prevention

### How It Works

**Indexing Flow:**
```
Document → ISearchable adapter
    ├─→ Embed text (ONNX/Hash) → Normalize → AJVI.AddEntry
    └─→ Extract fields → Lucene.Document → LuceneWriter.AddDocument
```

**Search Flow:**
```
Query → SearchMode selector
    ├─→ Lexical: Parse → BM25 search → Score
    ├─→ Semantic: Embed → AJVI.Search → Score
    └─→ Hybrid: Both in parallel → RRF fusion → Combined score

Optional: Apply decay factor → Final ranking → Return top-K
```

**Configuration:**
```csharp
services.AddContentIndexing(options =>
{
    options.IndexPath = "./index";
    options.DefaultMode = SearchMode.Hybrid;
    options.Precision = VectorPrecision.Float16;
    options.LexicalWeight = 0.5;
    options.SemanticWeight = 0.5;
    options.DecayHalfLifeDays = 90.0;
});
```

## Tradeoffs

| Pros | Cons |
|------|------|
| **Embedded model** - No download/setup complexity, works offline | **Large package** - 28MB NuGet package due to 23MB model |
| **Single assembly** - Simple deployment, no version conflicts | **Less modular** - Can't exclude components you don't need |
| **Custom AJVI index** - No external dependencies (Faiss/Annoy), simple binary format | **Limited scalability** - Brute-force O(n) search unsuitable for >1M vectors |
| **Generic document model** - Works with any content type via adapter | **Adapter boilerplate** - Users must implement ISearchable/IDocument |
| **Production-tested** - Components already proven in agent-session-search-tools | **Domain coupling** - Some abstractions leak agent/session concepts |
| **GPU acceleration** - DirectML support for 10-20x faster embeddings | **DirectML dependency** - Adds ~5MB to package, Windows-focused |
| **Float16 precision** - 50% storage savings, minimal quality loss | **Reduced precision** - May impact similarity scores in edge cases |
| **Hybrid search (RRF)** - Best-of-both-worlds for keyword + semantic | **No reranking** - RRF is fusion, not true cross-encoder reranking |
| **Temporal decay** - Boost fresh content automatically | **Configuration complexity** - Half-life tuning requires domain knowledge |
| **Thread-safe** - Concurrent reads, locked writes | **Write bottleneck** - Single writer per index (Lucene limitation) |

## Alignment

- [x] **Follows architectural layering rules**
  - Clear separation: Embeddings → Search → Storage
  - Interface-based abstractions (`IEmbeddingProvider`, `ISearchEngine`)
  - No circular dependencies

- [x] **Developer Experience (works with minimal setup)**
  - Embedded model eliminates manual downloads
  - Factory pattern provides graceful fallback (ONNX → Hash)
  - Options pattern integrates with ASP.NET DI
  - XML documentation on all public APIs

- [x] **Specification compliance**
  - Lucene.NET follows Apache Lucene standards
  - ONNX Runtime follows ONNX spec
  - BM25 scoring is standard Lucene implementation
  - RRF follows established research (used by Elasticsearch, etc.)

- [x] **Consistent with existing patterns**
  - Async/await throughout (matches .NET conventions)
  - Immutable records for models (functional style)
  - Options pattern for configuration (.NET standard)
  - Repository pattern for storage abstraction

## Evidence

### 1. Codebase Exploration

**Source Analysis:** `E:\data\src\agent-session-search-tools`

#### Search Engine Architecture
```
Files analyzed:
- /src/AgentJournal.Core/Search/ISearchEngine.cs (interface)
- /src/AgentJournal.Core/Search/LuceneSearchEngine.cs (500 LOC, BM25)
- /src/AgentJournal.Core/Search/VectorSearchEngine.cs (600 LOC, semantic)
- /src/AgentJournal.Core/Search/HybridSearcher.cs (200 LOC, RRF)

Key findings:
✅ Clean abstraction - ISearchEngine is 100% generic except Session coupling
✅ Thread-safe - SemaphoreSlim for writes, SearcherManager for concurrent reads
✅ Tested - HybridSearcherTests.cs validates RRF algorithm correctness
⚠️  Session/Message models are tightly coupled (needs generic document wrapper)
```

#### Embedding System
```
Files analyzed:
- /src/AgentJournal.Core/Embeddings/IEmbeddingProvider.cs (interface)
- /src/AgentJournal.Core/Embeddings/OnnxEmbeddingProvider.cs (300 LOC)
- /src/AgentJournal.Core/Embeddings/EmbeddingProviderFactory.cs (150 LOC)
- /src/AgentJournal.Core/Resources/all-MiniLM-L6-v2.onnx (23MB)

Key findings:
✅ Zero coupling - IEmbeddingProvider is 100% generic
✅ Embedded resources - Model extracted from assembly at runtime
✅ GPU acceleration - DirectML provider with CPU fallback
✅ Thread-safe - SemaphoreSlim guards ONNX InferenceSession (not thread-safe)
✅ Graceful degradation - Factory tries ONNX, falls back to hash-based
```

#### AJVI Vector Index
```
Files analyzed:
- /src/AgentJournal.Core/Search/AjviIndex.cs (800 LOC)

Format specification:
- Header: Magic "AJVI", version, precision, dimensions, entry count
- Entry: SHA256 hash (32B) + GUID (16B) + AgentType (1B) + Timestamp (8B) + Vector (d×2 or d×4)
- Memory-mapped file for O(1) access

Key findings:
✅ Production-ready - Handles file corruption, version checking
✅ Efficient - Float16 saves 50% space, memory-mapped I/O
✅ Deduplication - SHA256 content hashing prevents duplicates
⚠️  AgentType enum is domain-specific (can generalize to byte field)
⚠️  Brute-force search limits scalability (acceptable for <1M vectors)
```

#### Performance Benchmarks (from source project)
```
Indexing:
- Embed single message: ~15ms CPU, ~2ms GPU (DirectML)
- Index session (10 msgs): ~150ms CPU, ~30ms GPU
- Lucene index session: ~5ms (mostly I/O)
- AJVI add entry: <1ms (append)

Searching:
- Lexical (10K sessions): ~20ms (BM25)
- Semantic (100K vectors): ~80ms (brute-force)
- Semantic (1M vectors): ~800ms (needs optimization beyond this scale)
- Hybrid (10K sessions): ~30ms (parallel execution)

Storage:
- AJVI Float32: 1,593 bytes/entry (384 dims)
- AJVI Float16: 825 bytes/entry (48% savings)
- Lucene: ~500-2000 bytes/doc (varies by content)
```

### 2. Prior Art in Similar Systems

#### Elasticsearch Hybrid Search
- Uses RRF for combining BM25 + kNN vector search
- Default `rrf_k = 60` (same as our implementation)
- Production-proven at massive scale

#### Vespa.ai
- Combines BM25 (Lucene-based) + HNSW vector search
- Supports custom ranking expressions
- Multi-phase retrieval (retrieve → rank → rerank)

#### Semantic Kernel (Microsoft)
- Memory connectors abstract different vector DBs
- Plugin pattern for embedding providers
- Similar `IMemoryStore` interface to our `ISearchEngine`

**Key takeaway:** Our approach aligns with industry patterns (RRF fusion, embedding abstraction, hybrid retrieval).

### 3. Related ADR Patterns

**From source project analysis:**

No formal ADRs exist in agent-session-search-tools, but code reveals implicit decisions:

- **Why Lucene.NET over SQLite FTS5?**
  - Evidence: Both are used (Lucene for sessions, FTS5 for knowledge)
  - Reasoning: Lucene provides better BM25 tuning, field boosting, query parsing
  - FTS5 used where simpler full-text search suffices

- **Why custom AJVI vs. Faiss/Annoy?**
  - Evidence: No external vector DB dependencies
  - Reasoning: Simpler deployment, full control, adequate for expected scale
  - Trade-off: Limited to brute-force search (O(n*d))

- **Why embedded model vs. download?**
  - Evidence: Model is in Resources/, extracted at startup
  - Reasoning: Offline support, no API keys, consistent results
  - Trade-off: Larger package size (28MB)

### 4. Code Quality Analysis

**Metrics from source project:**
```
Total code to extract: ~3,700 LOC
- Embeddings: ~600 LOC (high quality, zero coupling)
- Search engines: ~1,300 LOC (good quality, some coupling)
- AJVI index: ~800 LOC (excellent quality, minimal coupling)
- Utilities: ~300 LOC (high quality, zero coupling)
- Models/config: ~700 LOC (needs refactoring for generics)

Test coverage:
- HybridSearcherTests.cs: 12 tests, RRF algorithm validated
- LuceneSearchEngineTests.cs: 8 tests, BM25 scoring validated
- VectorSearchEngineTests.cs: 10 tests, semantic search validated

Design patterns used:
✅ Strategy (ISearchEngine implementations)
✅ Factory (EmbeddingProviderFactory)
✅ Composite (HybridSearcher)
✅ Repository (ISessionRepository, etc.)
✅ Async/await throughout
✅ Immutable records
```

### 5. Dependency Analysis

**NuGet Dependencies:**
```xml
Core (required):
- Lucene.Net 4.8.0-beta00016 (Apache 2.0)
- Microsoft.ML.OnnxRuntime.DirectML 1.20.0 (MIT)
- Microsoft.ML.Tokenizers 1.0.0 (MIT)
- System.Numerics.Tensors 10.0.1 (MIT)

Framework (likely already referenced):
- Microsoft.Extensions.DependencyInjection 10.0.2
- Microsoft.Extensions.Logging 10.0.2
- Microsoft.Data.Sqlite 10.0.2

Total additional size: ~5MB (plus 23MB model)
All licenses: Permissive (Apache 2.0 / MIT)
```

### 6. Security Audit

**Implemented safeguards in source:**
```csharp
✅ Path validation (ContentUtils.ValidatePath)
   - Prevents directory traversal
   - Validates against allowed base paths

✅ File size limits (ContentUtils.ValidateFileSize)
   - Default: 10MB max
   - Prevents memory exhaustion

✅ Query sanitization (ContentUtils.SanitizeFts5Query)
   - Prevents FTS5 injection
   - Escapes LIKE patterns

✅ Thread safety
   - Read/write locks on indexes
   - Immutable data structures
   - Semaphore guards on ONNX session

✅ Input validation
   - Token limits (256 max sequence length)
   - Batch size limits (32)
   - Dimension validation
```

### 7. Integration Feasibility

**See [gaps-and-integration-details.md](gaps-and-integration-details.md) for complete integration examples including:**
- Full `SessionDocumentAdapter` implementation
- Custom Lucene field mapping (`SessionFieldMapper`)
- Dependency injection setup
- Search service facade pattern
- Migration guide from current implementation

**Quick Preview - agent-session-search-tools (source project):**
```csharp
// Adapter pattern (backward compatible):
public class SessionDocumentAdapter : IDocument, IHierarchicalDocument<MessageDocumentAdapter>
{
    private readonly Session _session;

    public string Id => _session.Id;
    public string GetSearchableText() =>
        string.Join("\n", _session.Messages.Select(m => $"[{m.Role}] {m.Content}"));
    public DateTime GetTimestamp() => _session.StartedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["AgentType"] = _session.AgentType,
        ["ProjectPath"] = _session.ProjectPath,
        ["GitBranch"] = _session.GitBranch,
        ["IsActive"] = _session.IsActive,
        ["MessageCount"] = _session.MessageCount,
        ["ToolNames"] = string.Join(",", _session.AllToolCalls.Select(tc => tc.Name))
    };

    public IReadOnlyList<MessageDocumentAdapter> GetChildren() =>
        _session.Messages.Select(m => new MessageDocumentAdapter(m, _session.Id)).ToList();

    public Session UnderlyingSession => _session; // For result unwrapping
}

// Field mapper controls Lucene indexing:
public class SessionFieldMapper : IDocumentFieldMapper<SessionDocumentAdapter>
{
    public IEnumerable<IIndexableField> MapToFields(SessionDocumentAdapter doc)
    {
        yield return new StringField("Id", doc.Id, Field.Store.YES);
        yield return new TextField("Content", doc.GetSearchableText(), Field.Store.YES);
        yield return new StringField("AgentType", doc.GetMetadata()["AgentType"].ToString()!);
        // ... see detailed examples for complete mapping
    }
}

// Usage via facade service (domain models stay clean):
var results = await _sessionSearchService.SearchSessionsAsync(
    query: "authentication error",
    filters: new SessionSearchFilters(AgentTypes: new[] { "claude-code" })
);
// Returns IReadOnlyList<SessionSearchResult> with original Session objects
```

**For localagent (RAG system):**
```csharp
// Simple document chunks (minimal interface):
public record DocumentChunk : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string Source { get; init; }
    public required DateTime Timestamp { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => Timestamp;
}

// RAG pipeline (pure semantic search):
var embeddings = await EmbeddingProviderFactory.TryCreateAsync("./models");
var search = new VectorSearchEngine<DocumentChunk>("./index", embeddings);

await search.IndexAsync(new DocumentChunk { /* ... */ });

var results = await search.SearchAsync(userQuery, maxResults: 5);
var context = string.Join("\n\n", results.Select(r => r.Document.Content));
var response = await llm.GenerateAsync(context, userQuery);
```

## Alternative Approaches

This is the first investigation for the hybrid-content-indexing feature. Other approaches worth investigating:

### 1. Multi-Assembly Plugin Architecture
**Topic**: `plugin-based-assemblies`

Split into separate packages:
- `ContentIndexing.Core` (interfaces only)
- `ContentIndexing.Lucene` (keyword search)
- `ContentIndexing.Semantic` (vector search + ONNX)
- `ContentIndexing.Hybrid` (RRF fusion)

**Pros**: Smaller packages, pay-for-what-you-use, easier to swap implementations
**Cons**: Version management complexity, more NuGet packages to maintain

### 2. Embedded Vector DB Integration
**Topic**: `embedded-vector-db`

Replace AJVI with established embedded vector databases:
- Chroma (Python-based, needs hosting)
- LanceDB (Rust-based, .NET bindings)
- DuckDB with VSS extension

**Pros**: Better scalability (HNSW/IVF), mature implementations
**Cons**: External dependencies, deployment complexity, larger footprint

### 3. Cloud-Native Architecture
**Topic**: `cloud-native-search`

Use managed services instead of embedded components:
- Azure AI Search (keyword + vector + reranking)
- Elasticsearch (BM25 + kNN)
- Pinecone (vector only)

**Pros**: Massive scalability, managed infrastructure, advanced features (reranking)
**Cons**: Requires internet, API costs, vendor lock-in, latency

## Known Gaps

See [gaps-and-integration-details.md](gaps-and-integration-details.md) for comprehensive analysis including:
- Detailed parent-child document relationship design with 4 indexing strategies
- Multi-tenancy clarification (NOT a gap - naturally supported)
- Complete integration examples

**Critical Gaps:**
1. **Reranking** - RRF fusion is not true cross-encoder reranking (mentioned in feature goals)
2. **Field Mapping** - `IDocumentFieldMapper` needs detailed design for Lucene customization
3. **Hierarchical Documents** - Parent-child relationships (Session→Messages) need abstraction
   - ✅ **Detailed design added** in gaps-and-integration-details.md
   - Includes `IHierarchicalDocument<TChild>` interface
   - Four indexing strategies: ParentOnly, ChildrenOnly, Both, ParentWithEmbedded
   - Context expansion API for "N messages before/after"

**Medium Priority:**
4. **Context Expansion** - ✅ **Solved** via `IHierarchicalDocument.GetChildrenBefore/After()`
5. **Index Lifecycle** - No coverage of maintenance (optimize, prune, backup)

**Low Priority:**
6. **Batch Operations** - API not detailed (can use multiple calls initially)
7. **Query Expansion** - No synonym support (Lucene.NET supports, users can add)
8. **Highlighting** - Basic implementation sufficient, could expose Lucene's advanced highlighter
9. **Observability** - No logging/metrics (easy to add ILogger integration)

**Not Gaps:**
- ✅ **Multi-Tenancy** - Multiple engine instances with different index paths (naturally supported)

## Open Questions

1. **Generic document model**
   - ❓ Should we require IDocument or allow ISearchable only?
   - ❓ How to handle metadata for Lucene field mapping?
   - **Resolution needed**: User testing with both interfaces
   - **See**: [gaps-and-integration-details.md](gaps-and-integration-details.md) for proposed `IDocumentFieldMapper` design

2. **AJVI metadata fields**
   - ❓ Keep GUID + AgentType + Timestamp, or generalize?
   - ❓ Add extensible metadata blob (JSON/binary)?
   - **Resolution needed**: Review use cases beyond agent sessions

3. **Repository pattern**
   - ❓ Include SQLite repository implementations or interfaces only?
   - ❓ Provide FTS5 integration helpers?
   - **Resolution needed**: Evaluate demand for batteries-included vs. minimal

4. **Package naming**
   - ❓ `VectorizedContentIndexer` (project name) vs. `ContentIndexing` (generic)?
   - **Leaning toward**: `ContentIndexing` (clearer, more marketable)

5. **Float16 default**
   - ❓ Default to Float16 or Float32 precision?
   - **Evidence**: Float16 saves 50% storage, minimal quality impact in testing
   - **Leaning toward**: Float16 default with opt-in Float32

6. **Hierarchical indexing**
   - ❓ Index parent only, children only, or both?
   - ❓ How to represent parent-child in search results?
   - **See**: [gaps-and-integration-details.md](gaps-and-integration-details.md) for `IHierarchicalDocument` proposal

## Verdict

**Pending evaluation** - Awaiting:

1. ✅ Codebase exploration complete
2. ✅ Performance analysis complete
3. ✅ Security audit complete
4. ⏳ User validation (integration with localagent)
5. ⏳ API design review (generic document model)
6. ⏳ Alternative investigation (plugin-based-assemblies)

**Next Steps:**
1. Create proof-of-concept extraction (Tier 1 components only)
2. Build localagent RAG integration example
3. Measure performance delta vs. original implementation
4. Decision point: Single assembly vs. plugin architecture

**Confidence Level**: High (80%)
- Components are production-tested
- Clear extraction path identified
- Minimal refactoring required for Tier 1 components
- Uncertainty remains around generic document model ergonomics

---

**References:**
- Source codebase: `E:\data\src\agent-session-search-tools`
- Initial analysis: `INVESTIGATION.md` (legacy format, to be archived)
- Related projects: `E:\data\src\localagent`
- [Reciprocal Rank Fusion Paper](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf)
- [MiniLM Model Card](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
