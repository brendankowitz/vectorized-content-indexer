# Investigation Addendum: Gaps & Integration Details

**Parent Investigation**: component-extraction.md
**Created**: 2026-01-21

## Known Gaps

### 1. Reranking (Mentioned but Not Detailed)

**Gap**: The feature description mentions "potentially with a reranker" but the investigation only covers RRF fusion, not true semantic reranking.

**What's Missing**:
- Cross-encoder reranking (e.g., ms-marco-MiniLM)
- Two-stage retrieval (retrieve → rerank)
- Integration point for reranker plugins

**Impact**: Medium - RRF is good, but cross-encoder reranking can significantly improve precision
**Mitigation**: Could add `IReranker` interface in future investigation

```csharp
// Future API
public interface IReranker<TDocument> where TDocument : ISearchable
{
    Task<IReadOnlyList<SearchResult<TDocument>>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult<TDocument>> candidates,
        int topK,
        CancellationToken cancellationToken = default);
}
```

---

### 2. Field Mapping for Lucene (Incomplete Abstraction)

**Gap**: We mention `IDocumentFieldMapper<TDocument>` in the extraction strategy but don't show concrete implementation.

**What's Missing**:
- How custom fields are defined
- Field boosting configuration
- Analyzed vs. not-analyzed field control
- Stored vs. indexed field control
- How tool calls, metadata, etc. map to Lucene fields

**Impact**: High - Critical for making LuceneSearchEngine truly generic
**Mitigation**: Need detailed field mapper design (see integration details below)

---

### 3. Parent-Child Document Relationships (DETAILED)

**Gap**: Source project indexes both individual messages AND combined session documents. Generic approach needs design.

**Core Requirements**:
- Represent hierarchical documents (session → messages)
- Index at both parent and child levels
- Preserve message-level matches while returning session-level results
- Enable context expansion (N messages before/after match)
- Support different indexing strategies (parent-only, child-only, both)

**Impact**: High - Core use case for agent-session-search-tools
**Mitigation**: Add `IHierarchicalDocument<TChild>` interface (detailed design below)

---

### 4. Context Expansion with Generic Documents

**Gap**: VectorSearchEngine has `contextMessages` parameter but it's coupled to Message model.

**What's Missing**:
- Generic API for "get N items before/after this match"
- How to maintain ordering across different document types
- How to deduplicate expanded context

**Impact**: Medium - Important for RAG scenarios
**Mitigation**: Add `ISequentialDocument` interface with Previous/Next navigation

---

### 5. Batch Indexing Details

**Gap**: We mention batch operations but don't show API or transaction handling.

**What's Missing**:
- Batch size optimization
- Transaction semantics
- Partial failure handling
- Progress reporting

**Impact**: Low - Can use multiple `IndexAsync` calls initially
**Mitigation**: Document batch best practices, add optimized batch API later

---

### 6. Index Lifecycle Management

**Gap**: No coverage of index maintenance operations.

**What's Missing**:
- Index compaction/optimization (Lucene)
- Pruning expired entries (decay-based)
- Backup/restore procedures
- Index versioning and migration
- Corruption recovery

**Impact**: Medium - Production systems need maintenance
**Mitigation**: Add `IIndexMaintenance` interface with Optimize/Prune/Backup methods

---

### 7. Multi-Tenancy / Multiple Indexes

**Status**: ✅ **NOT A GAP** - This is naturally supported by design

**How It Works**:
Users can create multiple `ISearchEngine` instances with different index paths. Each instance is isolated.

```csharp
// Example: Multiple tenants
var tenant1Engine = new HybridSearcher<Document>(
    new LuceneSearchEngine<Document>("./indexes/tenant1/lucene", ...),
    new VectorSearchEngine<Document>("./indexes/tenant1/vector", ...));

var tenant2Engine = new HybridSearcher<Document>(
    new LuceneSearchEngine<Document>("./indexes/tenant2/lucene", ...),
    new VectorSearchEngine<Document>("./indexes/tenant2/vector", ...));

// Example: Different purposes in same app
var agentSessionsEngine = new HybridSearcher<SessionDocument>("./indexes/sessions", ...);
var knowledgeEngine = new VectorSearchEngine<KnowledgeEntry>("./indexes/knowledge", ...);
var documentsEngine = new HybridSearcher<DocumentChunk>("./indexes/docs", ...);
```

**Resource Sharing**:
- ✅ **Embedding provider** - Can be shared across all instances (singleton)
- ✅ **Configuration** - Can share options, just override IndexPath
- ✅ **DI container** - Register multiple named instances

**No Gap**: This is standard practice. Each index is a separate directory.

---

### 8. Query Expansion / Synonyms

**Gap**: No support for expanding user queries with synonyms or related terms.

**What's Missing**:
- Synonym dictionary integration
- Query rewriting
- Term expansion strategies

**Impact**: Low - Not in original source project either
**Mitigation**: Lucene.NET supports synonym filters; users can add via custom analyzer

---

### 9. Highlighting Implementation

**Gap**: Mentioned but not detailed. Current implementation is simple substring matching.

**What's Missing**:
- Lucene's built-in highlighter integration
- Vector search highlighting (no position info in embeddings)
- Customizable highlight formatting

**Impact**: Low - Basic highlighting works
**Mitigation**: Expose Lucene's FastVectorHighlighter for lexical, keep simple for semantic

---

### 10. Observability / Metrics

**Gap**: No logging, metrics, or diagnostics discussed.

**What's Missing**:
- Search latency metrics
- Index size tracking
- Cache hit rates
- Embedding generation time
- Failed indexing operations

**Impact**: Medium - Important for production monitoring
**Mitigation**: Add `ILogger` integration, emit metrics via `IMeterFactory`

---

## Parent-Child Document Relationships: Detailed Design

### Overview

Many content indexing scenarios involve hierarchical documents:
- **Sessions** contain **Messages** (agent conversations)
- **Documents** contain **Chunks** (RAG systems)
- **Threads** contain **Posts** (forums)
- **Articles** contain **Sections** (documentation)

The library needs to support multiple indexing strategies for these relationships.

### Interface Design

