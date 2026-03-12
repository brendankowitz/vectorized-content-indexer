# RAG Example

This sample demonstrates how to use **ZeroProximity.VectorizedContentIndexer** for Retrieval-Augmented Generation (RAG) scenarios.

## What is RAG?

RAG (Retrieval-Augmented Generation) is a technique that enhances LLM responses by:
1. Retrieving relevant documents from a knowledge base
2. Providing those documents as context to the LLM
3. Having the LLM generate responses grounded in the retrieved information

This approach helps reduce hallucinations and keeps LLM responses factually accurate.

## What This Sample Demonstrates

- **Pure vector (semantic) search** - Finding conceptually similar content
- **Document chunking** - Splitting documents into searchable pieces
- **Simple ISearchable implementation** - The minimal interface for indexable content
- **Embedding provider initialization** - Setting up the embedding generator
- **Top-K retrieval** - Getting the most relevant chunks for RAG context
- **LLM prompt construction** - Building prompts with retrieved context

## Running the Sample

```bash
# From the repository root
dotnet run --project samples/RagExample

# Or from the sample directory
cd samples/RagExample
dotnet run
```

## Key Code Patterns

### 1. Implementing ISearchable

```csharp
public sealed record DocumentChunk : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string SourceDocument { get; init; }
    public DateTime IndexedAt { get; init; } = DateTime.UtcNow;

    // ISearchable implementation
    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => IndexedAt;
}
```

### 2. Creating a Vector Search Engine

```csharp
// Initialize embedding provider (uses hash-based fallback if ONNX unavailable)
var embeddings = await EmbeddingProviderFactory.CreateAsync();

// Create vector search engine with Float16 precision
await using var searchEngine = new VectorSearchEngine<DocumentChunk>(
    indexPath: "./data/rag-index",
    embedder: embeddings,
    precision: VectorPrecision.Float16);

// Initialize (creates or opens the index)
await searchEngine.InitializeAsync();
```

### 3. Indexing Document Chunks

```csharp
// Index a single chunk
await searchEngine.IndexAsync(chunk);

// Index multiple chunks (more efficient)
await searchEngine.IndexManyAsync(chunks);
```

### 4. Searching for RAG Context

```csharp
// Search for relevant chunks
var results = await searchEngine.SearchAsync(
    query: "How do I optimize async performance?",
    maxResults: 5,
    mode: SearchMode.Semantic);

// Build context from results
var context = string.Join("\n\n", results.Select(r => r.Document.Content));
```

### 5. Building an LLM Prompt

```csharp
var prompt = $"""
    Answer the user's question based on the provided context.

    ## Context
    {context}

    ## Question
    {userQuery}

    ## Answer
    """;

// Send to your LLM API (OpenAI, Anthropic, local model, etc.)
```

## Chunking Strategies

This sample uses paragraph-based chunking. In production, consider:

- **Fixed-size chunks** with overlap (e.g., 500 chars with 50 char overlap)
- **Semantic chunking** based on sentence boundaries
- **Recursive splitting** (try paragraphs, then sentences, then fixed-size)
- **Document-type specific** chunking (code files, markdown, etc.)

## Sample Documents

The sample includes three knowledge base documents:
- `csharp-best-practices.txt` - C# coding guidelines
- `dotnet-performance.txt` - Performance optimization tips
- `async-programming.txt` - Async/await patterns

## Production Considerations

1. **Use ONNX models** - The hash-based fallback is for development only. For semantic search, use the ONNX embedding provider with MiniLM or similar models.

2. **Persistent storage** - This sample uses a temp directory. In production, use a persistent path for your index.

3. **Chunking quality** - Invest in good chunking. Poor chunks = poor retrieval = poor LLM responses.

4. **Metadata filtering** - Consider adding metadata to chunks (category, date, source) for filtering before semantic search.

5. **Reranking** - For large knowledge bases, consider a two-stage approach: broad retrieval followed by cross-encoder reranking.

## Related Samples

- **AgentSessionExample** - Demonstrates hybrid search with metadata filtering
