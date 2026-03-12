namespace ZeroProximity.VectorizedContentIndexer.Models;

/// <summary>
/// Represents a search result containing the matched document and relevance information.
/// </summary>
/// <typeparam name="TDocument">The type of document in the result.</typeparam>
/// <param name="Document">The matched document.</param>
/// <param name="Score">The relevance score (higher is more relevant).</param>
/// <param name="Highlight">Optional highlighted snippet showing matched terms.</param>
/// <param name="DecayFactor">Optional temporal decay factor applied to the score (0.0 to 1.0).</param>
public record SearchResult<TDocument>(
    TDocument Document,
    double Score,
    string? Highlight = null,
    double? DecayFactor = null
) where TDocument : ISearchable;

/// <summary>
/// Represents a search result with scoring breakdown for hybrid search analysis.
/// </summary>
/// <typeparam name="TDocument">The type of document in the result.</typeparam>
/// <param name="Document">The matched document.</param>
/// <param name="Score">The combined relevance score.</param>
/// <param name="LexicalScore">The BM25 keyword search score component.</param>
/// <param name="SemanticScore">The vector similarity score component.</param>
/// <param name="Highlight">Optional highlighted snippet showing matched terms.</param>
/// <param name="DecayFactor">Optional temporal decay factor applied to the score (0.0 to 1.0).</param>
public record HybridSearchResult<TDocument>(
    TDocument Document,
    double Score,
    double? LexicalScore,
    double? SemanticScore,
    string? Highlight = null,
    double? DecayFactor = null
) where TDocument : ISearchable;

/// <summary>
/// Represents search results with pagination metadata.
/// </summary>
/// <typeparam name="TDocument">The type of documents in the results.</typeparam>
/// <param name="Results">The search results for the current page.</param>
/// <param name="TotalCount">The total number of matching documents.</param>
/// <param name="Offset">The offset from which results were returned.</param>
/// <param name="Limit">The maximum number of results requested.</param>
public record SearchResultPage<TDocument>(
    IReadOnlyList<SearchResult<TDocument>> Results,
    int TotalCount,
    int Offset,
    int Limit
) where TDocument : ISearchable
{
    /// <summary>
    /// Gets whether there are more results available beyond this page.
    /// </summary>
    public bool HasMore => Offset + Results.Count < TotalCount;
}