```csharp
// File: ContentIndexing/Models/IHierarchicalDocument.cs

namespace ContentIndexing.Models
{
    /// <summary>
    /// Represents a document that contains child documents.
    /// Enables parent-level and child-level indexing strategies.
    /// </summary>
    public interface IHierarchicalDocument<TChild> : IDocument
        where TChild : ISearchable
    {
        /// <summary>
        /// Gets the child documents for hierarchical indexing.
        /// Lazily evaluated - won't be called if using parent-only indexing.
        /// </summary>
        IReadOnlyList<TChild> GetChildren();

        /// <summary>
        /// Optional: Gets a specific child by ID for context expansion.
        /// Return null if not supported.
        /// </summary>
        TChild? GetChildById(string childId) => null;

        /// <summary>
        /// Optional: Gets N children before the specified child.
        /// Used for context expansion (e.g., "show 3 messages before this match").
        /// Return empty if not supported.
        /// </summary>
        IReadOnlyList<TChild> GetChildrenBefore(string childId, int count) =>
            Array.Empty<TChild>();

        /// <summary>
        /// Optional: Gets N children after the specified child.
        /// Used for context expansion.
        /// </summary>
        IReadOnlyList<TChild> GetChildrenAfter(string childId, int count) =>
            Array.Empty<TChild>();
    }

    /// <summary>
    /// Represents a child document that knows its parent.
    /// Enables parent retrieval from child matches.
    /// </summary>
    public interface IChildDocument : ISearchable
    {
        /// <summary>
        /// Gets the parent document ID.
        /// Used to group child matches by parent.
        /// </summary>
        string GetParentId();
    }
}
```

### Indexing Strategies

```csharp
// File: ContentIndexing/Search/HierarchicalIndexingStrategy.cs

namespace ContentIndexing.Search
{
    /// <summary>
    /// Controls how hierarchical documents are indexed.
    /// </summary>
    public enum HierarchicalIndexingStrategy
    {
        /// <summary>
        /// Index only the parent document (combined content).
        /// Pro: Simple, smaller index
        /// Con: No child-level matching details
        /// Use case: Session summaries, document overviews
        /// </summary>
        ParentOnly,

        /// <summary>
        /// Index only child documents (individual items).
        /// Pro: Precise matching, smaller parent footprint
        /// Con: Need to retrieve parent separately
        /// Use case: Message search, chunk retrieval for RAG
        /// </summary>
        ChildrenOnly,

        /// <summary>
        /// Index both parent and children (separate documents).
        /// Pro: Can search at both levels, best precision
        /// Con: Larger index, possible duplicate results
        /// Use case: Full-featured search with drill-down
        /// </summary>
        Both,

        /// <summary>
        /// Index parent with embedded child positions.
        /// Pro: Single document, preserves structure
        /// Con: Complex field mapping, limited by Lucene doc size
        /// Use case: Small hierarchies (< 100 children)
        /// </summary>
        ParentWithEmbeddedChildren
    }
}
```

### Search Result Extensions

```csharp
// File: ContentIndexing/Models/SearchResult.cs

namespace ContentIndexing.Models
{
    /// <summary>
    /// Search result for hierarchical documents.
    /// </summary>
    public record HierarchicalSearchResult<TParent, TChild>(
        TParent Parent,
        double Score,
        IReadOnlyList<ChildMatch<TChild>>? MatchedChildren = null,
        string? Highlight = null,
        double? DecayFactor = null
    ) where TParent : IHierarchicalDocument<TChild>
      where TChild : ISearchable;

    /// <summary>
    /// Represents a matched child within a parent document.
    /// </summary>
    public record ChildMatch<TChild>(
        TChild Child,
        double ChildScore,
        int Position,
        string? Highlight = null
    ) where TChild : ISearchable;

    /// <summary>
    /// Context-expanded result with surrounding children.
    /// </summary>
    public record ContextExpandedResult<TParent, TChild>(
        HierarchicalSearchResult<TParent, TChild> Result,
        IReadOnlyList<TChild> ContextBefore,
        IReadOnlyList<TChild> ContextAfter
    ) where TParent : IHierarchicalDocument<TChild>
      where TChild : ISearchable;
}
```

### Implementation Examples

#### Strategy 1: Parent-Only Indexing

```csharp
// Simple approach - index combined session content
public class SessionDocumentAdapter : IDocument
{
    private readonly Session _session;

    public string GetSearchableText() =>
        // Combine all messages into single searchable text
        string.Join("\n", _session.Messages.Select(m => $"[{m.Role}] {m.Content}"));

    // No GetChildren() needed
}

// Indexing
await searchEngine.IndexAsync(new SessionDocumentAdapter(session));

// Searching - returns sessions
var results = await searchEngine.SearchAsync("authentication error");
// Result contains entire session, don't know which message matched
```

**Pros**: Simplest, fast indexing, smaller index
**Cons**: No message-level detail, can't expand context around matches

---

#### Strategy 2: Children-Only Indexing

```csharp
// Index individual messages, reference parent
public class MessageDocumentAdapter : IChildDocument
{
    private readonly Message _message;
    private readonly string _sessionId;

    public string Id => _message.Id;
    public string GetParentId() => _sessionId;
    public string GetSearchableText() => _message.Content;
}

// Indexing - index each message separately
foreach (var message in session.Messages)
{
    await messageSearchEngine.IndexAsync(
        new MessageDocumentAdapter(message, session.Id));
}

// Searching - returns messages
var messageResults = await messageSearchEngine.SearchAsync("authentication error");

// Group by parent session
var sessionGroups = messageResults
    .GroupBy(r => r.Document.GetParentId())
    .Select(g => new {
        SessionId = g.Key,
        MatchedMessages = g.ToList(),
        Score = g.Max(r => r.Score)
    });

// Retrieve parent sessions
foreach (var group in sessionGroups)
{
    var session = await sessionRepository.GetAsync(group.SessionId);
    // Build result with session + matched messages
}
```

**Pros**: Precise message-level matching, can see which message matched
**Cons**: Need separate parent retrieval, more complex result building

---

#### Strategy 3: Both (Recommended for agent-session-search-tools)

