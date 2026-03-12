namespace ZeroProximity.VectorizedContentIndexer.Search;

/// <summary>
/// Specifies the search mode to use when querying the index.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Keyword-based search using Lucene BM25 scoring.
    /// Best for exact term matching, boolean queries, and phrase searches.
    /// </summary>
    /// <remarks>
    /// BM25 (Best Matching 25) is a bag-of-words retrieval function that ranks
    /// documents based on term frequency, inverse document frequency, and
    /// document length normalization.
    /// </remarks>
    Lexical,

    /// <summary>
    /// Embedding-based search using vector similarity.
    /// Best for semantic/conceptual matching and natural language queries.
    /// </summary>
    /// <remarks>
    /// Semantic search uses dense vector embeddings (e.g., MiniLM-L6-v2)
    /// to find conceptually similar content even when exact terms don't match.
    /// Uses cosine similarity for scoring.
    /// </remarks>
    Semantic,

    /// <summary>
    /// Combined search using Reciprocal Rank Fusion (RRF).
    /// Best for general-purpose search combining keyword and semantic matching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hybrid search executes both lexical and semantic searches in parallel,
    /// then combines results using RRF fusion with the formula:
    /// <c>score = sum(1 / (k + rank_i))</c> where k is typically 60.
    /// </para>
    /// <para>
    /// This approach provides the best of both worlds: exact keyword matching
    /// plus semantic understanding, without the complexity of cross-encoder reranking.
    /// </para>
    /// </remarks>
    Hybrid
}
