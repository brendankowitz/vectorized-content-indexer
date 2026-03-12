# Agent Session Example

This sample demonstrates how to use **ZeroProximity.VectorizedContentIndexer** for searching through agent conversation history using hybrid search (combining lexical and semantic search).

## Use Case

When building AI agents or assistants, you often want to:
- Search past conversations for relevant context
- Find similar problems the user has encountered before
- Retrieve specific commands or code snippets mentioned in history
- Build "memory" for conversational AI

This sample shows how to index and search conversation sessions efficiently.

## What This Sample Demonstrates

- **Hybrid search** - Combining BM25 keyword search with vector similarity
- **IDocument implementation** - Exposing metadata for filtering
- **Custom Lucene field mapper** - Controlling how documents are indexed
- **Hierarchical documents** - Sessions containing Messages
- **Context expansion** - Retrieving messages before/after a match
- **Search mode comparison** - Lexical vs Semantic vs Hybrid

## Running the Sample

```bash
# From the repository root
dotnet run --project samples/AgentSessionExample

# Or from the sample directory
cd samples/AgentSessionExample
dotnet run
```

## Key Code Patterns

### 1. Implementing IDocument with Metadata

```csharp
public sealed class SessionDocument : IHierarchicalDocument<Message>
{
    private readonly Session _session;

    public string Id => _session.Id;

    public string GetSearchableText() =>
        string.Join("\n\n", _session.Messages.Select(m =>
            $"[{m.Role.ToUpperInvariant()}]: {m.Content}"));

    public DateTime GetTimestamp() => _session.StartedAt;

    // Metadata for filtering and faceting
    public IDictionary<string, object> GetMetadata() =>
        new Dictionary<string, object>
        {
            ["AgentType"] = _session.AgentType,
            ["ProjectPath"] = _session.ProjectPath,
            ["IsActive"] = _session.IsActive,
            ["MessageCount"] = _session.MessageCount
        };
}
```

### 2. Creating a Custom Lucene Document Mapper

```csharp
public sealed class SessionDocumentMapper : ILuceneDocumentMapper<SessionDocument>
{
    public string IdField => "Id";
    public string ContentField => "Content";
    public string TimestampField => "Timestamp";

    public Document MapToLuceneDocument(SessionDocument document)
    {
        return new Document
        {
            new StringField("Id", document.Id, Field.Store.YES),
            new TextField("Content", document.GetSearchableText(), Field.Store.YES),
            new StringField("AgentType", document.Session.AgentType, Field.Store.YES),
            // ... more fields
        };
    }

    public SessionDocument MapFromLuceneDocument(Document luceneDoc)
    {
        // Reconstruct from stored fields
    }
}
```

### 3. Setting Up Hybrid Search

```csharp
// Create Lucene engine for keyword search
var luceneEngine = new LuceneSearchEngine<SessionDocument>(
    lucenePath,
    new SessionDocumentMapper());

// Create vector engine for semantic search
var vectorEngine = new VectorSearchEngine<SessionDocument>(
    vectorPath,
    embeddings);

// Combine with hybrid searcher
var hybridSearcher = new HybridSearcher<SessionDocument>(
    luceneEngine,
    vectorEngine,
    lexicalWeight: 0.5f,    // Equal weight for both
    semanticWeight: 0.5f,
    rrfK: 60);              // RRF fusion constant

await hybridSearcher.InitializeAsync();
```

### 4. Searching with Different Modes

```csharp
// Pure keyword search (BM25)
var lexical = await hybridSearcher.SearchAsync(query, mode: SearchMode.Lexical);

// Pure semantic search (vector similarity)
var semantic = await hybridSearcher.SearchAsync(query, mode: SearchMode.Semantic);

// Hybrid search (RRF fusion of both)
var hybrid = await hybridSearcher.SearchAsync(query, mode: SearchMode.Hybrid);

// Hybrid with scoring breakdown
var detailed = await hybridSearcher.SearchWithBreakdownAsync(query);
```

### 5. Context Expansion for Hierarchical Documents

```csharp
// IHierarchicalDocument provides methods for context
var document = new SessionDocument(session);

// Get messages around a specific match
var before = document.GetChildrenBefore(messageId, count: 3);
var after = document.GetChildrenAfter(messageId, count: 3);

// Or use the convenience method
var (before, match, after) = document.GetContextWindow(messageId, before: 3, after: 3);
```

## Hybrid Search Explained

The `HybridSearcher` uses Reciprocal Rank Fusion (RRF) to combine results:

```
RRF_score = sum(weight_i / (k + rank_i))
```

Where:
- `k` is typically 60 (controls how quickly scores decay with rank)
- Lexical and semantic results are ranked independently
- Documents appearing in both get boosted

Benefits:
- Finds exact keyword matches (lexical)
- Finds conceptually similar content (semantic)
- Robust to different score distributions
- No need for score normalization

## Sample Data

The sample includes 7 conversation sessions covering:
- Authentication debugging
- JSON parsing
- Async/await patterns
- File handling
- Database queries
- HTTP client usage
- Retry policies

## Production Considerations

1. **Index persistence** - Use persistent paths instead of temp directories
2. **Document caching** - Cache full Session objects for complete retrieval
3. **Incremental indexing** - Add new sessions without reindexing everything
4. **Metadata filtering** - Filter by AgentType or ProjectPath before search
5. **ONNX models** - Use real embedding models for semantic search quality

## Related Samples

- **RagExample** - Demonstrates pure vector search for RAG scenarios