```csharp
// Parent implements hierarchical interface
public class SessionDocumentAdapter : IHierarchicalDocument<MessageDocumentAdapter>
{
    private readonly Session _session;
    private readonly Lazy<IReadOnlyList<MessageDocumentAdapter>> _children;

    public SessionDocumentAdapter(Session session)
    {
        _session = session;
        _children = new Lazy<IReadOnlyList<MessageDocumentAdapter>>(
            () => _session.Messages
                .Select((m, idx) => new MessageDocumentAdapter(m, _session.Id, idx))
                .ToList()
        );
    }

    public string GetSearchableText() =>
        // Combined for parent-level search
        string.Join("\n", _session.Messages.Select(m => $"[{m.Role}] {m.Content}"));

    public IReadOnlyList<MessageDocumentAdapter> GetChildren() => _children.Value;

    public MessageDocumentAdapter? GetChildById(string childId) =>
        _children.Value.FirstOrDefault(c => c.Id == childId);

    public IReadOnlyList<MessageDocumentAdapter> GetChildrenBefore(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocumentAdapter>();

        return _children.Value
            .Where(c => c.Position < child.Position)
            .OrderByDescending(c => c.Position)
            .Take(count)
            .Reverse() // Back to chronological order
            .ToList();
    }

    public IReadOnlyList<MessageDocumentAdapter> GetChildrenAfter(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocumentAdapter>();

        return _children.Value
            .Where(c => c.Position > child.Position)
            .OrderBy(c => c.Position)
            .Take(count)
            .ToList();
    }
}

public class MessageDocumentAdapter : IChildDocument
{
    private readonly Message _message;
    private readonly string _sessionId;
    public int Position { get; }

    public MessageDocumentAdapter(Message message, string sessionId, int position)
    {
        _message = message;
        _sessionId = sessionId;
        Position = position;
    }

    public string Id => _message.Id;
    public string GetParentId() => _sessionId;
    public string GetSearchableText() => _message.Content;
    public DateTime GetTimestamp() => _message.Timestamp;
}

// Indexing - library handles both
await searchEngine.IndexHierarchicalAsync(
    new SessionDocumentAdapter(session),
    strategy: HierarchicalIndexingStrategy.Both);

// Searching with child-level detail
var results = await searchEngine.SearchAsync("authentication error", maxResults: 10);

// Results include matched children
foreach (var result in results)
{
    Console.WriteLine($"Session: {result.Parent.Id}");
    if (result.MatchedChildren != null)
    {
        foreach (var childMatch in result.MatchedChildren)
        {
            Console.WriteLine($"  Message {childMatch.Position}: {childMatch.Highlight}");
            Console.WriteLine($"  Score: {childMatch.ChildScore}");
        }
    }
}

// Context expansion
var expandedResults = await searchEngine.ExpandContextAsync(
    results.First(),
    messagesBefore: 2,
    messagesAfter: 2);

Console.WriteLine("Context before:");
foreach (var msg in expandedResults.ContextBefore)
    Console.WriteLine($"  {msg.GetSearchableText()}");

Console.WriteLine("Matched messages:");
// ... matched messages ...

Console.WriteLine("Context after:");
foreach (var msg in expandedResults.ContextAfter)
    Console.WriteLine($"  {msg.GetSearchableText()}");
```

**Pros**: Best of both worlds, precise matching + full context
**Cons**: Larger index, more complex implementation

---

#### Strategy 4: Parent with Embedded Children (Advanced)

```csharp
// Parent embeds child positions in metadata
public class SessionDocumentAdapter : IDocument
{
    public IDictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            // Standard fields
            ["AgentType"] = _session.AgentType,

            // Embedded child positions (for small hierarchies)
            ["MessageIds"] = string.Join(",", _session.Messages.Select(m => m.Id)),
            ["MessagePositions"] = _session.Messages
                .Select((m, idx) => new { Id = m.Id, Start = idx * 1000, Length = m.Content.Length })
                .ToJson(),

            // Store individual messages in separate fields
            ["Message_0"] = _session.Messages[0].Content,
            ["Message_1"] = _session.Messages[1].Content,
            // ... up to N messages
        };
    }
}

// Field mapper creates per-message fields
public class SessionFieldMapper : IDocumentFieldMapper<SessionDocumentAdapter>
{
    public IEnumerable<IIndexableField> MapToFields(SessionDocumentAdapter doc)
    {
        // ... standard fields ...

        // Create separate field for each message (up to limit)
        var messages = doc.UnderlyingSession.Messages.Take(100); // Limit
        foreach (var (message, index) in messages.Select((m, i) => (m, i)))
        {
            yield return new TextField($"Message_{index}",
                message.Content,
                Field.Store.YES)
            {
                Boost = message.Role == MessageRole.User ? 1.2f : 1.0f
            };
        }
    }
}
```

**Pros**: Single document, preserves positions, can boost per-child
**Cons**: Limited to ~100 children (Lucene field limits), complex field mapping

---

### Indexing Engine Support

```csharp
// File: ContentIndexing/Search/ISearchEngine.cs

public interface ISearchEngine<TDocument> where TDocument : ISearchable
{
    // Existing methods
    Task IndexAsync(TDocument document, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(...);

    // New hierarchical methods
    Task IndexHierarchicalAsync<TChild>(
        IHierarchicalDocument<TChild> document,
        HierarchicalIndexingStrategy strategy = HierarchicalIndexingStrategy.Both,
        CancellationToken ct = default)
        where TChild : ISearchable;

    Task<IReadOnlyList<HierarchicalSearchResult<TParent, TChild>>> SearchHierarchicalAsync<TParent, TChild>(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken ct = default)
        where TParent : IHierarchicalDocument<TChild>
        where TChild : ISearchable;

    Task<ContextExpandedResult<TParent, TChild>> ExpandContextAsync<TParent, TChild>(
        HierarchicalSearchResult<TParent, TChild> result,
        int childrenBefore = 0,
        int childrenAfter = 0,
        CancellationToken ct = default)
        where TParent : IHierarchicalDocument<TChild>
        where TChild : ISearchable;
}
```

### Use Case Matrix

| Use Case | Strategy | Rationale |
|----------|----------|-----------|
| **Agent Sessions** (10-100 messages) | Both | Need both session-level and message-level search, context expansion critical |
| **RAG Chunks** (100-1000 chunks per doc) | Children-Only | Only need chunk matches, parent just metadata |
| **Email Threads** (5-20 emails) | Both or ParentWithEmbedded | Small enough for embedded, need context |
| **Forum Threads** (100-1000 posts) | Children-Only | Too many children for parent-level indexing |
| **Documentation Sections** (10-50 sections) | ParentWithEmbedded | Structured, fixed sections, need boosting |
| **Chat History** (1000+ messages) | Children-Only with windowing | Too large for parent-level, use time-based chunks |

---

## Integration Details: agent-session-search-tools

### Complete Integration Example

Here's how agent-session-search-tools would integrate using generic abstractions:

#### 1. Document Adapter Layer

