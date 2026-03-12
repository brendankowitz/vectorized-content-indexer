namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for embedding generation performance.
/// </summary>
/// <remarks>
/// <para>
/// Measures the performance of:
/// <list type="bullet">
///   <item><description>Single text embedding (OnnxEmbeddingProvider)</description></item>
///   <item><description>Batch embedding with varying batch sizes</description></item>
///   <item><description>GPU vs CPU performance comparison</description></item>
///   <item><description>Hash embedding baseline (fallback provider)</description></item>
///   <item><description>Different text lengths (50, 200, 500 words)</description></item>
/// </list>
/// </para>
/// <para>
/// Expected performance ranges:
/// <list type="bullet">
///   <item><description>CPU single embed: ~15ms</description></item>
///   <item><description>GPU single embed: ~2ms</description></item>
///   <item><description>Hash embed: sub-millisecond</description></item>
/// </list>
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet handles disposal via GlobalCleanup")]
public class EmbeddingBenchmarks
{
    private OnnxEmbeddingProvider? _onnxProvider;
    private HashEmbeddingProvider? _hashProvider;

    private string _shortText = null!;   // ~50 words
    private string _mediumText = null!;  // ~200 words
    private string _longText = null!;    // ~500 words

    private List<string> _batchTexts10 = null!;
    private List<string> _batchTexts50 = null!;
    private List<string> _batchTexts100 = null!;

    /// <summary>
    /// Text length parameter for parameterized benchmarks.
    /// </summary>
    [Params(50, 200, 500)]
    public int WordCount { get; set; }

    /// <summary>
    /// Batch size parameter for batch embedding benchmarks.
    /// </summary>
    [Params(10, 50, 100)]
    public int BatchSize { get; set; }

    /// <summary>
    /// Global setup - initializes embedding providers and test data.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Initialize ONNX provider
        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        _onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);

        // Initialize hash provider
        _hashProvider = new HashEmbeddingProvider();

        // Generate test texts of different lengths
        _shortText = TestDataGenerator.GenerateText(50, seed: 42);
        _mediumText = TestDataGenerator.GenerateText(200, seed: 42);
        _longText = TestDataGenerator.GenerateText(500, seed: 42);

        // Generate batch texts
        _batchTexts10 = TestDataGenerator.GenerateTextsByWordCount(100, 10);
        _batchTexts50 = TestDataGenerator.GenerateTextsByWordCount(100, 50);
        _batchTexts100 = TestDataGenerator.GenerateTextsByWordCount(100, 100);
    }

    /// <summary>
    /// Global cleanup - disposes embedding providers.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_onnxProvider != null)
        {
            await _onnxProvider.DisposeAsync();
        }

        if (_hashProvider != null)
        {
            await _hashProvider.DisposeAsync();
        }
    }

    #region Single Embedding Benchmarks

    /// <summary>
    /// Benchmark: ONNX embedding of short text (~50 words).
    /// </summary>
    [Benchmark(Baseline = true, Description = "ONNX: Short text (50 words)")]
    [BenchmarkCategory("Single", "ONNX")]
    public async Task<float[]> OnnxEmbed_ShortText()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float>();
        }

        return await _onnxProvider.EmbedAsync(_shortText);
    }

    /// <summary>
    /// Benchmark: ONNX embedding of medium text (~200 words).
    /// </summary>
    [Benchmark(Description = "ONNX: Medium text (200 words)")]
    [BenchmarkCategory("Single", "ONNX")]
    public async Task<float[]> OnnxEmbed_MediumText()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float>();
        }

        return await _onnxProvider.EmbedAsync(_mediumText);
    }

    /// <summary>
    /// Benchmark: ONNX embedding of long text (~500 words).
    /// </summary>
    [Benchmark(Description = "ONNX: Long text (500 words)")]
    [BenchmarkCategory("Single", "ONNX")]
    public async Task<float[]> OnnxEmbed_LongText()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float>();
        }

        return await _onnxProvider.EmbedAsync(_longText);
    }

    /// <summary>
    /// Benchmark: Hash embedding of short text (baseline comparison).
    /// </summary>
    [Benchmark(Description = "Hash: Short text (50 words)")]
    [BenchmarkCategory("Single", "Hash")]
    public async Task<float[]> HashEmbed_ShortText()
    {
        return await _hashProvider!.EmbedAsync(_shortText);
    }

    /// <summary>
    /// Benchmark: Hash embedding of medium text (baseline comparison).
    /// </summary>
    [Benchmark(Description = "Hash: Medium text (200 words)")]
    [BenchmarkCategory("Single", "Hash")]
    public async Task<float[]> HashEmbed_MediumText()
    {
        return await _hashProvider!.EmbedAsync(_mediumText);
    }

    /// <summary>
    /// Benchmark: Hash embedding of long text (baseline comparison).
    /// </summary>
    [Benchmark(Description = "Hash: Long text (500 words)")]
    [BenchmarkCategory("Single", "Hash")]
    public async Task<float[]> HashEmbed_LongText()
    {
        return await _hashProvider!.EmbedAsync(_longText);
    }

    #endregion

    #region Batch Embedding Benchmarks

    /// <summary>
    /// Benchmark: ONNX batch embedding of 10 texts.
    /// </summary>
    [Benchmark(Description = "ONNX: Batch 10 texts")]
    [BenchmarkCategory("Batch", "ONNX")]
    public async Task<float[][]> OnnxEmbed_Batch10()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float[]>();
        }

        return await _onnxProvider.EmbedBatchAsync(_batchTexts10);
    }

    /// <summary>
    /// Benchmark: ONNX batch embedding of 50 texts.
    /// </summary>
    [Benchmark(Description = "ONNX: Batch 50 texts")]
    [BenchmarkCategory("Batch", "ONNX")]
    public async Task<float[][]> OnnxEmbed_Batch50()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float[]>();
        }

        return await _onnxProvider.EmbedBatchAsync(_batchTexts50);
    }

    /// <summary>
    /// Benchmark: ONNX batch embedding of 100 texts.
    /// </summary>
    [Benchmark(Description = "ONNX: Batch 100 texts")]
    [BenchmarkCategory("Batch", "ONNX")]
    public async Task<float[][]> OnnxEmbed_Batch100()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float[]>();
        }

        return await _onnxProvider.EmbedBatchAsync(_batchTexts100);
    }

    /// <summary>
    /// Benchmark: Hash batch embedding of 10 texts.
    /// </summary>
    [Benchmark(Description = "Hash: Batch 10 texts")]
    [BenchmarkCategory("Batch", "Hash")]
    public async Task<float[][]> HashEmbed_Batch10()
    {
        return await _hashProvider!.EmbedBatchAsync(_batchTexts10);
    }

    /// <summary>
    /// Benchmark: Hash batch embedding of 50 texts.
    /// </summary>
    [Benchmark(Description = "Hash: Batch 50 texts")]
    [BenchmarkCategory("Batch", "Hash")]
    public async Task<float[][]> HashEmbed_Batch50()
    {
        return await _hashProvider!.EmbedBatchAsync(_batchTexts50);
    }

    /// <summary>
    /// Benchmark: Hash batch embedding of 100 texts.
    /// </summary>
    [Benchmark(Description = "Hash: Batch 100 texts")]
    [BenchmarkCategory("Batch", "Hash")]
    public async Task<float[][]> HashEmbed_Batch100()
    {
        return await _hashProvider!.EmbedBatchAsync(_batchTexts100);
    }

    #endregion
}

