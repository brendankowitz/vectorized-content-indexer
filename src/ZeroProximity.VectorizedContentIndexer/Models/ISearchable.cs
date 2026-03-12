namespace ZeroProximity.VectorizedContentIndexer.Models;

/// <summary>
/// Minimal interface for any content that can be indexed and searched.
/// Implement this interface to make your content searchable via keyword or semantic search.
/// </summary>
/// <remarks>
/// <para>
/// This is the simplest contract for searchable content. If you need to expose
/// additional metadata for Lucene field mapping or filtering, implement <see cref="IDocument"/> instead.
/// </para>
/// <para>
/// Example implementation:
/// <code>
/// public record DocumentChunk : ISearchable
/// {
///     public required string Id { get; init; }
///     public required string Content { get; init; }
///     public required DateTime CreatedAt { get; init; }
///
///     public string GetSearchableText() => Content;
///     public DateTime GetTimestamp() => CreatedAt;
/// }
/// </code>
/// </para>
/// </remarks>
public interface ISearchable
{
    /// <summary>
    /// Gets the unique identifier for this content.
    /// Used for deduplication and result retrieval.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the text content to be indexed and searched.
    /// This text is used for both keyword (BM25) and semantic (embedding) search.
    /// </summary>
    /// <returns>The searchable text content.</returns>
    string GetSearchableText();

    /// <summary>
    /// Gets the timestamp associated with this content.
    /// Used for temporal relevance decay and sorting.
    /// </summary>
    /// <returns>The content timestamp.</returns>
    DateTime GetTimestamp();
}