```csharp
// File: Adapters/SessionDocumentAdapter.cs

using ContentIndexing.Models;
using AgentJournal.Core.Models;

namespace AgentJournal.Adapters
{
    /// <summary>
    /// Adapts Session model to IDocument for search indexing.
    /// Implements hierarchical document pattern (session contains messages).
    /// </summary>
    public class SessionDocumentAdapter : IDocument, IHierarchicalDocument<MessageDocumentAdapter>
    {
        private readonly Session _session;
        private readonly Lazy<IReadOnlyList<MessageDocumentAdapter>> _children;

        public SessionDocumentAdapter(Session session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _children = new Lazy<IReadOnlyList<MessageDocumentAdapter>>(
                () => _session.Messages
                    .Select(m => new MessageDocumentAdapter(m, _session.Id))
                    .ToList()
            );
        }

        // ISearchable implementation
        public string Id => _session.Id;

        public string GetSearchableText()
        {
            // Combine all messages for session-level search
            return string.Join("\n\n", _session.Messages.Select(m =>
                $"[{m.Role}] {m.Content}"));
        }

        public DateTime GetTimestamp() => _session.StartedAt;

        // IDocument implementation - custom fields for Lucene
        public IDictionary<string, object> GetMetadata()
        {
            return new Dictionary<string, object>
            {
                // Searchable fields
                ["AgentType"] = _session.AgentType,
                ["ProjectPath"] = _session.ProjectPath ?? "",
                ["GitBranch"] = _session.GitBranch ?? "",

                // Filter fields (not analyzed)
                ["IsActive"] = _session.IsActive,
                ["Duration"] = _session.Duration?.TotalMinutes ?? 0,
                ["MessageCount"] = _session.MessageCount,

                // Sortable fields
                ["EndedAt"] = _session.EndedAt ?? DateTime.MaxValue,

                // Stored fields (for display)
                ["Summary"] = _session.Summary ?? "",
                ["AgentVersion"] = _session.AgentVersion ?? ""
            };
        }

        // IHierarchicalDocument implementation
        public IReadOnlyList<MessageDocumentAdapter> GetChildren() => _children.Value;

        // Expose original session for result building
        public Session UnderlyingSession => _session;
    }

    /// <summary>
    /// Adapts individual Message to IDocument for message-level indexing.
    /// Maintains reference to parent session.
    /// </summary>
    public class MessageDocumentAdapter : IDocument
    {
        private readonly Message _message;
        private readonly string _sessionId;

        public MessageDocumentAdapter(Message message, string sessionId)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _sessionId = sessionId;
        }

        // ISearchable implementation
        public string Id => _message.Id;
        public string GetSearchableText() => _message.Content;
        public DateTime GetTimestamp() => _message.Timestamp;

        // IDocument implementation
        public IDictionary<string, object> GetMetadata()
        {
            return new Dictionary<string, object>
            {
                // Link to parent
                ["SessionId"] = _sessionId,

                // Message-specific fields
                ["Role"] = _message.Role.ToString(),
                ["ParentId"] = _message.ParentId ?? "",
                ["Model"] = _message.Model ?? "",

                // Tool call information
                ["HasToolCalls"] = _message.HasToolCalls,
                ["ToolCallCount"] = _message.ToolCallCount,
                ["ToolNames"] = _message.HasToolCalls
                    ? string.Join(",", _message.ToolCalls!.Select(tc => tc.Name))
                    : "",

                // Content metadata
                ["ContentLength"] = _message.ContentLength,
                ["IsResponse"] = _message.IsResponse
            };
        }

        // Expose original message
        public Message UnderlyingMessage => _message;
    }
}
```

#### 2. Field Mapping Configuration

```csharp
// File: Configuration/SessionFieldMapper.cs

using ContentIndexing.Search.Lucene;
using Lucene.Net.Documents;

namespace AgentJournal.Configuration
{
    /// <summary>
    /// Defines how SessionDocumentAdapter maps to Lucene fields.
    /// Controls field analysis, storage, and boosting.
    /// </summary>
    public class SessionFieldMapper : IDocumentFieldMapper<SessionDocumentAdapter>
    {
        public IEnumerable<IIndexableField> MapToFields(SessionDocumentAdapter document)
        {
            var metadata = document.GetMetadata();

            // Session ID - stored, not analyzed (exact match only)
            yield return new StringField("Id", document.Id, Field.Store.YES);

            // Primary search field - analyzed, stored, boosted
            yield return new TextField("Content", document.GetSearchableText(), Field.Store.YES)
            {
                Boost = 1.0f // Standard boost
            };

            // AgentType - not analyzed (exact match), stored
            yield return new StringField("AgentType",
                metadata["AgentType"].ToString()!,
                Field.Store.YES);

            // ProjectPath - analyzed (for partial match), stored
            yield return new TextField("ProjectPath",
                metadata["ProjectPath"].ToString()!,
                Field.Store.YES);

            // GitBranch - not analyzed, stored
            yield return new StringField("GitBranch",
                metadata["GitBranch"].ToString()!,
                Field.Store.YES);

            // Timestamp - numeric field for range queries and sorting
            yield return new Int64Field("Timestamp",
                document.GetTimestamp().Ticks,
                Field.Store.YES);

            // IsActive - for filtering
            yield return new StringField("IsActive",
                metadata["IsActive"].ToString()!,
                Field.Store.NO); // Don't need to retrieve

            // MessageCount - numeric for range queries
            yield return new Int32Field("MessageCount",
                (int)metadata["MessageCount"],
                Field.Store.YES);

            // Summary - analyzed, stored, lower boost (less important than content)
            if (!string.IsNullOrEmpty(metadata["Summary"].ToString()))
            {
                yield return new TextField("Summary",
                    metadata["Summary"].ToString()!,
                    Field.Store.YES)
                {
                    Boost = 0.5f // Less important than main content
                };
            }

            // Combined searchable field (not stored, analyzed, high boost)
            // Used for session-level "search all" queries
            yield return new TextField("AllContent",
                document.GetSearchableText(),
                Field.Store.NO) // Save space, already stored in Content
            {
                Boost = 1.2f // Slightly boost full-session matches
            };
        }

        public string GetPrimaryContentField() => "Content";
        public string GetIdField() => "Id";
        public string GetTimestampField() => "Timestamp";
    }

    /// <summary>
    /// Message-level field mapping for granular search.
    /// </summary>
    public class MessageFieldMapper : IDocumentFieldMapper<MessageDocumentAdapter>
    {
        public IEnumerable<IIndexableField> MapToFields(MessageDocumentAdapter document)
        {
            var metadata = document.GetMetadata();

            yield return new StringField("Id", document.Id, Field.Store.YES);
            yield return new StringField("SessionId",
                metadata["SessionId"].ToString()!,
                Field.Store.YES);

            // Message content - analyzed, stored, boosted based on role
            var boost = metadata["Role"].ToString() == "User" ? 1.2f : 1.0f;
            yield return new TextField("Content",
                document.GetSearchableText(),
                Field.Store.YES)
            {
                Boost = boost // User messages slightly more important
            };

            // Role - not analyzed
            yield return new StringField("Role",
                metadata["Role"].ToString()!,
                Field.Store.YES);

            // Tool calls - searchable by tool name
            if ((bool)metadata["HasToolCalls"])
            {
                yield return new TextField("ToolNames",
                    metadata["ToolNames"].ToString()!,
                    Field.Store.YES);
            }

            // Model - for filtering by AI model used
            if (!string.IsNullOrEmpty(metadata["Model"].ToString()))
            {
                yield return new StringField("Model",
                    metadata["Model"].ToString()!,
                    Field.Store.YES);
            }

            yield return new Int64Field("Timestamp",
                document.GetTimestamp().Ticks,
                Field.Store.YES);
        }

        public string GetPrimaryContentField() => "Content";
        public string GetIdField() => "Id";
        public string GetTimestampField() => "Timestamp";
    }
}
```

