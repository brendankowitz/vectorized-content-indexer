# Changelog

All notable changes to ZeroProximity.VectorizedContentIndexer will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-01-21

### Initial Release

First public release of ZeroProximity.VectorizedContentIndexer, extracted and generalized from the production-tested search components in agent-session-search-tools.

### Added

#### Core Interfaces
- `ISearchable` - Minimal interface for indexable content
- `IDocument` - Extended interface with metadata support
- `IHierarchicalDocument<TChild>` - Support for parent-child relationships
- `ISearchEngine<TDocument>` - Generic search engine contract
- `IEmbeddingProvider` - Pluggable embedding generation interface

#### Search Engines
- `LuceneSearchEngine<T>` - BM25 keyword search using Lucene.NET
  - Boolean query support (AND, OR, NOT)
  - Phrase queries
  - Field boosting
  - Customizable field mapping via `ILuceneDocumentMapper<T>`
  - Thread-safe concurrent reads
- `VectorSearchEngine<T>` - Semantic search with ONNX embeddings
  - Cosine similarity search
  - AJVI custom vector index format
  - Float16 and Float32 precision support
  - Memory-mapped file I/O
  - SHA256 content deduplication
- `HybridSearcher<T>` - Reciprocal Rank Fusion (RRF) combining lexical and semantic search
  - Configurable lexical/semantic weights
  - Parallel search execution
  - RRF constant (k) configuration

#### Embedding Providers
- `OnnxEmbeddingProvider` - High-quality embeddings using ONNX Runtime
  - Embedded MiniLM-L6-v2 model (384 dimensions, 22.7M parameters)
  - DirectML GPU acceleration (10-20x faster than CPU)
  - Automatic CPU fallback
  - Mean pooling with attention mask
  - Thread-safe with internal locking
- `HashEmbeddingProvider` - Deterministic hash-based fallback
  - Fast embedding generation (~1ms)
  - Zero ML dependencies
  - Suitable for development/testing
- `EmbeddingProviderFactory` - Factory with graceful degradation
  - `CreateAsync()` - Requires ONNX provider
  - `TryCreateAsync()` - Falls back to hash provider

#### AJVI Vector Index
- Custom binary format optimized for semantic search
- Header: Magic bytes, version, precision, dimensions, entry count
- Entry: SHA256 hash, document ID, metadata, vector
- Float16 precision support (50% storage savings)
- Memory-mapped file access for efficient I/O
- O(1) append operations
- O(n×d) brute-force search (suitable for <1M vectors)
- Content deduplication via SHA256 hashing

#### Search Features
- Three search modes:
  - `SearchMode.Lexical` - BM25 keyword search only
  - `SearchMode.Semantic` - Vector similarity search only
  - `SearchMode.Hybrid` - RRF fusion of both (default)
- Batch operations (`IndexManyAsync`, `DeleteManyAsync`)
- Index optimization (`OptimizeAsync`)
- Document counting (`GetCountAsync`)
- Index clearing (`ClearAsync`)
- Configurable result limits
- Highlighting support
- Temporal relevance decay (optional)

#### Temporal Decay
- `DecayCalculator` - Exponential decay with configurable half-life
- Decay formula: `0.5 ^ (age_days / half_life_days)`
- Decay categories: Fresh, Good, Aging, Decaying, Expiring
- Configurable via `SearchEngineOptions.ApplyDecay` and `DecayHalfLifeDays`
- Boosts recent content in search results automatically

#### Utilities
- `ContentUtils` - Security and validation helpers
  - Path traversal prevention (`ValidatePath`)
  - File size validation (`ValidateFileSize`)
  - Query sanitization (`SanitizeQuery`)
  - Text normalization (`NormalizeText`)

#### Configuration
- `SearchEngineOptions` - Centralized configuration
  - Index paths
  - Default search mode
  - Vector precision (Float16/Float32)
  - Lexical/semantic weights
  - RRF constant (k)
  - Temporal decay settings

#### Documentation
- Comprehensive README with quick start guide
- Getting Started tutorial (step-by-step)
- Complete API documentation
- Architecture overview with diagrams
- Advanced topics guides:
  - Hierarchical documents (parent-child relationships)
  - Custom field mapping (Lucene field control)
  - Temporal decay (time-based relevance)
  - Performance tuning (optimization strategies)
