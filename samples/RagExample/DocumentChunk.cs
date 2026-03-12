using ZeroProximity.VectorizedContentIndexer.Models;

namespace RagExample;

/// <summary>
/// Represents a chunk of a document that can be indexed for vector search.
/// This is a minimal implementation of ISearchable suitable for RAG scenarios.
/// </summary>
/// <remarks>
/// <para>
/// In RAG (Retrieval-Augmented Generation) applications, documents are typically
/// split into smaller chunks (paragraphs, sentences, or fixed-size windows) before
/// indexing. This allows for more precise retrieval of relevant context.
/// </para>
/// <para>
/// Example chunking strategies:
/// - Fixed-size chunks (e.g., 500 characters with 50 character overlap)
/// - Paragraph-based chunks
/// - Sentence-based chunks with semantic boundaries
/// </para>
/// </remarks>
public sealed record DocumentChunk : ISearchable
{
    /// <summary>
    /// Unique identifier for this chunk.
    /// Format: {sourceDocument}:chunk:{chunkIndex}
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The text content of this chunk.
    /// This is what gets embedded and searched against.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The name of the source document this chunk came from.
    /// Useful for attribution and grouping results.
    /// </summary>
    public required string SourceDocument { get; init; }

    /// <summary>
    /// The position of this chunk within the source document.
    /// Used for ordering and context reconstruction.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// When this chunk was indexed.
    /// Used for temporal relevance decay if enabled.
    /// </summary>
    public DateTime IndexedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Returns the content text for embedding and search.
    /// </summary>
    public string GetSearchableText() => Content;

    /// <summary>
    /// Returns the timestamp for temporal relevance calculations.
    /// </summary>
    public DateTime GetTimestamp() => IndexedAt;

    /// <summary>
    /// Creates a chunk from a document with automatic ID generation.
    /// </summary>
    /// <param name="sourceDocument">Name of the source document.</param>
    /// <param name="content">The chunk content.</param>
    /// <param name="chunkIndex">Position in the document.</param>
    /// <returns>A new DocumentChunk instance.</returns>
    public static DocumentChunk Create(string sourceDocument, string content, int chunkIndex)
    {
        return new DocumentChunk
        {
            Id = $"{sourceDocument}:chunk:{chunkIndex}",
            Content = content,
            SourceDocument = sourceDocument,
            ChunkIndex = chunkIndex
        };
    }
}