#### 3. Dependency Injection Setup

```csharp
// File: Startup.cs or Program.cs

using AgentJournal.Adapters;
using AgentJournal.Configuration;
using ContentIndexing;
using ContentIndexing.Search;
using ContentIndexing.Embeddings;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentJournalSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure search options
        services.Configure<SearchEngineOptions>(options =>
        {
            options.IndexPath = configuration["Search:IndexPath"] ?? "./data/search-index";
            options.DefaultMode = SearchMode.Hybrid;
            options.Precision = VectorPrecision.Float16;
            options.LexicalWeight = 0.5;
            options.SemanticWeight = 0.5;
            options.DecayHalfLifeDays = 90.0;
            options.ApplyDecay = false; // Sessions don't decay by default
        });

        // Register embedding provider
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var modelsPath = configuration["Search:ModelsPath"];
            return EmbeddingProviderFactory.TryCreateAsync(modelsPath).GetAwaiter().GetResult();
        });

        // Register field mappers
        services.AddSingleton<IDocumentFieldMapper<SessionDocumentAdapter>, SessionFieldMapper>();
        services.AddSingleton<IDocumentFieldMapper<MessageDocumentAdapter>, MessageFieldMapper>();

        // Register search engines with specific document types
        services.AddSingleton<ISearchEngine<SessionDocumentAdapter>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SearchEngineOptions>>().Value;
            var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
            var fieldMapper = sp.GetRequiredService<IDocumentFieldMapper<SessionDocumentAdapter>>();
            var logger = sp.GetRequiredService<ILogger<HybridSearcher<SessionDocumentAdapter>>>();

            var lucene = new LuceneSearchEngine<SessionDocumentAdapter>(
                Path.Combine(options.IndexPath, "lucene-sessions"),
                fieldMapper,
                logger);

            var vector = new VectorSearchEngine<SessionDocumentAdapter>(
                Path.Combine(options.IndexPath, "vector-sessions"),
                embeddings,
                options.Precision,
                logger);

            return new HybridSearcher<SessionDocumentAdapter>(
                lucene,
                vector,
                options.LexicalWeight,
                options.SemanticWeight,
                options.RrfK,
                logger);
        });

        // Separate engine for message-level search if needed
        services.AddSingleton<ISearchEngine<MessageDocumentAdapter>>(sp =>
        {
            // Similar setup for messages...
        });

        // Register search service (facade)
        services.AddSingleton<ISessionSearchService, SessionSearchService>();

        return services;
    }
}
```

#### 4. Search Service (Facade Pattern)

```csharp
// File: Services/SessionSearchService.cs

using AgentJournal.Adapters;
using AgentJournal.Core.Models;
using ContentIndexing.Search;

namespace AgentJournal.Services
{
    /// <summary>
    /// High-level search service that works with domain models (Session)
    /// and handles adapter conversion internally.
    /// </summary>
    public interface ISessionSearchService
    {
        Task IndexSessionAsync(Session session, CancellationToken ct = default);
        Task<IReadOnlyList<SessionSearchResult>> SearchSessionsAsync(
            string query,
            int maxResults = 10,
            SearchMode mode = SearchMode.Hybrid,
            SessionSearchFilters? filters = null,
            CancellationToken ct = default);
    }

    public class SessionSearchService : ISessionSearchService
    {
        private readonly ISearchEngine<SessionDocumentAdapter> _searchEngine;
        private readonly ILogger<SessionSearchService> _logger;

        public SessionSearchService(
            ISearchEngine<SessionDocumentAdapter> searchEngine,
            ILogger<SessionSearchService> logger)
        {
            _searchEngine = searchEngine;
            _logger = logger;
        }

        public async Task IndexSessionAsync(Session session, CancellationToken ct = default)
        {
            var adapter = new SessionDocumentAdapter(session);

            // Index session-level document
            await _searchEngine.IndexAsync(adapter, ct);

            // Also index individual messages for message-level search
            // (if using hierarchical indexing)
            foreach (var messageAdapter in adapter.GetChildren())
            {
                // Could index to separate message search engine
                // or include in session index with parent reference
            }

            _logger.LogInformation(
                "Indexed session {SessionId} with {MessageCount} messages",
                session.Id, session.MessageCount);
        }

        public async Task<IReadOnlyList<SessionSearchResult>> SearchSessionsAsync(
            string query,
            int maxResults = 10,
            SearchMode mode = SearchMode.Hybrid,
            SessionSearchFilters? filters = null,
            CancellationToken ct = default)
        {
            // Perform search
            var results = await _searchEngine.SearchAsync(query, maxResults, mode, ct);

            // Convert back to domain model
            var sessionResults = results.Select(r => new SessionSearchResult(
                Session: r.Document.UnderlyingSession,
                Score: r.Score,
                Highlight: r.Highlight,
                DecayFactor: r.DecayFactor,
                MatchedMessages: ExtractMatchedMessages(r)
            )).ToList();

            // Apply filters if provided
            if (filters != null)
            {
                sessionResults = ApplyFilters(sessionResults, filters);
            }

            return sessionResults;
        }

        private IReadOnlyList<Message> ExtractMatchedMessages(
            SearchResult<SessionDocumentAdapter> result)
        {
            // If search engine provided matched children (context expansion)
            if (result.MatchedChildren != null)
            {
                return result.MatchedChildren
                    .OfType<MessageDocumentAdapter>()
                    .Select(m => m.UnderlyingMessage)
                    .ToList();
            }

            // Otherwise return all messages
            return result.Document.UnderlyingSession.Messages;
        }

        private List<SessionSearchResult> ApplyFilters(
            List<SessionSearchResult> results,
            SessionSearchFilters filters)
        {
            var filtered = results.AsEnumerable();

            if (filters.AgentTypes?.Any() == true)
                filtered = filtered.Where(r =>
                    filters.AgentTypes.Contains(r.Session.AgentType));

            if (filters.ProjectPath != null)
                filtered = filtered.Where(r =>
                    r.Session.ProjectPath?.Contains(filters.ProjectPath) == true);

            if (filters.OnlyActive)
                filtered = filtered.Where(r => r.Session.IsActive);

            if (filters.MinMessageCount > 0)
                filtered = filtered.Where(r =>
                    r.Session.MessageCount >= filters.MinMessageCount);

            if (filters.DateRange != null)
                filtered = filtered.Where(r =>
                    r.Session.StartedAt >= filters.DateRange.Start &&
                    r.Session.StartedAt <= filters.DateRange.End);

            return filtered.ToList();
        }
    }

    // Domain-specific search result
    public record SessionSearchResult(
        Session Session,
        double Score,
        string? Highlight,
        double? DecayFactor,
        IReadOnlyList<Message> MatchedMessages);

    // Domain-specific filters
    public record SessionSearchFilters(
        IReadOnlyList<string>? AgentTypes = null,
        string? ProjectPath = null,
        bool OnlyActive = false,
        int MinMessageCount = 0,
        DateTimeRange? DateRange = null);

    public record DateTimeRange(DateTime Start, DateTime End);
}
```

