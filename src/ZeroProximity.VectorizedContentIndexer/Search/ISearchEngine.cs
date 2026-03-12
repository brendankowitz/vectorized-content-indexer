using ZeroProximity.VectorizedContentIndexer.Models;

namespace ZeroProximity.VectorizedContentIndexer.Search;

/// <summary>
/// Defines the contract for a search engine that indexes and searches documents.
/// </summary>
/// <typeparam name="TDocument">The type of document to index and search.</typeparam>
/// <remarks>
/// <para>
/// Implementations include:
/// <list type="bullet">
///   <item><description>LuceneSearchEngine - BM25 keyword search</description></item>
///   <item><description>VectorSearchEngine - Semantic search with AJVI index</description></item>
///   <item><description>HybridSearcher - RRF fusion combining both</description></item>
/// </list>
/// </para>
/// <para>
/// Search engines are thread-safe for concurrent reads but serialize writes.
/// </para>
/// </remarks>
public interface ISearchEngine<TDocument> : IAsyncDisposable
    where TDocument : ISearchable
{
    /// <summary>
    /// Indexes a document for search.
    /// </summary>
    /// <param name="document">The document to index.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    Task IndexAsync(TDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes multiple documents for search.
    /// </summary>
    /// <param name="documents">The documents to index.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documents"/> is null.</exception>
    Task IndexManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for documents matching the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="mode">The search mode to use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of search results ordered by relevance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="query"/> is null or empty.</exception>
    Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document from the index.
    /// </summary>
    /// <param name="documentId">The ID of the document to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the document was found and deleted; otherwise, false.</returns>
    Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple documents from the index.
    /// </summary>
    /// <param name="documentIds">The IDs of the documents to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of documents that were deleted.</returns>
    Task<int> DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all documents from the index.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of documents in the index.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document count.</returns>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the index for better search performance.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This operation may be slow and should be run during maintenance windows.
    /// For Lucene, this performs segment merging. For AJVI, this compacts the index file.
    /// </remarks>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}
