namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for AJVI (Agent Journal Vector Index) performance.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>Adding entries (single, batch)</description></item>
///   <item><description>Vector search (top-1, top-10, top-100)</description></item>
///   <item><description>Float16 vs Float32 precision performance</description></item>
///   <item><description>Index size variations (1K, 10K, 100K vectors)</description></item>
///   <item><description>Duplicate detection (hash lookup)</description></item>
///   <item><description>Memory usage patterns</description></item>
/// </list>
/// </para>
/// <para>
/// Expected performance ranges:
/// <list type="bullet">
///   <item><description>Add entry: sub-millisecond</description></item>
///   <item><description>Search 100K vectors: ~80ms</description></item>
///   <item><description>Hash lookup: ~O(n) linear scan</description></item>
/// </list>
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class AjviIndexBenchmarks
{
    private const int Dimensions = 384;  // MiniLM dimensions

    private string _tempDirectory = null!;
    private AjviIndex _indexFloat16 = null!;
    private AjviIndex _indexFloat32 = null!;
    private AjviIndex _indexSmall = null!;   // 1K vectors
    private AjviIndex _indexMedium = null!;  // 10K vectors
    private AjviIndex _indexLarge = null!;   // 100K vectors

    private float[] _queryVector = null!;
    private List<float[]> _batchVectors = null!;
    private List<byte[]> _batchHashes = null!;
    private byte[] _existingHash = null!;
    private byte[] _nonExistingHash = null!;

    /// <summary>
    /// Global setup - creates indexes with pre-populated data.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ajvi-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Generate test data
        _queryVector = TestDataGenerator.GenerateRandomVector(Dimensions, seed: 42);
        _batchVectors = TestDataGenerator.GenerateRandomVectors(100, Dimensions);
        _batchHashes = _batchVectors.Select((_, i) => TestDataGenerator.GenerateContentHash($"batch-content-{i}")).ToList();
        _existingHash = TestDataGenerator.GenerateContentHash("existing-content-0");
        _nonExistingHash = TestDataGenerator.GenerateContentHash("non-existing-content");

        // Create Float16 index with 10K vectors
        _indexFloat16 = CreateAndPopulateIndex("float16.ajvi", VectorPrecision.Float16, 10_000);

        // Create Float32 index with 10K vectors
        _indexFloat32 = CreateAndPopulateIndex("float32.ajvi", VectorPrecision.Float32, 10_000);

        // Create size-varied indexes
        _indexSmall = CreateAndPopulateIndex("small.ajvi", VectorPrecision.Float16, 1_000);
        _indexMedium = CreateAndPopulateIndex("medium.ajvi", VectorPrecision.Float16, 10_000);
        _indexLarge = CreateAndPopulateIndex("large.ajvi", VectorPrecision.Float16, 100_000);
    }

    private AjviIndex CreateAndPopulateIndex(string fileName, VectorPrecision precision, int entryCount)
    {
        var indexPath = Path.Combine(_tempDirectory, fileName);
        var index = AjviIndex.Create(indexPath, Dimensions, precision);

        for (int i = 0; i < entryCount; i++)
        {
            var vector = TestDataGenerator.GenerateRandomVector(Dimensions, seed: i);
            var hash = TestDataGenerator.GenerateContentHash($"content-{i}");
            var docId = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            index.AddEntry(hash, docId, 0, timestamp, vector);
        }

        return index;
    }

    /// <summary>
    /// Global cleanup - disposes indexes and removes temp directory.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _indexFloat16?.Dispose();
        _indexFloat32?.Dispose();
        _indexSmall?.Dispose();
        _indexMedium?.Dispose();
        _indexLarge?.Dispose();

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

    #region Add Entry Benchmarks

    /// <summary>
    /// Benchmark: Add single entry to Float16 index.
    /// </summary>
    [Benchmark(Description = "Add entry (Float16)")]
    [BenchmarkCategory("Add", "Float16")]
    public void AddEntry_Float16()
    {
        var vector = TestDataGenerator.GenerateRandomVector(Dimensions);
        var hash = TestDataGenerator.GenerateContentHash($"new-content-{Guid.NewGuid()}");
        _indexFloat16.AddEntry(hash, Guid.NewGuid(), 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), vector);
    }

    /// <summary>
    /// Benchmark: Add single entry to Float32 index.
    /// </summary>
    [Benchmark(Description = "Add entry (Float32)")]
    [BenchmarkCategory("Add", "Float32")]
    public void AddEntry_Float32()
    {
        var vector = TestDataGenerator.GenerateRandomVector(Dimensions);
        var hash = TestDataGenerator.GenerateContentHash($"new-content-{Guid.NewGuid()}");
        _indexFloat32.AddEntry(hash, Guid.NewGuid(), 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), vector);
    }

    #endregion

    #region Search Benchmarks - Top-K Variations

    /// <summary>
    /// Benchmark: Search with top-1 result (10K index).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Search top-1 (10K)")]
    [BenchmarkCategory("Search", "TopK")]
    public IReadOnlyList<(long, float)> Search_Top1_10K()
    {
        return _indexFloat16.Search(_queryVector, topK: 1);
    }

    /// <summary>
    /// Benchmark: Search with top-10 results (10K index).
    /// </summary>
    [Benchmark(Description = "Search top-10 (10K)")]
    [BenchmarkCategory("Search", "TopK")]
    public IReadOnlyList<(long, float)> Search_Top10_10K()
    {
        return _indexFloat16.Search(_queryVector, topK: 10);
    }

    /// <summary>
    /// Benchmark: Search with top-100 results (10K index).
    /// </summary>
    [Benchmark(Description = "Search top-100 (10K)")]
    [BenchmarkCategory("Search", "TopK")]
    public IReadOnlyList<(long, float)> Search_Top100_10K()
    {
        return _indexFloat16.Search(_queryVector, topK: 100);
    }

    #endregion

    #region Search Benchmarks - Index Size Variations

    /// <summary>
    /// Benchmark: Search in small index (1K vectors).
    /// </summary>
    [Benchmark(Description = "Search top-10 (1K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public IReadOnlyList<(long, float)> Search_1K()
    {
        return _indexSmall.Search(_queryVector, topK: 10);
    }

    /// <summary>
    /// Benchmark: Search in medium index (10K vectors).
    /// </summary>
    [Benchmark(Description = "Search top-10 (10K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public IReadOnlyList<(long, float)> Search_10K()
    {
        return _indexMedium.Search(_queryVector, topK: 10);
    }

    /// <summary>
    /// Benchmark: Search in large index (100K vectors).
    /// </summary>
    [Benchmark(Description = "Search top-10 (100K)")]
    [BenchmarkCategory("Search", "IndexSize")]
    public IReadOnlyList<(long, float)> Search_100K()
    {
        return _indexLarge.Search(_queryVector, topK: 10);
    }

    #endregion

    #region Precision Comparison Benchmarks

    /// <summary>
    /// Benchmark: Search in Float16 index (10K vectors).
    /// </summary>
    [Benchmark(Description = "Search Float16 (10K)")]
    [BenchmarkCategory("Search", "Precision")]
    public IReadOnlyList<(long, float)> Search_Float16()
    {
        return _indexFloat16.Search(_queryVector, topK: 20);
    }

    /// <summary>
    /// Benchmark: Search in Float32 index (10K vectors).
    /// </summary>
    [Benchmark(Description = "Search Float32 (10K)")]
    [BenchmarkCategory("Search", "Precision")]
    public IReadOnlyList<(long, float)> Search_Float32()
    {
        return _indexFloat32.Search(_queryVector, topK: 20);
    }

    #endregion

    #region Hash Lookup Benchmarks

    /// <summary>
    /// Benchmark: Check for existing hash (worst case - hash at end).
    /// </summary>
    [Benchmark(Description = "ContainsHash (exists)")]
    [BenchmarkCategory("Hash")]
    public bool ContainsHash_Existing()
    {
        return _indexFloat16.ContainsHash(_existingHash);
    }

    /// <summary>
    /// Benchmark: Check for non-existing hash (full scan).
    /// </summary>
    [Benchmark(Description = "ContainsHash (not exists)")]
    [BenchmarkCategory("Hash")]
    public bool ContainsHash_NotExisting()
    {
        return _indexFloat16.ContainsHash(_nonExistingHash);
    }

    #endregion

    #region Data Access Benchmarks

    /// <summary>
    /// Benchmark: Get vector by index.
    /// </summary>
    [Benchmark(Description = "GetVector")]
    [BenchmarkCategory("Access")]
    public ReadOnlySpan<float> GetVector()
    {
        return _indexFloat16.GetVector(5000);
    }

    /// <summary>
    /// Benchmark: Get document ID by index.
    /// </summary>
    [Benchmark(Description = "GetDocumentId")]
    [BenchmarkCategory("Access")]
    public Guid GetDocumentId()
    {
        return _indexFloat16.GetDocumentId(5000);
    }

    /// <summary>
    /// Benchmark: Get content hash by index.
    /// </summary>
    [Benchmark(Description = "GetContentHash")]
    [BenchmarkCategory("Access")]
    public byte[] GetContentHash()
    {
        return _indexFloat16.GetContentHash(5000);
    }

    #endregion
}

/// <summary>
/// Parameterized AJVI search benchmarks for index size scaling analysis.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class AjviIndexScalingBenchmarks
{
    private const int Dimensions = 384;

    private string _tempDirectory = null!;
    private Dictionary<int, AjviIndex> _indexes = null!;
    private float[] _queryVector = null!;

    /// <summary>
    /// Index size parameter.
    /// </summary>
    [Params(1000, 5000, 10000, 50000, 100000)]
    public int IndexSize { get; set; }

    /// <summary>
    /// Top-K parameter.
    /// </summary>
    [Params(10, 20)]
    public int TopK { get; set; }

    /// <summary>
    /// Global setup - creates indexes of all sizes.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ajvi-scale-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _queryVector = TestDataGenerator.GenerateRandomVector(Dimensions, seed: 42);
        _indexes = new Dictionary<int, AjviIndex>();

        // Pre-create all indexes
        foreach (var size in new[] { 1000, 5000, 10000, 50000, 100000 })
        {
            var indexPath = Path.Combine(_tempDirectory, $"index-{size}.ajvi");
            var index = AjviIndex.Create(indexPath, Dimensions, VectorPrecision.Float16);

            for (int i = 0; i < size; i++)
            {
                var vector = TestDataGenerator.GenerateRandomVector(Dimensions, seed: i);
                var hash = TestDataGenerator.GenerateContentHash($"content-{i}");
                index.AddEntry(hash, Guid.NewGuid(), 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), vector);
            }

            _indexes[size] = index;
        }
    }

    /// <summary>
    /// Global cleanup.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        foreach (var index in _indexes.Values)
        {
            index.Dispose();
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

    /// <summary>
    /// Benchmark: Search with parameterized index size and top-K.
    /// </summary>
    [Benchmark(Description = "Search")]
    public IReadOnlyList<(long, float)> Search()
    {
        return _indexes[IndexSize].Search(_queryVector, TopK);
    }
}