#### 5. Usage Examples

```csharp
// File: Examples/SearchUsageExamples.cs

public class SearchExamples
{
    private readonly ISessionSearchService _searchService;

    // Example 1: Basic hybrid search
    public async Task BasicSearchExample()
    {
        var results = await _searchService.SearchSessionsAsync(
            query: "error handling authentication",
            maxResults: 10,
            mode: SearchMode.Hybrid
        );

        foreach (var result in results)
        {
            Console.WriteLine($"Session: {result.Session.Id}");
            Console.WriteLine($"Score: {result.Score:F3}");
            Console.WriteLine($"Summary: {result.Session.Summary}");
            Console.WriteLine($"Matched messages: {result.MatchedMessages.Count}");
            Console.WriteLine($"Highlight: {result.Highlight}");
            Console.WriteLine("---");
        }
    }

    // Example 2: Filtered search
    public async Task FilteredSearchExample()
    {
        var results = await _searchService.SearchSessionsAsync(
            query: "database migration",
            maxResults: 20,
            mode: SearchMode.Lexical,
            filters: new SessionSearchFilters(
                AgentTypes: new[] { "claude-code", "copilot-cli" },
                ProjectPath: "/home/user/myproject",
                OnlyActive: false,
                MinMessageCount: 5,
                DateRange: new DateTimeRange(
                    Start: DateTime.Now.AddDays(-30),
                    End: DateTime.Now
                )
            )
        );
    }

    // Example 3: Semantic search only
    public async Task SemanticSearchExample()
    {
        var results = await _searchService.SearchSessionsAsync(
            query: "How do I optimize React performance?",
            maxResults: 5,
            mode: SearchMode.Semantic // Pure vector search
        );

        // Semantic search finds conceptually similar sessions
        // even if exact keywords don't match
    }

    // Example 4: Indexing new sessions
    public async Task IndexingExample(Session session)
    {
        await _searchService.IndexSessionAsync(session);

        // Session is now searchable via:
        // - Keyword match on message content
        // - Semantic similarity on embedded content
        // - Metadata filters (agent type, project, etc.)
    }

    // Example 5: Tool call search
    public async Task ToolCallSearchExample()
    {
        // Search for sessions that used specific tools
        var results = await _searchService.SearchSessionsAsync(
            query: "ToolNames:Bash OR ToolNames:Git",
            mode: SearchMode.Lexical // Use Lucene query syntax
        );
    }
}
```

#### 6. Migration Path from Current Implementation

```csharp
// File: Migration/SearchMigrationGuide.cs

/*
BEFORE (current agent-session-search-tools):
========================================

// Direct usage of search engines
var luceneEngine = new LuceneSearchEngine(lucenePath);
var vectorEngine = new VectorSearchEngine(vectorPath, embeddings);
var hybridSearcher = new HybridSearcher(luceneEngine, vectorEngine);

await luceneEngine.IndexSessionAsync(session);
await vectorEngine.IndexSessionAsync(session);

var results = await hybridSearcher.SearchAsync(query, maxResults: 10);


AFTER (with generic library):
==============================

// Register in DI
services.AddAgentJournalSearch(configuration);

// Inject service
public class MyController
{
    private readonly ISessionSearchService _searchService;

    public MyController(ISessionSearchService searchService)
    {
        _searchService = searchService;
    }

    public async Task<IActionResult> Search(string query)
    {
        var results = await _searchService.SearchSessionsAsync(query);
        return Ok(results);
    }
}

// Indexing (same interface, adapted internally)
await _searchService.IndexSessionAsync(session);


KEY DIFFERENCES:
================

1. Session model stays the same - adapters hide library details
2. Search service facade provides domain-specific API
3. Field mapping is explicit and customizable
4. DI manages lifecycle and configuration
5. Search results unwrap to original Session objects
6. Filters are type-safe domain models, not string queries

BREAKING CHANGES: None if using ISessionSearchService facade
BACKWARD COMPATIBLE: Yes, old code can coexist during migration
*/
```

---

## Multi-Tenancy & Multiple Indexes: Detailed Examples

### Pattern 1: Multiple Tenants (SaaS Application)

