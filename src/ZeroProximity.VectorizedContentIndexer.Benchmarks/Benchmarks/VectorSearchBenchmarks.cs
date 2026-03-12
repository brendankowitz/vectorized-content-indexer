namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for end-to-end vector search engine performance.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>End-to-end search (embedding + AJVI search)</description></item>
///   <item><description>Index size scaling</description></item>
///   <item><description>Precision impact (Float16 vs Float32)</description></item>
///   <item><description>Document cache performance</description></item>
///   <item><description>Batch search operations</description></item>
/// </list>
/// </para>
/// <para>
/// This benchmarks the VectorSearchEngine which combines:
/// <list type="bullet">
///   <item><description>Embedding generation via IEmbeddingProvider</description></item>
///   <item><description>Vector similarity search via AjviIndex</description></item>
///   <item><description>Document retrieval via cache</description></item>
/// </list>
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class VectorSearchBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private IEmbeddingProvider _embedder = null!;

    private VectorSearchEngine<BenchmarkDocument> _engineSmall = null!;   // 1K docs
    private VectorSearchEngine<BenchmarkDocument> _engineMedium = null!;  // 10K docs

    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup - creates vector search engines with pre-populated data.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"vector-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Initialize embedder
        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;

        // Create and populate engines
        _engineSmall = await CreateAndPopulateEngine("small", 1_000);
        _engineMedium = await CreateAndPopulateEngine("medium", 10_000);
    }

    private async Task<VectorSearchEngine<BenchmarkDocument>> CreateAndPopulateEngine(string name, int docCount)
    {
        var indexPath = Path.Combine(_tempDirectory, $"vector-{name}");
        var engine = new VectorSearchEngine<BenchmarkDocument>(indexPath, _embedder);

        await engine.InitializeAsync();

        // Index documents in batches
        var documents = BenchmarkDocument.CreateBatch(docCount);
        const int batchSize = 100; // Smaller batches due to embedding overhead

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
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

    #region End-to-End Search Benchmarks

    /// <summary>
    /// Benchmark: End-to-end semantic search (1K index).
    /// </summary>
    [Benchmark(Description = "Semantic search (1K)")]
    [BenchmarkCategory("Search", "EndToEnd")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> SemanticSearch_1K()
    {
        return await _engineSmall.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: End-to-end semantic search (10K index).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Semantic search (10K)")]
    [BenchmarkCategory("Search", "EndToEnd")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> SemanticSearch_10K()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
    }

    #endregion

    #region Result Size Benchmarks

    /// <summary>
    /// Benchmark: Semantic search with top-5 results.
    /// </summary>
    [Benchmark(Description = "Search top-5 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top5()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 5, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Semantic search with top-10 results.
    /// </summary>
    [Benchmark(Description = "Search top-10 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top10()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Semantic search with top-20 results.
    /// </summary>
    [Benchmark(Description = "Search top-20 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top20()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 20, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Semantic search with top-50 results.
    /// </summary>
    [Benchmark(Description = "Search top-50 (10K)")]
    [BenchmarkCategory("Search", "ResultSize")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Top50()
    {
        return await _engineMedium.SearchAsync(GetNextQuery(), maxResults: 50, mode: SearchMode.Semantic);
    }

    #endregion

    #region Query Complexity Benchmarks

    /// <summary>
    /// Benchmark: Short query (2-3 words).
    /// </summary>
    [Benchmark(Description = "Short query")]
    [BenchmarkCategory("Search", "QueryLength")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_ShortQuery()
    {
        return await _engineMedium.SearchAsync("vector search", maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Medium query (4-6 words).
    /// </summary>
    [Benchmark(Description = "Medium query")]
    [BenchmarkCategory("Search", "QueryLength")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_MediumQuery()
    {
        return await _engineMedium.SearchAsync("machine learning model training optimization", maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Long query (natural language question).
    /// </summary>
    [Benchmark(Description = "Long query")]
    [BenchmarkCategory("Search", "QueryLength")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_LongQuery()
    {
        return await _engineMedium.SearchAsync(
            "How do I implement efficient vector similarity search for large scale document collections",
            maxResults: 10,
            mode: SearchMode.Semantic);
    }

    #endregion

    #region Cache Performance Benchmarks

    /// <summary>
    /// Benchmark: Repeated query (same embedding).
    /// </summary>
    [Benchmark(Description = "Repeated query")]
    [BenchmarkCategory("Search", "Cache")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Repeated()
    {
        // Note: Query embedding is computed each time, but tests overall consistency
        return await _engineMedium.SearchAsync("vector search algorithm", maxResults: 10, mode: SearchMode.Semantic);
    }

    #endregion

    #region Operations Benchmarks

    /// <summary>
    /// Benchmark: Get vector index count.
    /// </summary>
    [Benchmark(Description = "GetCount")]
    [BenchmarkCategory("Operations")]
    public async Task<int> GetCount()
    {
        return await _engineMedium.GetCountAsync();
    }

    /// <summary>
    /// Benchmark: Get vector index statistics.
    /// </summary>
    [Benchmark(Description = "GetStats")]
    [BenchmarkCategory("Operations")]
    public async Task<VectorIndexStats> GetStats()
    {
        return await _engineMedium.GetStatsAsync();
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
                _embedder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Benchmarks comparing Float16 vs Float32 precision in vector search.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class VectorPrecisionBenchmarks : IDisposable
{
    private string _tempDirectory = null!;
    private IEmbeddingProvider _embedder = null!;

    private VectorSearchEngine<BenchmarkDocument> _engineFloat16 = null!;
    private VectorSearchEngine<BenchmarkDocument> _engineFloat32 = null!;

    private List<string> _queries = null!;
    private int _queryIndex;
    private bool _disposed;

    /// <summary>
    /// Global setup - creates Float16 and Float32 vector search engines.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"vector-precision-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Initialize embedder
        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _embedder = onnxProvider is not null ? onnxProvider : new HashEmbeddingProvider();

        _queries = TestDataGenerator.GenerateQueries(20);
        _queryIndex = 0;

        // Create Float16 engine
        var indexPathF16 = Path.Combine(_tempDirectory, "vector-float16");
        _engineFloat16 = new VectorSearchEngine<BenchmarkDocument>(indexPathF16, _embedder, VectorPrecision.Float16);
        await _engineFloat16.InitializeAsync();

        // Create Float32 engine
        var indexPathF32 = Path.Combine(_tempDirectory, "vector-float32");
        _engineFloat32 = new VectorSearchEngine<BenchmarkDocument>(indexPathF32, _embedder, VectorPrecision.Float32);
        await _engineFloat32.InitializeAsync();

        // Populate both engines with same documents
        var documents = BenchmarkDocument.CreateBatch(5_000);
        const int batchSize = 100;

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
            await _engineFloat16.IndexManyAsync(batch);
            await _engineFloat32.IndexManyAsync(batch);
        }
    }

    /// <summary>
    /// Global cleanup.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _engineFloat16.DisposeAsync();
        await _engineFloat32.DisposeAsync();
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
    /// Benchmark: Search with Float16 precision.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Float16 search")]
    [BenchmarkCategory("Precision")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Float16()
    {
        return await _engineFloat16.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
    }

    /// <summary>
    /// Benchmark: Search with Float32 precision.
    /// </summary>
    [Benchmark(Description = "Float32 search")]
    [BenchmarkCategory("Precision")]
    public async Task<IReadOnlyList<SearchResult<BenchmarkDocument>>> Search_Float32()
    {
        return await _engineFloat32.SearchAsync(GetNextQuery(), maxResults: 10, mode: SearchMode.Semantic);
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
                _engineFloat16?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _engineFloat32?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _embedder?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}
