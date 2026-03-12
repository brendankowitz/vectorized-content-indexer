using ZeroProximity.VectorizedContentIndexer.Models;
using ZeroProximity.VectorizedContentIndexer.Search.Lucene;
using ZeroProximity.VectorizedContentIndexer.Search.Vector;

namespace ZeroProximity.VectorizedContentIndexer.Search;

/// <summary>
/// Combines lexical (BM25) and semantic (vector) search using Reciprocal Rank Fusion (RRF).
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="ISearchable"/>.</typeparam>
/// <remarks>
/// <para>
/// Hybrid search provides the best of both worlds by combining:
/// <list type="bullet">
///   <item><description>Lexical (BM25) search for exact keyword matching</description></item>
///   <item><description>Semantic (vector) search for conceptual similarity</description></item>
/// </list>
/// </para>
/// <para>
/// The Reciprocal Rank Fusion algorithm combines results using the formula:
/// <code>RRF_score = sum(weight_i / (k + rank_i))</code>
/// where k is typically 60 (a constant that controls how quickly scores decay with rank).
/// </para>
/// <para>
/// This approach is more robust than score normalization because it only uses rank positions,
/// making it independent of the different score distributions from each search method.
/// </para>
/// </remarks>
public sealed partial class HybridSearcher<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    private const int DefaultRrfK = 60;
    private const float DefaultLexicalWeight = 0.5f;
    private const float DefaultSemanticWeight = 0.5f;

    private readonly LuceneSearchEngine<TDocument> _lexicalEngine;
    private readonly VectorSearchEngine<TDocument> _vectorEngine;
    private readonly float _lexicalWeight;
    private readonly float _semanticWeight;
    private readonly int _rrfK;
    private readonly ILogger<HybridSearcher<TDocument>>? _logger;
    private bool _disposed;

    /// <summary>
    /// Creates a new hybrid searcher combining lexical and semantic search.
    /// </summary>
    /// <param name="lexicalEngine">Lexical search engine (BM25).</param>
    /// <param name="vectorEngine">Semantic search engine (vector-based).</param>
    /// <param name="lexicalWeight">Weight for lexical search in fusion. Defaults to 0.5.</param>
    /// <param name="semanticWeight">Weight for semantic search in fusion. Defaults to 0.5.</param>
    /// <param name="rrfK">RRF constant (controls score decay rate). Defaults to 60.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <exception cref="ArgumentException">Thrown when weights are invalid.</exception>
    public HybridSearcher(
        LuceneSearchEngine<TDocument> lexicalEngine,
        VectorSearchEngine<TDocument> vectorEngine,
        float lexicalWeight = DefaultLexicalWeight,
        float semanticWeight = DefaultSemanticWeight,
        int rrfK = DefaultRrfK,
        ILogger<HybridSearcher<TDocument>>? logger = null)
    {
        _lexicalEngine = lexicalEngine ?? throw new ArgumentNullException(nameof(lexicalEngine));
        _vectorEngine = vectorEngine ?? throw new ArgumentNullException(nameof(vectorEngine));

        if (lexicalWeight < 0 || semanticWeight < 0)
        {
            throw new ArgumentException("Weights cannot be negative");
        }

        if (lexicalWeight + semanticWeight == 0)
        {
            throw new ArgumentException("At least one weight must be positive");
        }

        if (rrfK <= 0)
        {
            throw new ArgumentException("RRF k must be positive", nameof(rrfK));
        }

        _lexicalWeight = lexicalWeight;
        _semanticWeight = semanticWeight;
        _rrfK = rrfK;
        _logger = logger;
    }

    /// <summary>
    /// Gets the lexical (BM25) search weight in fusion.
    /// </summary>
    public float LexicalWeight => _lexicalWeight;

    /// <summary>
    /// Gets the semantic (vector) search weight in fusion.
    /// </summary>
    public float SemanticWeight => _semanticWeight;

    /// <summary>
    /// Gets the RRF k constant.
    /// </summary>
    public int RrfK => _rrfK;

    /// <summary>
    /// Initializes both lexical and semantic search engines.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _lexicalEngine.InitializeAsync(cancellationToken),
            _vectorEngine.InitializeAsync(cancellationToken)
        ).ConfigureAwait(false);

        if (_logger?.IsEnabled(LogLevel.Information) == true)
        {
            Log.Initialized(_logger, _lexicalWeight, _semanticWeight, _rrfK);
        }
    }

    /// <inheritdoc />
    public async Task IndexAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await Task.WhenAll(
            _lexicalEngine.IndexAsync(document, cancellationToken),
            _vectorEngine.IndexAsync(document, cancellationToken)
        ).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task IndexManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList(); // Materialize to avoid multiple enumeration

        await Task.WhenAll(
            _lexicalEngine.IndexManyAsync(documentList, cancellationToken),
            _vectorEngine.IndexManyAsync(documentList, cancellationToken)
        ).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(query);

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults must be positive.");
        }

        if (maxResults > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults cannot exceed 1000.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResult<TDocument>>();
        }

        return mode switch
        {
            SearchMode.Lexical => await _lexicalEngine.SearchAsync(query, maxResults, SearchMode.Lexical, cancellationToken)
                .ConfigureAwait(false),

            SearchMode.Semantic => await _vectorEngine.SearchAsync(query, maxResults, SearchMode.Semantic, cancellationToken)
                .ConfigureAwait(false),

            SearchMode.Hybrid => await HybridSearchAsync(query, maxResults, cancellationToken)
                .ConfigureAwait(false),

            _ => throw new NotSupportedException($"Search mode {mode} is not supported")
        };
    }

    /// <summary>
    /// Performs hybrid search and returns detailed scoring breakdown.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Hybrid search results with lexical and semantic score components.</returns>
    public async Task<IReadOnlyList<HybridSearchResult<TDocument>>> SearchWithBreakdownAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<HybridSearchResult<TDocument>>();
        }

        // Fetch more results from each engine for better fusion
        var fetchCount = maxResults * 3;

        // Run both searches in parallel
        var lexicalTask = _lexicalEngine.SearchAsync(query, fetchCount, SearchMode.Lexical, cancellationToken);
        var semanticTask = _vectorEngine.SearchAsync(query, fetchCount, SearchMode.Semantic, cancellationToken);

        await Task.WhenAll(lexicalTask, semanticTask).ConfigureAwait(false);

        var lexicalResults = await lexicalTask.ConfigureAwait(false);
        var semanticResults = await semanticTask.ConfigureAwait(false);

        // Apply RRF scoring with detailed breakdown
        var fusedScores = new Dictionary<string, (double TotalScore, double LexicalScore, double SemanticScore, SearchResult<TDocument> Result)>();

        // Score lexical results (1-based ranking)
        for (int rank = 0; rank < lexicalResults.Count; rank++)
        {
            var result = lexicalResults[rank];
            var rrfScore = _lexicalWeight / (_rrfK + rank + 1);

            if (fusedScores.TryGetValue(result.Document.Id, out var existing))
            {
                fusedScores[result.Document.Id] = (
                    existing.TotalScore + rrfScore,
                    existing.LexicalScore + rrfScore,
                    existing.SemanticScore,
                    result
                );
            }
            else
            {
                fusedScores[result.Document.Id] = (rrfScore, rrfScore, 0.0, result);
            }
        }

        // Score semantic results (1-based ranking)
        for (int rank = 0; rank < semanticResults.Count; rank++)
        {
            var result = semanticResults[rank];
            var rrfScore = _semanticWeight / (_rrfK + rank + 1);

            if (fusedScores.TryGetValue(result.Document.Id, out var existing))
            {
                fusedScores[result.Document.Id] = (
                    existing.TotalScore + rrfScore,
                    existing.LexicalScore,
                    existing.SemanticScore + rrfScore,
                    existing.Result
                );
            }
            else
            {
                fusedScores[result.Document.Id] = (rrfScore, 0.0, rrfScore, result);
            }
        }

        // Sort by fused score and return top results
        var results = fusedScores.Values
            .OrderByDescending(x => x.TotalScore)
            .Take(maxResults)
            .Select(x => new HybridSearchResult<TDocument>(
                x.Result.Document,
                x.TotalScore,
                x.LexicalScore > 0 ? x.LexicalScore : null,
                x.SemanticScore > 0 ? x.SemanticScore : null,
                x.Result.Highlight,
                x.Result.DecayFactor
            ))
            .ToList();

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.HybridSearchCompleted(_logger, query, results.Count, lexicalResults.Count, semanticResults.Count);
        }

        return results;
    }

    private async Task<IReadOnlyList<SearchResult<TDocument>>> HybridSearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        // Fetch more results from each engine for better fusion
        var fetchCount = maxResults * 3;

        // Run both searches in parallel
        var lexicalTask = _lexicalEngine.SearchAsync(query, fetchCount, SearchMode.Lexical, cancellationToken);
        var semanticTask = _vectorEngine.SearchAsync(query, fetchCount, SearchMode.Semantic, cancellationToken);

        await Task.WhenAll(lexicalTask, semanticTask).ConfigureAwait(false);

        var lexicalResults = await lexicalTask.ConfigureAwait(false);
        var semanticResults = await semanticTask.ConfigureAwait(false);

        // Apply RRF scoring
        var fusedScores = new Dictionary<string, (double Score, SearchResult<TDocument> Result)>();

        // Score lexical results (1-based ranking)
        for (int rank = 0; rank < lexicalResults.Count; rank++)
        {
            var result = lexicalResults[rank];
            var rrfScore = _lexicalWeight / (_rrfK + rank + 1);

            if (fusedScores.TryGetValue(result.Document.Id, out var existing))
            {
                fusedScores[result.Document.Id] = (existing.Score + rrfScore, result);
            }
            else
            {
                fusedScores[result.Document.Id] = (rrfScore, result);
            }
        }

        // Score semantic results (1-based ranking)
        for (int rank = 0; rank < semanticResults.Count; rank++)
        {
            var result = semanticResults[rank];
            var rrfScore = _semanticWeight / (_rrfK + rank + 1);

            if (fusedScores.TryGetValue(result.Document.Id, out var existing))
            {
                fusedScores[result.Document.Id] = (existing.Score + rrfScore, existing.Result);
            }
            else
            {
                fusedScores[result.Document.Id] = (rrfScore, result);
            }
        }

        // Sort by fused score and return top results
        var results = fusedScores.Values
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Result with { Score = x.Score })
            .ToList();

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.RrfSearchCompleted(_logger, query, results.Count);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            _lexicalEngine.DeleteAsync(documentId, cancellationToken),
            _vectorEngine.DeleteAsync(documentId, cancellationToken)
        ).ConfigureAwait(false);

        return results[0] || results[1];
    }

    /// <inheritdoc />
    public async Task<int> DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var documentIdList = documentIds.ToList();

        var results = await Task.WhenAll(
            _lexicalEngine.DeleteManyAsync(documentIdList, cancellationToken),
            _vectorEngine.DeleteManyAsync(documentIdList, cancellationToken)
        ).ConfigureAwait(false);

        return results[0];
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _lexicalEngine.ClearAsync(cancellationToken),
            _vectorEngine.ClearAsync(cancellationToken)
        ).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        // Use lexical engine count as authoritative
        return await _lexicalEngine.GetCountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _lexicalEngine.OptimizeAsync(cancellationToken),
            _vectorEngine.OptimizeAsync(cancellationToken)
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a document to the cache of both engines for faster retrieval.
    /// </summary>
    /// <param name="document">The document to cache.</param>
    public void CacheDocument(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _lexicalEngine.CacheDocument(document);
        _vectorEngine.CacheDocument(document);
    }

    /// <summary>
    /// Adds multiple documents to the cache of both engines.
    /// </summary>
    /// <param name="documents">The documents to cache.</param>
    public void CacheDocuments(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var docList = documents.ToList();
        _lexicalEngine.CacheDocuments(docList);
        _vectorEngine.CacheDocuments(docList);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lexicalEngine.DisposeAsync().ConfigureAwait(false);
        await _vectorEngine.DisposeAsync().ConfigureAwait(false);

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// High-performance logging methods using source generators.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "HybridSearcher initialized with weights: lexical={LexicalWeight}, semantic={SemanticWeight}, k={K}")]
        public static partial void Initialized(ILogger logger, float lexicalWeight, float semanticWeight, int k);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid search for '{Query}' returned {Count} results (lexical: {LexicalCount}, semantic: {SemanticCount})")]
        public static partial void HybridSearchCompleted(ILogger logger, string query, int count, int lexicalCount, int semanticCount);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid RRF search for '{Query}' returned {Count} results")]
        public static partial void RrfSearchCompleted(ILogger logger, string query, int count);
    }
}
