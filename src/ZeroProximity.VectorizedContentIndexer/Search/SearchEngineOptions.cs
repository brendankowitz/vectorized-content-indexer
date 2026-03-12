namespace ZeroProximity.VectorizedContentIndexer.Search;

/// <summary>
/// Configuration options for search engines.
/// </summary>
public class SearchEngineOptions
{
    /// <summary>
    /// The path where index files are stored.
    /// </summary>
    public string IndexPath { get; set; } = "./index";

    /// <summary>
    /// The default search mode when not specified.
    /// </summary>
    public SearchMode DefaultMode { get; set; } = SearchMode.Hybrid;

    /// <summary>
    /// The precision for vector storage in the AJVI index.
    /// </summary>
    public VectorPrecision Precision { get; set; } = VectorPrecision.Float16;

    /// <summary>
    /// The weight for lexical (keyword) scores in hybrid search.
    /// Must be between 0.0 and 1.0.
    /// </summary>
    public double LexicalWeight { get; set; } = 0.5;

    /// <summary>
    /// The weight for semantic (vector) scores in hybrid search.
    /// Must be between 0.0 and 1.0.
    /// </summary>
    public double SemanticWeight { get; set; } = 0.5;

    /// <summary>
    /// The k parameter for Reciprocal Rank Fusion (RRF).
    /// Higher values reduce the impact of high-ranking documents.
    /// Default is 60 (same as Elasticsearch).
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// Whether to apply temporal decay to search scores.
    /// </summary>
    public bool ApplyDecay { get; set; }

    /// <summary>
    /// The half-life in days for temporal decay.
    /// After this many days, a document's decay factor is 0.5.
    /// </summary>
    public double DecayHalfLifeDays { get; set; } = 90.0;

    /// <summary>
    /// Maximum number of documents to retrieve before filtering/ranking.
    /// </summary>
    public int MaxRetrievalCount { get; set; } = 1000;

    /// <summary>
    /// Whether to generate highlight snippets in search results.
    /// </summary>
    public bool EnableHighlighting { get; set; } = true;

    /// <summary>
    /// Maximum length of highlight snippets in characters.
    /// </summary>
    public int HighlightMaxLength { get; set; } = 200;
}
