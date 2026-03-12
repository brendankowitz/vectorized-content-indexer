using Lucene.Net.Documents;
using ZeroProximity.VectorizedContentIndexer.Models;

namespace ZeroProximity.VectorizedContentIndexer.Search.Lucene;

/// <summary>
/// Defines the contract for mapping between domain documents and Lucene documents.
/// </summary>
/// <typeparam name="TDocument">The domain document type that implements <see cref="ISearchable"/>.</typeparam>
/// <remarks>
/// <para>
/// Implementations of this interface control how domain documents are indexed and retrieved:
/// <list type="bullet">
///   <item><description>Which fields to index for full-text search</description></item>
///   <item><description>Which fields to store for retrieval</description></item>
///   <item><description>How to reconstruct domain objects from Lucene documents</description></item>
/// </list>
/// </para>
/// <para>
/// The default implementation (<see cref="DefaultLuceneDocumentMapper{TDocument}"/>) handles
/// basic ISearchable and IDocument types. Implement a custom mapper for specialized indexing needs.
/// </para>
/// </remarks>
public interface ILuceneDocumentMapper<TDocument>
    where TDocument : ISearchable
{
    /// <summary>
    /// Gets the name of the field that stores the document ID.
    /// </summary>
    /// <remarks>
    /// This field is used for document lookups and deletion.
    /// </remarks>
    string IdField { get; }

    /// <summary>
    /// Gets the name of the field that stores the searchable content.
    /// </summary>
    /// <remarks>
    /// This field is analyzed for full-text search using BM25 scoring.
    /// </remarks>
    string ContentField { get; }

    /// <summary>
    /// Gets the name of the field that stores the timestamp.
    /// </summary>
    /// <remarks>
    /// This field is used for temporal filtering and decay calculations.
    /// </remarks>
    string TimestampField { get; }

    /// <summary>
    /// Maps a domain document to a Lucene document for indexing.
    /// </summary>
    /// <param name="document">The domain document to index.</param>
    /// <returns>A Lucene document ready for indexing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    Document MapToLuceneDocument(TDocument document);

    /// <summary>
    /// Maps a Lucene document back to a domain document.
    /// </summary>
    /// <param name="luceneDoc">The Lucene document to map.</param>
    /// <returns>The reconstructed domain document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="luceneDoc"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method is used to reconstruct domain objects from search results.
    /// The returned document may be a partial reconstruction if not all fields
    /// were stored in the index.
    /// </para>
    /// <para>
    /// For complex document types, consider storing minimal data in Lucene
    /// and using a document cache for full reconstruction.
    /// </para>
    /// </remarks>
    TDocument MapFromLuceneDocument(Document luceneDoc);

    /// <summary>
    /// Gets a simple text highlight showing matched terms in context.
    /// </summary>
    /// <param name="content">The full content to highlight.</param>
    /// <param name="query">The search query terms.</param>
    /// <param name="maxLength">Maximum highlight length in characters. Defaults to 200.</param>
    /// <returns>A highlight snippet, or null if no match found.</returns>
    string? GetHighlight(string? content, string query, int maxLength = 200);
}