/// <summary>
/// Parameterized embedding benchmarks for text length analysis.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet handles disposal via GlobalCleanup")]
public class EmbeddingTextLengthBenchmarks
{
    private OnnxEmbeddingProvider? _onnxProvider;
    private HashEmbeddingProvider? _hashProvider;
    private string _text = null!;

    /// <summary>
    /// Word count parameter for parameterized benchmarks.
    /// </summary>
    [Params(50, 100, 200, 300, 500)]
    public int WordCount { get; set; }

    /// <summary>
    /// Setup for each parameter iteration.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var modelsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroProximity", "Models");
        _onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath);
        _hashProvider = new HashEmbeddingProvider();
    }

    /// <summary>
    /// Setup for each parameter iteration - generates text with current word count.
    /// </summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _text = TestDataGenerator.GenerateText(WordCount, seed: 42);
    }

    /// <summary>
    /// Global cleanup.
    /// </summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_onnxProvider != null)
        {
            await _onnxProvider.DisposeAsync();
        }

        if (_hashProvider != null)
        {
            await _hashProvider.DisposeAsync();
        }
    }

    /// <summary>
    /// Benchmark: ONNX embedding with parameterized text length.
    /// </summary>
    [Benchmark(Baseline = true, Description = "ONNX Embed")]
    public async Task<float[]> OnnxEmbed()
    {
        if (_onnxProvider == null)
        {
            return Array.Empty<float>();
        }

        return await _onnxProvider.EmbedAsync(_text);
    }

    /// <summary>
    /// Benchmark: Hash embedding with parameterized text length (baseline).
    /// </summary>
    [Benchmark(Description = "Hash Embed")]
    public async Task<float[]> HashEmbed()
    {
        return await _hashProvider!.EmbedAsync(_text);
    }
}