```csharp
// File: Services/MultiTenantSearchService.cs

public interface IMultiTenantSearchService
{
    Task<IReadOnlyList<SearchResult<Document>>> SearchAsync(
        string tenantId,
        string query,
        int maxResults = 10);
}

public class MultiTenantSearchService : IMultiTenantSearchService
{
    private readonly ConcurrentDictionary<string, ISearchEngine<Document>> _tenantEngines;
    private readonly IEmbeddingProvider _sharedEmbeddings; // Shared across tenants
    private readonly SearchEngineOptions _baseOptions;
    private readonly ILogger<MultiTenantSearchService> _logger;

    public MultiTenantSearchService(
        IEmbeddingProvider embeddingProvider,
        IOptions<SearchEngineOptions> options,
        ILogger<MultiTenantSearchService> logger)
    {
        _tenantEngines = new ConcurrentDictionary<string, ISearchEngine<Document>>();
        _sharedEmbeddings = embeddingProvider;
        _baseOptions = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult<Document>>> SearchAsync(
        string tenantId,
        string query,
        int maxResults = 10)
    {
        var engine = GetOrCreateEngine(tenantId);
        return await engine.SearchAsync(query, maxResults);
    }

    private ISearchEngine<Document> GetOrCreateEngine(string tenantId)
    {
        return _tenantEngines.GetOrAdd(tenantId, tid =>
        {
            // Each tenant gets isolated index directories
            var lucene = new LuceneSearchEngine<Document>(
                Path.Combine(_baseOptions.IndexPath, tid, "lucene"),
                _fieldMapper,
                _logger);

            var vector = new VectorSearchEngine<Document>(
                Path.Combine(_baseOptions.IndexPath, tid, "vector"),
                _sharedEmbeddings, // Shared embedding provider (singleton)
                _baseOptions.Precision,
                _logger);

            _logger.LogInformation("Created search engine for tenant {TenantId}", tid);

            return new HybridSearcher<Document>(lucene, vector);
        });
    }

    // Cleanup when tenant is deleted
    public async Task DeleteTenantIndexAsync(string tenantId)
    {
        if (_tenantEngines.TryRemove(tenantId, out var engine))
        {
            await engine.ClearIndexAsync();
            var tenantPath = Path.Combine(_baseOptions.IndexPath, tenantId);
            Directory.Delete(tenantPath, recursive: true);
            _logger.LogInformation("Deleted index for tenant {TenantId}", tenantId);
        }
    }
}

// Usage
var results = await _multiTenantSearch.SearchAsync("tenant-123", "query");
```

**Benefits**:
- ✅ Complete isolation between tenants
- ✅ Shared embedding provider (no duplicate model loading)
- ✅ Lazy engine creation (only load active tenants)
- ✅ Easy tenant deletion

---

### Pattern 2: Multiple Purpose-Specific Indexes

```csharp
// File: Program.cs - DI Registration

public void ConfigureServices(IServiceCollection services)
{
    // Shared embedding provider
    services.AddSingleton<IEmbeddingProvider>(sp =>
        EmbeddingProviderFactory.TryCreateAsync("./models").GetAwaiter().GetResult());

    // Named search engines for different purposes
    services.AddSingleton<ISearchEngine<SessionDocument>>(sp =>
    {
        var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
        var logger = sp.GetRequiredService<ILogger<HybridSearcher<SessionDocument>>>();

        return new HybridSearcher<SessionDocument>(
            new LuceneSearchEngine<SessionDocument>("./indexes/sessions/lucene", ...),
            new VectorSearchEngine<SessionDocument>("./indexes/sessions/vector", embeddings, ...),
            logger: logger);
    });

    services.AddSingleton<ISearchEngine<KnowledgeEntry>>(sp =>
    {
        var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
        var logger = sp.GetRequiredService<ILogger<VectorSearchEngine<KnowledgeEntry>>>();

        // Knowledge uses semantic-only search
        return new VectorSearchEngine<KnowledgeEntry>(
            "./indexes/knowledge/vector",
            embeddings,
            VectorPrecision.Float16,
            logger);
    });

    services.AddSingleton<ISearchEngine<DocumentChunk>>(sp =>
    {
        var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
        var logger = sp.GetRequiredService<ILogger<HybridSearcher<DocumentChunk>>>();

        // Documents use hybrid search
        return new HybridSearcher<DocumentChunk>(
            new LuceneSearchEngine<DocumentChunk>("./indexes/docs/lucene", ...),
            new VectorSearchEngine<DocumentChunk>("./indexes/docs/vector", embeddings, ...),
            logger: logger);
    });
}

// Usage - inject specific engine
public class SessionService
{
    private readonly ISearchEngine<SessionDocument> _sessionSearch;

    public SessionService(ISearchEngine<SessionDocument> sessionSearch)
    {
        _sessionSearch = sessionSearch; // Gets the sessions engine
    }

    public async Task<IReadOnlyList<SearchResult<SessionDocument>>> SearchSessions(string query)
    {
        return await _sessionSearch.SearchAsync(query);
    }
}

public class KnowledgeService
{
    private readonly ISearchEngine<KnowledgeEntry> _knowledgeSearch;

    public KnowledgeService(ISearchEngine<KnowledgeEntry> knowledgeSearch)
    {
        _knowledgeSearch = knowledgeSearch; // Gets the knowledge engine
    }
}
```

**Benefits**:
- ✅ Type-safe injection - no runtime casting
- ✅ Different search strategies per document type
- ✅ Clear separation of concerns
- ✅ Single embedding provider shared across all

---

### Pattern 3: Project-Scoped Indexes (localagent use case)