- Migration guide (from agent-session-search-tools)
- Contributing guide
- Two sample applications:
  - RAG example (pure vector search)
  - Agent session example (hybrid search with hierarchical docs)

#### Performance
- CPU embedding: ~15ms per document
- GPU embedding (DirectML): ~2-5ms per document
- Lexical search (10K docs): ~10-20ms
- Semantic search (100K vectors): ~50-100ms
- Hybrid search (10K docs): ~60-120ms
- Storage (Float16): ~825 bytes per vector (384 dims)
- Storage (Float32): ~1,593 bytes per vector (384 dims)

#### Dependencies
- Lucene.Net 4.8.0-beta00016
- Microsoft.ML.OnnxRuntime.DirectML 1.20.0
- Microsoft.ML.Tokenizers 1.0.0
- System.Numerics.Tensors 10.0.1
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0
- Microsoft.Extensions.Options 9.0.0

### Design Decisions

#### Why Hybrid Search?
- Combines strengths of keyword (precision) and semantic (recall) search
- RRF algorithm proven in production (Elasticsearch, Vespa.ai)
- No score normalization required
- Robust to different score distributions

#### Why Custom AJVI Index?
- Simpler deployment than Faiss/Annoy (no external dependencies)
- Full control over format and features
- Adequate performance for <1M vectors
- Memory-mapped I/O for efficient access
- Float16 support for storage savings

#### Why Embedded Model?
- Works offline (no API keys or downloads)
- Consistent results across environments
- No external service dependencies
- Trade-off: Larger package size (28MB)

#### Why Generic Interfaces?
- Decouples library from specific domain models
- Works with any content type via adapters
- Type-safe search results
- Minimal surface area (`ISearchable` has only 3 members)

### Attribution

- **Extracted from**: agent-session-search-tools (production-tested components)
- **Embedding Model**: [sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) by Nils Reimers (Apache 2.0 License)
- **RRF Algorithm**: [Cormack et al., SIGIR 2009](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf)
- **Inspired by**: Elasticsearch hybrid search, Vespa.ai multi-phase retrieval

### Known Limitations

1. **Vector Search Scalability**: Brute-force search O(n×d) limits performance beyond ~1M vectors
   - Consider approximate nearest neighbor algorithms (HNSW, IVF) for larger scales
   - Current implementation suitable for most use cases (<100K vectors)

2. **AJVI Format**: Custom format not compatible with standard vector databases
   - Intentional trade-off for simplicity and control
   - Future: Consider adding export to standard formats

3. **Reranking**: RRF fusion is not true cross-encoder reranking
   - RRF combines rankings, doesn't re-score
   - Future: Add `IReranker` interface for cross-encoder support

4. **Embedding Provider**: Limited to single ONNX model
   - Current: MiniLM-L6-v2 (384 dims)
   - Future: Support custom models, different sizes

5. **Multi-Tenancy**: No built-in tenant isolation
   - Pattern: Create separate search engine instances per tenant
   - Well-documented in architecture guide

### Breaking Changes

None (initial release).

### Security

- Path traversal prevention in index paths
- File size validation (default: 10MB limit)
- Query sanitization to prevent injection
- Input validation for all public APIs
- Thread-safe operations (concurrent reads, serialized writes)

---

## Version History

- **1.0.0** (2026-01-21) - Initial public release

---

## Future Roadmap

Potential features for future releases (not committed):

- Approximate nearest neighbor search (HNSW, IVF) for >1M vector scalability
- Cross-encoder reranking support (`IReranker` interface)
- Additional embedding provider implementations (OpenAI, Cohere, local models)
- Multi-language model support
- Query expansion and synonym support
- Advanced highlighting (Lucene FastVectorHighlighter)
- Index backup and restore utilities
- Metrics and observability (ILogger integration, IMeterFactory)
- Batch optimization for bulk operations
- Index versioning and migration tools

---

For upgrade instructions and migration guides, see [MIGRATION.md](docs/migration.md).

For contributing guidelines, see [CONTRIBUTING.md](CONTRIBUTING.md).
