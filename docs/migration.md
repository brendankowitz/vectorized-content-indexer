# Migration Guide

This guide helps projects migrate to ZeroProximity.VectorizedContentIndexer, particularly for users of agent-session-search-tools who want to adopt the generic library.

## Table of Contents

1. [Overview](#overview)
2. [Migrating from agent-session-search-tools](#migrating-from-agent-session-search-tools)
3. [Wrapping Existing Domain Models](#wrapping-existing-domain-models)
4. [Backward Compatibility](#backward-compatibility)
5. [Migration Checklist](#migration-checklist)

## Overview

ZeroProximity.VectorizedContentIndexer is a generic extraction of production-tested search components from agent-session-search-tools. The migration path is designed to be:

- **Non-breaking**: Existing domain models stay unchanged
- **Gradual**: Migrate one component at a time
- **Adapter-based**: Thin wrappers hide implementation details

## Migrating from agent-session-search-tools

### Before: Direct Engine Usage

```csharp
// agent-session-search-tools (before)
using AgentJournal.Core.Search;
using AgentJournal.Core.Embeddings;
using AgentJournal.Core.Models;

// Search engines tightly coupled to Session/Message models
var luceneEngine = new LuceneSearchEngine(lucenePath);
var vectorEngine = new VectorSearchEngine(vectorPath, embeddings);
var hybridSearcher = new HybridSearcher(luceneEngine, vectorEngine);

// Index sessions directly
await luceneEngine.IndexSessionAsync(session);
await vectorEngine.IndexSessionAsync(session);

// Search returns Session objects
var results = await hybridSearcher.SearchAsync(query, maxResults: 10);
foreach (var result in results)
{
    Session session = result.Session;  // Domain model
    // Use session...
}
```

### After: Generic Library with Adapters

```csharp
// ZeroProximity.VectorizedContentIndexer (after)
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Models;

// 1. Create adapter (thin wrapper around Session)
public class SessionDocument : IDocument
{
    private readonly Session _session;

    public SessionDocument(Session session) => _session = session;

    public string Id => _session.Id;
    public string GetSearchableText() =>
        string.Join("\n", _session.Messages.Select(m => m.Content));
    public DateTime GetTimestamp() => _session.StartedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["AgentType"] = _session.AgentType,
        ["ProjectPath"] = _session.ProjectPath,
        ["MessageCount"] = _session.MessageCount
    };

    public Session UnderlyingSession => _session;  // Easy unwrapping
}

// 2. Create generic search engines
var luceneEngine = new LuceneSearchEngine<SessionDocument>(lucenePath);
var vectorEngine = new VectorSearchEngine<SessionDocument>(vectorPath, embeddings);
var hybridSearcher = new HybridSearcher<SessionDocument>(luceneEngine, vectorEngine);

// 3. Index via adapter
await hybridSearcher.IndexAsync(new SessionDocument(session));

// 4. Search returns SessionDocument (unwrap to get Session)
var results = await hybridSearcher.SearchAsync(query, maxResults: 10);
foreach (var result in results)
{
    Session session = result.Document.UnderlyingSession;  // Unwrap
    // Use session...
}
```

### Key Differences

| Aspect | Before (agent-session-search-tools) | After (VectorizedContentIndexer) |
|--------|-----------------------------------|----------------------------------|
| **Coupling** | Tightly coupled to Session model | Generic, works with any model |
| **Indexing** | `IndexSessionAsync(Session)` | `IndexAsync(ISearchable)` |
| **Results** | `SearchResult.Session` | `SearchResult<T>.Document` |
| **Field Mapping** | Hardcoded Session fields | Customizable via `IDocument.GetMetadata()` |
| **Reusability** | Only works with Sessions | Works with any content type |

## Wrapping Existing Domain Models

### Example 1: Session → SessionDocument

```csharp
// Original domain model (UNCHANGED)
public class Session
{
    public string Id { get; set; }
    public string AgentType { get; set; }
    public DateTime StartedAt { get; set; }
    public List<Message> Messages { get; set; } = new();
    // ... other properties ...
}

// Adapter (NEW)
public class SessionDocument : IDocument
{
    private readonly Session _session;

    public SessionDocument(Session session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    // ISearchable implementation
    public string Id => _session.Id;

    public string GetSearchableText()
    {
        return string.Join("\n\n", _session.Messages.Select(m =>
            $"[{m.Role.ToUpperInvariant()}] {m.Content}"));
    }

    public DateTime GetTimestamp() => _session.StartedAt;

    // IDocument implementation
    public IDictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["AgentType"] = _session.AgentType,
            ["ProjectPath"] = _session.ProjectPath ?? "",
            ["GitBranch"] = _session.GitBranch ?? "",
            ["MessageCount"] = _session.Messages.Count,
            ["Duration"] = _session.Duration?.TotalMinutes ?? 0,
            ["IsActive"] = _session.IsActive
        };
    }

    // Unwrapping
    public Session UnderlyingSession => _session;
}
```

### Example 2: Service Facade Pattern

Create a service layer to hide adapter details from application code:

```csharp
// Service interface (UNCHANGED API)
public interface ISessionSearchService
{
    Task IndexSessionAsync(Session session, CancellationToken ct = default);
    Task<IReadOnlyList<SessionSearchResult>> SearchSessionsAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}

// Implementation using generic library
public class SessionSearchService : ISessionSearchService
{
    private readonly ISearchEngine<SessionDocument> _searchEngine;

    public SessionSearchService(ISearchEngine<SessionDocument> searchEngine)
    {
        _searchEngine = searchEngine;
    }

    public async Task IndexSessionAsync(Session session, CancellationToken ct = default)
    {
        // Wrap in adapter internally
        var document = new SessionDocument(session);
        await _searchEngine.IndexAsync(document, ct);
    }

    public async Task<IReadOnlyList<SessionSearchResult>> SearchSessionsAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        // Search
        var results = await _searchEngine.SearchAsync(query, maxResults, ct);

        // Unwrap and convert to domain result type
        return results.Select(r => new SessionSearchResult
        {
            Session = r.Document.UnderlyingSession,  // Unwrap
            Score = r.Score,
            Highlight = r.Highlight,
            DecayFactor = r.DecayFactor
        }).ToList();
    }
}

// Domain result type
public class SessionSearchResult
{
    public required Session Session { get; init; }
    public required double Score { get; init; }
    public string? Highlight { get; init; }
    public double? DecayFactor { get; init; }
}
```

### Example 3: Dependency Injection Setup

```csharp
// Before (agent-session-search-tools)
services.AddSingleton<IEmbeddingProvider>(sp => /* ... */);
services.AddSingleton<ILuceneSearchEngine, LuceneSearchEngine>();
services.AddSingleton<IVectorSearchEngine, VectorSearchEngine>();
services.AddSingleton<IHybridSearcher, HybridSearcher>();

// After (VectorizedContentIndexer)
services.AddSingleton<IEmbeddingProvider>(sp =>
    EmbeddingProviderFactory.TryCreateAsync().GetAwaiter().GetResult());

services.AddSingleton<ISearchEngine<SessionDocument>>(sp =>
{
    var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
    var logger = sp.GetRequiredService<ILogger<HybridSearcher<SessionDocument>>>();

    var lucene = new LuceneSearchEngine<SessionDocument>(
        "./data/lucene",
        mapper: new SessionFieldMapper(),
        logger: logger
    );

    var vector = new VectorSearchEngine<SessionDocument>(
        "./data/vector",
        embeddings,
        VectorPrecision.Float16,
        logger
    );

    return new HybridSearcher<SessionDocument>(
        lucene,
        vector,
        lexicalWeight: 0.5,
        semanticWeight: 0.5,
        logger: logger
    );
});

services.AddSingleton<ISessionSearchService, SessionSearchService>();
```

## Backward Compatibility

### Maintaining Existing API

```csharp
// Old API (keep for backward compatibility)
public class LegacySessionSearchService
{
    private readonly ISessionSearchService _newService;

    public LegacySessionSearchService(ISessionSearchService newService)
    {
        _newService = newService;
    }

    // Old method signature (unchanged)
    public async Task<List<SessionSearchResult>> SearchAsync(
        string query,
        SessionSearchFilters? filters = null)
    {
        var results = await _newService.SearchSessionsAsync(query, maxResults: 20);

        // Apply filters (if any)
        if (filters != null)
        {
            results = ApplyFilters(results, filters);
        }

        return results.ToList();
    }

    private IReadOnlyList<SessionSearchResult> ApplyFilters(
        IReadOnlyList<SessionSearchResult> results,
        SessionSearchFilters filters)
    {
        var filtered = results.AsEnumerable();

        if (filters.AgentTypes?.Any() == true)
            filtered = filtered.Where(r =>
                filters.AgentTypes.Contains(r.Session.AgentType));

        if (filters.ProjectPath != null)
            filtered = filtered.Where(r =>
                r.Session.ProjectPath?.Contains(filters.ProjectPath) == true);

        return filtered.ToList();
    }
}
```

### Index Compatibility

The AJVI binary format is backward compatible:

```csharp
// Old indexes can be read by new library
// No migration needed for index files

// Just point to existing index path
var vectorEngine = new VectorSearchEngine<SessionDocument>(
    indexPath: "./existing-agent-journal-index/vector",  // Old index
    embedder: embeddings
);

// Existing vectors are read correctly
var results = await vectorEngine.SearchAsync("query");
```

**Note:** If the old index used a different document structure, you may need to re-index for optimal results with the new field mapping.

## Migration Checklist

### Phase 1: Preparation

- [ ] Review existing search usage
- [ ] Identify all places where sessions are indexed
- [ ] Identify all places where sessions are searched
- [ ] Document current field mapping and boosting
- [ ] Backup existing indexes

### Phase 2: Create Adapters

- [ ] Create `SessionDocument` implementing `IDocument`
- [ ] Create `MessageDocument` implementing `IDocument` (if needed)
- [ ] Implement `GetMetadata()` with all searchable fields
- [ ] Add unwrapping properties (`UnderlyingSession`)
- [ ] Create custom `ILuceneDocumentMapper` if needed

### Phase 3: Create Service Layer

- [ ] Create `ISessionSearchService` interface
- [ ] Implement `SessionSearchService` using generic library
- [ ] Maintain existing method signatures
- [ ] Add adapter wrapping/unwrapping internally
- [ ] Convert result types

### Phase 4: Configure DI

- [ ] Register `IEmbeddingProvider` as singleton
- [ ] Register `ISearchEngine<SessionDocument>`
- [ ] Register `ISessionSearchService`
- [ ] Configure `SearchEngineOptions`
- [ ] Update index paths if needed

### Phase 5: Test

- [ ] Unit test adapters (wrap/unwrap)
- [ ] Integration test indexing
- [ ] Integration test searching
- [ ] Verify field mapping
- [ ] Verify result ranking
- [ ] Performance test (compare to old implementation)

### Phase 6: Deploy

- [ ] Deploy to staging environment
- [ ] Monitor search quality
- [ ] Monitor performance metrics
- [ ] Run A/B test (old vs new)
- [ ] Deploy to production

### Phase 7: Cleanup (Optional)

- [ ] Remove old search engine dependencies
- [ ] Archive old code
- [ ] Update documentation
- [ ] Re-index with optimized field mapping

## Common Issues

### Issue 1: Missing Metadata Fields

**Problem:** Search results missing fields that were available before.

**Solution:** Ensure `GetMetadata()` includes all necessary fields:

```csharp
public IDictionary<string, object> GetMetadata()
{
    return new Dictionary<string, object>
    {
        // ✅ Include all fields from old implementation
        ["AgentType"] = _session.AgentType,
        ["ProjectPath"] = _session.ProjectPath ?? "",
        ["GitBranch"] = _session.GitBranch ?? "",
        // ... all other fields
    };
}
```

### Issue 2: Different Search Results

**Problem:** Search returns different results than before.

**Possible Causes:**
1. Different field mapping or boosting
2. Different RRF weights
3. Different decay settings

**Solution:** Match configuration to old implementation:

```csharp
// Match old RRF settings
var hybridSearcher = new HybridSearcher<SessionDocument>(
    luceneEngine,
    vectorEngine,
    lexicalWeight: 0.5,  // Same as old
    semanticWeight: 0.5,  // Same as old
    rrfK: 60  // Default
);

// Match old field boosting in custom mapper
doc.Add(new TextField("Title", title, Field.Store.YES)
{
    Boost = 2.0f  // Same as old implementation
});
```

### Issue 3: Performance Regression

**Problem:** Search slower than before.

**Possible Causes:**
1. Not using GPU acceleration
2. Storing too many fields
3. Not optimizing index

**Solution:** See [Performance Tuning](advanced/performance-tuning.md)

## Migration Examples by Project Type

### RAG System

```csharp
// Minimal migration - documents are already simple
public record DocumentChunk : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => CreatedAt;
}

// No service layer needed - use engine directly
var vectorEngine = new VectorSearchEngine<DocumentChunk>(
    "./index/vector",
    embeddings,
    VectorPrecision.Float16
);

await vectorEngine.IndexAsync(chunk);
var results = await vectorEngine.SearchAsync(query, mode: SearchMode.Semantic);
```

### Knowledge Base

```csharp
// Add metadata for filtering
public class KnowledgeArticle : IDocument
{
    private readonly Article _article;

    public string GetSearchableText() => $"{_article.Title}\n{_article.Body}";

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["Category"] = _article.Category,
        ["Tags"] = string.Join(",", _article.Tags),
        ["Author"] = _article.Author,
        ["Rating"] = _article.Rating
    };
}

// Use hybrid search
var hybridSearcher = new HybridSearcher<KnowledgeArticle>(...);
```

### E-commerce

```csharp
// Rich metadata for faceting
public class ProductDocument : IDocument
{
    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["Brand"] = _product.Brand,
        ["Category"] = _product.Category,
        ["Price"] = _product.Price,
        ["InStock"] = _product.StockQuantity > 0,
        ["Rating"] = _product.AverageRating
    };
}

// Custom Lucene mapper for field control
public class ProductFieldMapper : ILuceneDocumentMapper<ProductDocument>
{
    // ... custom field mapping with boosting ...
}
```

## See Also

- [Getting Started](getting-started.md) - Basic library usage
- [API Documentation](api/README.md) - Complete API reference
- [Hierarchical Documents](advanced/hierarchical-documents.md) - Parent-child relationships
- [Custom Field Mapping](advanced/custom-field-mapping.md) - Advanced Lucene configuration
- [AgentSessionExample](../samples/AgentSessionExample/) - Complete migration example
