namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for Lucene search engine performance.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>Indexing documents (single, batch)</description></item>
///   <item><description>BM25 search performance</description></item>
///   <item><description>Query parsing overhead</description></item>
///   <item><description>Index size variations (1K, 10K, 100K documents)</description></item>
///   <item><description>Result set sizes (top-10, top-100)</description></item>
///   <item><description>Cache hit vs miss performance</description></item>
/// </list>
/// </para>
/// <para>
/// Expected performance ranges:
/// <list type="bullet">
///   <item><description>Index single document: ~1-5ms</description></item>
///   <item><description>Search 10K documents: ~20ms</description></item>
///   <item><description>Batch indexing: improved throughput</description></item>
/// </list>
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class LuceneSearchBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private LuceneSearchEngine<BenchmarkDocument> _engineSmall = null!;   // 1K docs
    private LuceneSearchEngine<BenchmarkDocument> _engineMedium = null!;  // 10K docs
    private LuceneSearchEngine<BenchmarkDocument> _engineLarge = null!;   // 100K docs

    private BenchmarkDocument _singleDocument = null!;
    private List<BenchmarkDocument> _batchDocuments = null!;
    private List<string> _queries = null!;

    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup - creates indexes with pre-populated data.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"lucene-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Generate test data
        _singleDocument = BenchmarkDocument.Create(99999, TestDataGenerator.GenerateDocument(99999));
        _batchDocuments = BenchmarkDocument.CreateBatch(100);
        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;

        // Create and populate engines
        _engineSmall = await CreateAndPopulateEngine("small", 1_000);
        _engineMedium = await CreateAndPopulateEngine("medium", 10_000);
        _engineLarge = await CreateAndPopulateEngine("large", 100_000);
    }

    private async Task<LuceneSearchEngine<BenchmarkDocument>> CreateAndPopulateEngine(string name, int docCount)
    {
        var indexPath = Path.Combine(_tempDirectory, $"lucene-{name}");
        var mapper = new BenchmarkDocumentMapper();
        var engine = new LuceneSearchEngine<BenchmarkDocument>(indexPath, mapper);

        await engine.InitializeAsync();

        // Index documents in batches for efficiency
        var documents = BenchmarkDocument.CreateBatch(docCount);
        const int batchSize = 1000;

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize);
            await engine.IndexManyAsync(batch);
        }

        return engine;
    }

    /// <summary>
    /// Global cleanup - disposes engines and removes temp directory.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _engineSmall.DisposeAsync();
        await _engineMedium.DisposeAsync();
        await _engineLarge.DisposeAsync();

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

    #region Indexing Benchmarks

    /// <summary>
    /// Benchmark: Index a single document.
    /// </summary>
    [Benchmark(Description = "Index single document")]
    [BenchmarkCategory("Index")]
    public async Task IndexSingleDocument()
    {
        await _engineMedium.IndexAsync(_singleDocument);
    }

    /// <summary>
    /// Benchmark: Index a batch of 100 documents.
    /// </summary>
    [Benchmark(Description = "Index batch (100 docs)")]
    [BenchmarkCategory("Index")]
    public async Task IndexBatch()
    {
        await _engineMedium.IndexManyAsync(_batchDocuments);
    }

    #endregion

    #region Search Benchmarks - Result Size Variations

    /// <summary>
    /// Benchmark: Search with top-10 results (10K index).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Search top-10 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top10_10K()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Search with top-50 results (10K index).
    /// </summary>
    [Benchmark(Description = "Search top-50 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top50_10K()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 50, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Search with top-100 results (10K index).
    /// </summary>
    [Benchmark(Description = "Search top-100 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top100_10K()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 100, mode: SearchMode.Lexical);
    }

    #endregion

    #region Search Benchmarks - Index Size Variations

    /// <summary>
    /// Benchmark: Search in small index (1K documents).
    /// </summary>
    [Benchmark(Description = "Search top-10 (1K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_1K()
    {
        return await _engineSmall.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Search in medium index (10K documents).
    /// </summary>
    [Benchmark(Description = "Search top-10 (10K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_10K()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Search in large index (100K documents).
    /// </summary>
    [Benchmark(Description = "Search top-10 (100K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_100K()
    {
        return await _engineLarge.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
    }

    #endregion

    #region Query Type Benchmarks

    /// <summary>
    /// Benchmark: Simple single-term query.
    /// </summary>
    [Benchmark(Description = "Simple query")]
    [BenchmarkCategory("Search", "QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_SimpleQuery()
    {
        return await _engineMedium.SearchAsync("algorithm", maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Multi-term query (implicit AND).
    /// </summary>
    [Benchmark(Description = "Multi-term query")]
    [BenchmarkCategory("Search", "QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_MultiTermQuery()
    {
        return await _engineMedium.SearchAsync("machine learning model", maxResults: 10, mode: SearchMode.Lexical);
    }

    /// <summary>
    /// Benchmark: Phrase query (quoted).
    /// </summary>
    [Benchmark(Description = "Phrase query")]
    [BenchmarkCategory("Search", "QueryType")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_PhraseQuery()
    {
        return await _engineMedium.SearchAsync("\"vector search\"", maxResults: 10, mode: SearchMode.Lexical);
    }

    #endregion

    #region Cache Performance Benchmarks

    /// <summary>
    /// Benchmark: Repeated search (cache hit scenario).
    /// </summary>
    [Benchmark(Description = "Search (repeated query)")]
    [BenchmarkCategory("Search", "Cache")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Repeated()
    {
        // Same query repeated - should benefit from Lucene's internal caching
        return await _engineMedium.SearchAsync("vector search algorithm", maxResults: 10, mode: SearchMode.Lexical);
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
        return await _engineMedium.GetCountAsync();
    }

    /// <summary>
    /// Benchmark: Delete a document.
    /// </summary>
    [Benchmark(Description = "Delete document")]
    [BenchmarkCategory("Operations")]
    public async Task<bool> DeleteDocument()
    {
        // Delete and re-index to maintain state
        var deleted = await _engineMedium.DeleteAsync(_singleDocument.Id);
        await _engineMedium.IndexAsync(_singleDocument);
        return deleted;
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
                _engineSmall?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _engineMedium?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _engineLarge?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Parameterized Lucene search benchmarks for scaling analysis.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class LuceneScalingBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private Dictionary<int, LuceneSearchEngine<BenchmarkDocument>> _engines = null!;
    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Index size parameter.
    /// </summary>
    [Params(1000, 5000, 10000, 50000)]
    public int IndexSize { get; set; }

    /// <summary>
    /// Max results parameter.
    /// </summary>
    [Params(10, 50)]
    public int MaxResults { get; set; }

    /// <summary>
    /// Global setup - creates indexes of all sizes.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"lucene-scale-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;
        _engines = new Dictionary<int, LuceneSearchEngine<BenchmarkDocument>>();

        // Pre-create all indexes
        foreach (var size in new[] { 1000, 5000, 10000, 50000 })
        {
            var indexPath = Path.Combine(_tempDirectory, $"lucene-{size}");
            var mapper = new BenchmarkDocumentMapper();
            var engine = new LuceneSearchEngine<BenchmarkDocument>(indexPath, mapper);

            await engine.InitializeAsync();

            var documents = BenchmarkDocument.CreateBatch(size);
            const int batchSize = 1000;

            for (int i = 0; i < documents.Count; i += batchSize)
            {
                var batch = documents.Skip(i).Take(batchSize);
                await engine.IndexManyAsync(batch);
            }

            _engines[size] = engine;
        }
    }

    /// <summary>
    /// Global cleanup.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        foreach (var engine in _engines.Values)
        {
            await engine.DisposeAsync();
        }

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
    /// Benchmark: Search with parameterized index size and max results.
    /// </summary>
    [Benchmark(Description = "Search")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search()
    {
        return await _engines[IndexSize].SearchAsync(GetNextQuery(), MaxResults, SearchMode.Lexical);
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
                foreach (var engine in _engines.Values)
                {
                    engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            _disposed = true;
        }
    }
}
