namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for hybrid search performance combining lexical and semantic search.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>Hybrid search vs individual search modes</description></item>
///   <item><description>RRF (Reciprocal Rank Fusion) overhead</description></item>
///   <item><description>Parallel execution benefit</description></item>
///   <item><description>Different weight configurations</description></item>
/// </list>
/// </para>
/// <para>
/// Expected performance ranges:
/// <list type="bullet">
///   <item><description>Hybrid search: ~30ms (parallel execution)</description></item>
///   <item><description>Lexical-only: ~20ms</description></item>
///   <item><description>Semantic-only: ~25ms (embedding + search)</description></item>
/// </list>
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class HybridSearchBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private IEmbeddingProvider _embedder = null!;

    private LuceneSearchEngine<BenchmarkDocument> _luceneEngine = null!;
    private VectorSearchEngine<BenchmarkDocument> _vectorEngine = null!;
    private HybridSearcher<BenchmarkDocument> _hybridSearcher = null!;
    private HybridSearcher<BenchmarkDocument> _hybridSearcherLexicalHeavy = null!;
    private HybridSearcher<BenchmarkDocument> _hybridSearcherSemanticHeavy = null!;

    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup - creates all search engines with pre-populated data.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"hybrid-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Initialize embedder
        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;

        // Create Lucene engine
        var lucenePath = Path.Combine(_tempDirectory, "lucene");
        var mapper = new BenchmarkDocumentMapper();
        _luceneEngine = new LuceneSearchEngine<BenchmarkDocument>(lucenePath, mapper);
        await _luceneEngine.InitializeAsync();

        // Create Vector engine
        var vectorPath = Path.Combine(_tempDirectory, "vector");
        _vectorEngine = new VectorSearchEngine<BenchmarkDocument>(vectorPath, _embedder);
        await _vectorEngine.InitializeAsync();

        // Generate and index documents
        var documents = BenchmarkDocument.CreateBatch(10_000);

        // Index in Lucene (batch)
        await _luceneEngine.IndexManyAsync(documents);

        // Index in Vector (smaller batches due to embedding)
        const int batchSize = 100;
        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
            await _vectorEngine.IndexManyAsync(batch);
        }

        // Create hybrid searchers with different weight configurations
        _hybridSearcher = new HybridSearcher<BenchmarkDocument>(
            _luceneEngine,
            _vectorEngine,
            lexicalWeight: 0.5f,
            semanticWeight: 0.5f);

        _hybridSearcherLexicalHeavy = new HybridSearcher<BenchmarkDocument>(
            _luceneEngine,
            _vectorEngine,
            lexicalWeight: 0.7f,
            semanticWeight: 0.3f);

        _hybridSearcherSemanticHeavy = new HybridSearcher<BenchmarkDocument>(
            _luceneEngine,
            _vectorEngine,
            lexicalWeight: 0.3f,
            semanticWeight: 0.7f);
    }

    /// <summary>
    /// Global cleanup - disposes all engines and removes temp directory.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _hybridSearcher.DisposeAsync();
        await _hybridSearcherLexicalHeavy.DisposeAsync();
        await _hybridSearcherSemanticHeavy.DisposeAsync();
        await _embedder.DisposeAsync();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private string GetNextQuery()
    {
        var query = _queries[_queryIndex % _queries.Count];
        _queryIndex++;
        return query;
    }

    #region Search Mode Comparison

    /// <summary>
    /// Benchmark: Lexical-only search (BM25).
    /// </summary>
    [Benchmark(Description = "Lexical only")]
    [BenchmarkCategory("SearchMode")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_LexicalOnly()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Semantic-only search (vector similarity).
    /// </summary>
    [Benchmark(Description = "Semantic only")]
    [BenchmarkCategory("SearchMode")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_SemanticOnly()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Hybrid search (RRF fusion).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Hybrid (RRF)")]
    [BenchmarkCategory("SearchMode")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Hybrid()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid search with detailed breakdown.
    /// </summary>
    [Benchmark(Description = "Hybrid with breakdown")]
    [BenchmarkCategory("SearchMode")]
    public async Task<IReadOnlyList<HybridSearchResult<BenchmarkDocument>>> Search_HybridWithBreakdown()
    {
        return await _hybridSearcher.SearchWithBreakdownAsync(GetNextQuery(), maxResults: 10);
    }

    #endregion

    #region Weight Configuration Comparison

    /// <summary>
    /// Benchmark: Hybrid with balanced weights (0.5/0.5).
    /// </summary>
    [Benchmark(Description = "Hybrid balanced (0.5/0.5)")]
    [BenchmarkCategory("Weights")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Balanced()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid with lexical-heavy weights (0.7/0.3).
    /// </summary>
    [Benchmark(Description = "Hybrid lexical-heavy (0.7/0.3)")]
    [BenchmarkCategory("Weights")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_LexicalHeavy()
    {
        return await _hybridSearcherLexicalHeavy.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid with semantic-heavy weights (0.3/0.7).
    /// </summary>
    [Benchmark(Description = "Hybrid semantic-heavy (0.3/0.7)")]
    [BenchmarkCategory("Weights")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_SemanticHeavy()
    {
        return await _hybridSearcherSemanticHeavy.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    #endregion

    #region Result Size Benchmarks

    /// <summary>
    /// Benchmark: Hybrid search with top-5 results.
    /// </summary>
    [Benchmark(Description = "Hybrid top-5")]
    [BenchmarkCategory("ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top5()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 5, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid search with top-10 results.
    /// </summary>
    [Benchmark(Description = "Hybrid top-10")]
    [BenchmarkCategory("ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top10()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid search with top-20 results.
    /// </summary>
    [Benchmark(Description = "Hybrid top-20")]
    [BenchmarkCategory("ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top20()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 20, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid search with top-50 results.
    /// </summary>
    [Benchmark(Description = "Hybrid top-50")]
    [BenchmarkCategory("ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top50()
    {
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 50, mode: SearchMode.Hybrid);
    }

    #endregion

    #region Query Type Benchmarks

    /// <summary>
    /// Benchmark: Hybrid with keyword-like query (favors lexical).
    /// </summary>
    [Benchmark(Description = "Keyword query")]
    [BenchmarkCategory("QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_KeywordQuery()
    {
        // Exact term queries tend to favor lexical search
        return await _hybridSearcher.SearchAsync("algorithm", maxResults: 10, mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid with semantic query (favors vector).
    /// </summary>
    [Benchmark(Description = "Semantic query")]
    [BenchmarkCategory("QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_SemanticQuery()
    {
        // Natural language queries tend to favor semantic search
        return await _hybridSearcher.SearchAsync(
            "What is the best way to implement efficient search",
            maxResults: 10,
            mode: SearchMode.Hybrid);
    }

    /// <summary>
    /// Benchmark: Hybrid with mixed query (benefits both).
    /// </summary>
    [Benchmark(Description = "Mixed query")]
    [BenchmarkCategory("QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_MixedQuery()
    {
        // Queries with specific terms + context benefit from both
        return await _hybridSearcher.SearchAsync(
            "vector embedding search algorithm",
            maxResults: 10,
            mode: SearchMode.Hybrid);
    }

    #endregion

    #region Operations Benchmarks

    /// <summary>
    /// Benchmark: Get document count.
    /// </summary>
    [Benchmark(Description = "GetCount")]
    [BenchmarkCategory("Operations")]
    public async Task<int> GetCount()
    {
        return await _hybridSearcher.GetCountAsync();
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _hybridSearcher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _hybridSearcherLexicalHeavy?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _hybridSearcherSemanticHeavy?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _embedder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Benchmarks measuring parallel execution benefit of hybrid search.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class HybridParallelBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private IEmbeddingProvider _embedder = null!;

    private LuceneSearchEngine<BenchmarkDocument> _luceneEngine = null!;
    private VectorSearchEngine<BenchmarkDocument> _vectorEngine = null!;
    private HybridSearcher<BenchmarkDocument> _hybridSearcher = null!;

    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"hybrid-parallel-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;

        var lucenePath = Path.Combine(_tempDirectory, "lucene");
        var mapper = new BenchmarkDocumentMapper();
        _luceneEngine = new LuceneSearchEngine<BenchmarkDocument>(lucenePath, mapper);
        await _luceneEngine.InitializeAsync();

        var vectorPath = Path.Combine(_tempDirectory, "vector");
        _vectorEngine = new VectorSearchEngine<BenchmarkDocument>(vectorPath, _embedder);
        await _vectorEngine.InitializeAsync();

        var documents = BenchmarkDocument.CreateBatch(5_000);
        await _luceneEngine.IndexManyAsync(documents);

        const int batchSize = 100;
        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
            await _vectorEngine.IndexManyAsync(batch);
        }

        _hybridSearcher = new HybridSearcher<BenchmarkDocument>(_luceneEngine, _vectorEngine);
    }

    /// <summary>
    /// Global cleanup.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _hybridSearcher.DisposeAsync();
        await _embedder.DisposeAsync();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private string GetNextQuery()
    {
        var query = _queries[_queryIndex % _queries.Count];
        _queryIndex++;
        return query;
    }

    /// <summary>
    /// Benchmark: Sequential execution (lexical then semantic).
    /// </summary>
    [Benchmark(Description = "Sequential (lexical+semantic)")]
    [BenchmarkCategory("Parallel")]
    public async Task<int> Search_Sequential()
    {
        var query = GetNextQuery();

        // Execute sequentially
        var lexicalResults = await _luceneEngine.SearchAsync(query, maxResults: 30, mode: SearchMode.Lexical);
        var semanticResults = await _vectorEngine.SearchAsync(query, maxResults: 30, mode: SearchMode.Semantic);

        return lexicalResults.Count + semanticResults.Count;
    }

    /// <summary>
    /// Benchmark: Parallel execution (hybrid search uses Task.WhenAll).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Parallel (hybrid)")]
    [BenchmarkCategory("Parallel")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Parallel()
    {
        // Hybrid search internally uses Task.WhenAll for parallel execution
        return await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _hybridSearcher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _embedder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}
