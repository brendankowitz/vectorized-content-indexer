using System.Diagnostics;

namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for overall system throughput measurements.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>Documents indexed per second</description></item>
///   <item><description>Queries per second (QPS)</description></item>
///   <item><description>Concurrent indexing + searching</description></item>
///   <item><description>Sustained throughput under load</description></item>
/// </list>
/// </para>
/// <para>
/// These benchmarks simulate real-world usage patterns with sustained operations.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ThroughputBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private IEmbeddingProvider _embedder = null!;

    private LuceneSearchEngine<BenchmarkDocument> _luceneEngine = null!;
    private VectorSearchEngine<BenchmarkDocument> _vectorEngine = null!;
    private HybridSearcher<BenchmarkDocument> _hybridSearcher = null!;

    private List<BenchmarkDocument> _documentsToIndex = null!;
    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"throughput-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(100);
        _queryIndex = 0;

        // Prepare documents to index
        _documentsToIndex = BenchmarkDocument.CreateBatch(1000);

        // Create and populate engines with base data
        var lucenePath = Path.Combine(_tempDirectory, "lucene");
        var mapper = new BenchmarkDocumentMapper();
        _luceneEngine = new LuceneSearchEngine<BenchmarkDocument>(lucenePath, mapper);
        await _luceneEngine.InitializeAsync();

        var vectorPath = Path.Combine(_tempDirectory, "vector");
        _vectorEngine = new VectorSearchEngine<BenchmarkDocument>(vectorPath, _embedder);
        await _vectorEngine.InitializeAsync();

        // Pre-populate with some data
        var baseDocuments = BenchmarkDocument.CreateBatch(5_000);
        await _luceneEngine.IndexManyAsync(baseDocuments);

        const int batchSize = 100;
        for (int i = 0; i < baseDocuments.Count; i += batchSize)
        {
            var batch = baseDocuments.Skip(i).Take(batchSize).ToList();
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

    #region Indexing Throughput

    /// <summary>
    /// Benchmark: Lucene batch indexing throughput (100 documents).
    /// </summary>
    [Benchmark(Description = "Lucene: Index 100 docs")]
    [BenchmarkCategory("Indexing", "Lucene")]
    public async Task LuceneIndexThroughput_100()
    {
        var docs = _documentsToIndex.Take(100);
        await _luceneEngine.IndexManyAsync(docs);
    }

    /// <summary>
    /// Benchmark: Lucene batch indexing throughput (500 documents).
    /// </summary>
    [Benchmark(Description = "Lucene: Index 500 docs")]
    [BenchmarkCategory("Indexing", "Lucene")]
    public async Task LuceneIndexThroughput_500()
    {
        var docs = _documentsToIndex.Take(500);
        await _luceneEngine.IndexManyAsync(docs);
    }

    /// <summary>
    /// Benchmark: Vector batch indexing throughput (50 documents - limited by embedding).
    /// </summary>
    [Benchmark(Description = "Vector: Index 50 docs")]
    [BenchmarkCategory("Indexing", "Vector")]
    public async Task VectorIndexThroughput_50()
    {
        var docs = _documentsToIndex.Take(50);
        await _vectorEngine.IndexManyAsync(docs);
    }

    /// <summary>
    /// Benchmark: Vector batch indexing throughput (100 documents).
    /// </summary>
    [Benchmark(Description = "Vector: Index 100 docs")]
    [BenchmarkCategory("Indexing", "Vector")]
    public async Task VectorIndexThroughput_100()
    {
        var docs = _documentsToIndex.Take(100);
        await _vectorEngine.IndexManyAsync(docs);
    }

    /// <summary>
    /// Benchmark: Hybrid batch indexing throughput (50 documents).
    /// </summary>
    [Benchmark(Description = "Hybrid: Index 50 docs")]
    [BenchmarkCategory("Indexing", "Hybrid")]
    public async Task HybridIndexThroughput_50()
    {
        var docs = _documentsToIndex.Take(50);
        await _hybridSearcher.IndexManyAsync(docs);
    }

    #endregion

    #region Query Throughput

    /// <summary>
    /// Benchmark: Lucene query throughput (10 sequential queries).
    /// </summary>
    [Benchmark(Description = "Lucene: 10 queries")]
    [BenchmarkCategory("Query", "Lucene")]
    public async Task<int> LuceneQueryThroughput_10()
    {
        int totalResults = 0;
        for (int i = 0; i < 10; i++)
        {
            var results = await _luceneEngine.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
            totalResults += results.Count;
        }
        return totalResults;
    }

    /// <summary>
    /// Benchmark: Vector query throughput (10 sequential queries).
    /// </summary>
    [Benchmark(Description = "Vector: 10 queries")]
    [BenchmarkCategory("Query", "Vector")]
    public async Task<int> VectorQueryThroughput_10()
    {
        int totalResults = 0;
        for (int i = 0; i < 10; i++)
        {
            var results = await _vectorEngine.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
            totalResults += results.Count;
        }
        return totalResults;
    }

    /// <summary>
    /// Benchmark: Hybrid query throughput (10 sequential queries).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Hybrid: 10 queries")]
    [BenchmarkCategory("Query", "Hybrid")]
    public async Task<int> HybridQueryThroughput_10()
    {
        int totalResults = 0;
        for (int i = 0; i < 10; i++)
        {
            var results = await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
            totalResults += results.Count;
        }
        return totalResults;
    }

    #endregion

    #region Concurrent Operations

    /// <summary>
    /// Benchmark: Concurrent search operations (5 parallel queries).
    /// </summary>
    [Benchmark(Description = "Concurrent: 5 parallel queries")]
    [BenchmarkCategory("Concurrent")]
    public async Task<int> ConcurrentQueries_5()
    {
        var tasks = new Task<IReadOnlyList<SearchResult<BenchmarkDocument>>>[5];

        for (int i = 0; i < 5; i++)
        {
            var query = GetNextQuery();
            tasks[i] = _hybridSearcher.SearchAsync(query, maxResults: 10, mode: SearchMode.Hybrid);
        }

        var results = await Task.WhenAll(tasks);
        return results.Sum(r => r.Count);
    }

    /// <summary>
    /// Benchmark: Concurrent search operations (10 parallel queries).
    /// </summary>
    [Benchmark(Description = "Concurrent: 10 parallel queries")]
    [BenchmarkCategory("Concurrent")]
    public async Task<int> ConcurrentQueries_10()
    {
        var tasks = new Task<IReadOnlyList<SearchResult<BenchmarkDocument>>>[10];

        for (int i = 0; i < 10; i++)
        {
            var query = GetNextQuery();
            tasks[i] = _hybridSearcher.SearchAsync(query, maxResults: 10, mode: SearchMode.Hybrid);
        }

        var results = await Task.WhenAll(tasks);
        return results.Sum(r => r.Count);
    }

    /// <summary>
    /// Benchmark: Mixed read/write operations (index while searching).
    /// </summary>
    [Benchmark(Description = "Mixed: Index + Search")]
    [BenchmarkCategory("Concurrent")]
    public async Task<int> MixedReadWrite()
    {
        // Simulate concurrent index and search operations
        var indexTask = _luceneEngine.IndexAsync(_documentsToIndex[0]);
        var searchTask = _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);

        await Task.WhenAll(indexTask, searchTask);
        return (await searchTask).Count;
    }

    #endregion

    #region Sustained Load

    /// <summary>
    /// Benchmark: Sustained search load (50 consecutive queries).
    /// </summary>
    [Benchmark(Description = "Sustained: 50 queries")]
    [BenchmarkCategory("Sustained")]
    public async Task<int> SustainedSearchLoad_50()
    {
        int totalResults = 0;

        for (int i = 0; i < 50; i++)
        {
            var results = await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
            totalResults += results.Count;
        }

        return totalResults;
    }

    /// <summary>
    /// Benchmark: Sustained search load (100 consecutive queries).
    /// </summary>
    [Benchmark(Description = "Sustained: 100 queries")]
    [BenchmarkCategory("Sustained")]
    public async Task<int> SustainedSearchLoad_100()
    {
        int totalResults = 0;

        for (int i = 0; i < 100; i++)
        {
            var results = await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
            totalResults += results.Count;
        }

        return totalResults;
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
                _embedder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Measures queries per second (QPS) for the search engines.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class QpsBenchmarks : IDisposable
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
    /// Number of queries to execute in QPS test.
    /// </summary>
    [Params(10, 25, 50)]
    public int QueryCount { get; set; }

    /// <summary>
    /// Global setup.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"qps-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(100);
        _queryIndex = 0;

        var lucenePath = Path.Combine(_tempDirectory, "lucene");
        var mapper = new BenchmarkDocumentMapper();
        _luceneEngine = new LuceneSearchEngine<BenchmarkDocument>(lucenePath, mapper);
        await _luceneEngine.InitializeAsync();

        var vectorPath = Path.Combine(_tempDirectory, "vector");
        _vectorEngine = new VectorSearchEngine<BenchmarkDocument>(vectorPath, _embedder);
        await _vectorEngine.InitializeAsync();

        var documents = BenchmarkDocument.CreateBatch(10_000);
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
    /// Benchmark: Lucene QPS.
    /// </summary>
    [Benchmark(Description = "Lucene QPS")]
    [BenchmarkCategory("QPS")]
    public async Task<int> LuceneQps()
    {
        int total = 0;
        for (int i = 0; i < QueryCount; i++)
        {
            var results = await _luceneEngine.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Lexical);
            total += results.Count;
        }
        return total;
    }

    /// <summary>
    /// Benchmark: Vector QPS.
    /// </summary>
    [Benchmark(Description = "Vector QPS")]
    [BenchmarkCategory("QPS")]
    public async Task<int> VectorQps()
    {
        int total = 0;
        for (int i = 0; i < QueryCount; i++)
        {
            var results = await _vectorEngine.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
            total += results.Count;
        }
        return total;
    }

    /// <summary>
    /// Benchmark: Hybrid QPS.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Hybrid QPS")]
    [BenchmarkCategory("QPS")]
    public async Task<int> HybridQps()
    {
        int total = 0;
        for (int i = 0; i < QueryCount; i++)
        {
            var results = await _hybridSearcher.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Hybrid);
            total += results.Count;
        }
        return total;
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