```csharp
// File: Services/ProjectSearchService.cs

public interface IProjectSearchService
{
    Task IndexProjectAsync(string projectPath, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult<DocumentChunk>>> SearchProjectAsync(
        string projectPath,
        string query,
        int maxResults = 10);
    Task<IReadOnlyList<string>> ListIndexedProjectsAsync();
}

public class ProjectSearchService : IProjectSearchService
{
    private readonly ConcurrentDictionary<string, ISearchEngine<DocumentChunk>> _projectEngines;
    private readonly IEmbeddingProvider _embeddings;
    private readonly string _indexBasePath;

    public ProjectSearchService(IEmbeddingProvider embeddings, IConfiguration config)
    {
        _projectEngines = new ConcurrentDictionary<string, ISearchEngine<DocumentChunk>>();
        _embeddings = embeddings;
        _indexBasePath = config["IndexBasePath"] ?? "./indexes";
    }

    public async Task IndexProjectAsync(string projectPath, CancellationToken ct = default)
    {
        var engine = GetOrCreateEngineForProject(projectPath);

        // Chunk and index all files in project
        var files = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var chunks = await ChunkFileAsync(file);
            foreach (var chunk in chunks)
            {
                await engine.IndexAsync(chunk, ct);
            }
        }
    }

    public async Task<IReadOnlyList<SearchResult<DocumentChunk>>> SearchProjectAsync(
        string projectPath,
        string query,
        int maxResults = 10)
    {
        var engine = GetOrCreateEngineForProject(projectPath);
        return await engine.SearchAsync(query, maxResults, SearchMode.Semantic);
    }

    private ISearchEngine<DocumentChunk> GetOrCreateEngineForProject(string projectPath)
    {
        // Use project path hash as index key
        var projectHash = ComputeHash(projectPath);

        return _projectEngines.GetOrAdd(projectHash, _ =>
        {
            return new VectorSearchEngine<DocumentChunk>(
                Path.Combine(_indexBasePath, projectHash),
                _embeddings,
                VectorPrecision.Float16);
        });
    }

    public Task<IReadOnlyList<string>> ListIndexedProjectsAsync()
    {
        var projects = Directory.GetDirectories(_indexBasePath)
            .Select(d => new DirectoryInfo(d).Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(projects);
    }

    private string ComputeHash(string path)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16]; // First 16 chars
    }
}

// Usage
await _projectSearch.IndexProjectAsync("/home/user/myproject");
var results = await _projectSearch.SearchProjectAsync("/home/user/myproject", "authentication");
```

**Benefits**:
- ✅ Isolated index per project
- ✅ No cross-project contamination
- ✅ Easy to delete project index
- ✅ Works with any number of projects

---

### Pattern 4: Named Engine Factory

```csharp
// File: Services/SearchEngineFactory.cs

public interface ISearchEngineFactory
{
    ISearchEngine<TDocument> Create<TDocument>(
        string indexName,
        SearchEngineOptions? options = null)
        where TDocument : ISearchable;
}

public class SearchEngineFactory : ISearchEngineFactory
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly SearchEngineOptions _defaultOptions;
    private readonly ILoggerFactory _loggerFactory;

    public SearchEngineFactory(
        IEmbeddingProvider embeddings,
        IOptions<SearchEngineOptions> defaultOptions,
        ILoggerFactory loggerFactory)
    {
        _embeddings = embeddings;
        _defaultOptions = defaultOptions.Value;
        _loggerFactory = loggerFactory;
    }

    public ISearchEngine<TDocument> Create<TDocument>(
        string indexName,
        SearchEngineOptions? options = null)
        where TDocument : ISearchable
    {
        var opts = options ?? _defaultOptions;
        var indexPath = Path.Combine(opts.IndexPath, indexName);

        var logger = _loggerFactory.CreateLogger<HybridSearcher<TDocument>>();

        return new HybridSearcher<TDocument>(
            new LuceneSearchEngine<TDocument>(
                Path.Combine(indexPath, "lucene"),
                /* field mapper */,
                logger),
            new VectorSearchEngine<TDocument>(
                Path.Combine(indexPath, "vector"),
                _embeddings,
                opts.Precision,
                logger),
            opts.LexicalWeight,
            opts.SemanticWeight,
            opts.RrfK,
            logger);
    }
}

// Usage - on-demand engine creation
var agentEngine = _factory.Create<SessionDocument>("agent-sessions");
var knowledgeEngine = _factory.Create<KnowledgeEntry>("knowledge-base");
var docsEngine = _factory.Create<DocumentChunk>("documentation");

// Temporary index for one-off task
var tempEngine = _factory.Create<DocumentChunk>(
    "temp-" + Guid.NewGuid(),
    new SearchEngineOptions { Precision = VectorPrecision.Float32 });

// Use it
await tempEngine.IndexAsync(document);
var results = await tempEngine.SearchAsync(query);

// Clean up
await tempEngine.ClearIndexAsync();
```

**Benefits**:
- ✅ Create engines on-demand
- ✅ Override options per engine
- ✅ Easy to create temporary indexes
- ✅ Factory handles all wiring

---

### Resource Sharing Best Practices

```csharp
// ✅ DO: Share embedding provider (singleton)
services.AddSingleton<IEmbeddingProvider>(sp => /* ... */);

// ✅ DO: Create multiple engine instances
var engine1 = new VectorSearchEngine<Doc>("./index1", sharedEmbeddings);
var engine2 = new VectorSearchEngine<Doc>("./index2", sharedEmbeddings);

// ✅ DO: Use different index paths
var sessionEngine = new HybridSearcher<Session>("./indexes/sessions", ...);
var knowledgeEngine = new VectorSearchEngine<Knowledge>("./indexes/knowledge", ...);

// ❌ DON'T: Share engine instances across tenants
// (creates security risk - tenant A could see tenant B's data)

// ❌ DON'T: Create multiple embedding providers
// (wastes memory - ONNX model is ~100MB loaded)

// ✅ DO: Dispose engines when done (if long-lived app)
await using var tempEngine = factory.Create<Doc>("temp");
// ... use engine ...
// Disposed automatically at end of scope
```

### Directory Structure Example

```
indexes/
├── tenant-123/
│   ├── lucene/
│   │   ├── segments_1
│   │   └── _0.cfs
│   └── vector/
│       └── index.ajvi
├── tenant-456/
│   ├── lucene/
│   └── vector/
├── sessions/
│   ├── lucene/
│   └── vector/
├── knowledge/
│   └── vector/              # Knowledge uses vector-only
└── documents/
    ├── lucene/
    └── vector/
```

**Isolation**:
- Each directory is completely independent
- No shared files between indexes
- Safe to delete entire directory to remove index
- Can move directories to different drives for load balancing

### Key Integration Points

1. **GetMetadata() Usage**:
   - Each key becomes a Lucene field name
   - Field mapper controls analysis/storage
   - Values can be strings, numbers, bools, dates
   - Metadata is used for filtering and faceting

2. **Hierarchical Documents**:
   - `IHierarchicalDocument<TChild>` represents parent-child relationships
   - Sessions contain Messages
   - Search can return session with matched messages
   - Context expansion works via child navigation

3. **Type Safety**:
   - `ISearchEngine<SessionDocumentAdapter>` ensures compile-time safety
   - No casting or reflection at runtime
   - Domain models stay clean (Session, Message)
   - Adapters are thin wrappers

4. **Customization**:
   - Field mappers control Lucene indexing
   - Metadata dictionary allows arbitrary fields
   - Filters are domain-specific (not generic)
   - Search service encapsulates complexity

