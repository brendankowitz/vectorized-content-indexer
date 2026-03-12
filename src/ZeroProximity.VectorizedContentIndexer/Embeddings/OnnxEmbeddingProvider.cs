using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

/// <summary>
/// ONNX-based semantic embedding provider using MiniLM-L6-v2 model.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses a local ONNX model to generate semantic embeddings.
/// The MiniLM-L6-v2 model produces 384-dimensional vectors that capture semantic meaning.
/// </para>
/// <para>
/// Thread safety: This class is thread-safe. The ONNX InferenceSession is not thread-safe,
/// so a semaphore is used to serialize inference calls.
/// </para>
/// <para>
/// GPU acceleration: Attempts to use DirectML for GPU acceleration on Windows.
/// Falls back to CPU if DirectML is unavailable.
/// </para>
/// </remarks>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider
{
    private const int VectorDimensions = 384;
    private const int MaxSequenceLength = 256;
    private const int BatchSize = 32;

    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1); // ONNX Session is not thread-safe
    private bool _disposed;

    /// <inheritdoc />
    public int Dimensions => VectorDimensions;

    /// <inheritdoc />
    public string ModelName => "all-MiniLM-L6-v2";

    /// <inheritdoc />
    public bool IsGpuAccelerated { get; }

    /// <summary>
    /// Gets the execution provider being used (e.g., "DirectML (GPU)" or "CPU").
    /// </summary>
    public string ExecutionProvider { get; }

    private OnnxEmbeddingProvider(InferenceSession session, Tokenizer tokenizer, string executionProvider, bool isGpuAccelerated)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        ExecutionProvider = executionProvider;
        IsGpuAccelerated = isGpuAccelerated;
    }

    /// <summary>
    /// Attempts to create an ONNX embedding provider from the specified models path.
    /// Falls back to bundled model if not found on disk.
    /// </summary>
    /// <param name="modelsPath">Path to the directory containing model files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An OnnxEmbeddingProvider instance, or null if model files are unavailable.</returns>
    public static async Task<OnnxEmbeddingProvider?> TryCreateAsync(string modelsPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsPath);

        var modelPath = Path.Combine(modelsPath, "minilm", "model.onnx");
        var tokenizerPath = Path.Combine(modelsPath, "minilm", "tokenizer.json");

        // If model doesn't exist on disk, extract bundled model
        if (!File.Exists(modelPath))
        {
            var extracted = await ExtractBundledModelAsync(modelsPath, ct);
            if (!extracted)
            {
                return null;
            }
        }

        try
        {
            // Create session with auto-detected execution provider (GPU/NPU -> CPU fallback)
            var (session, executionProvider, isGpu) = CreateSessionWithBestProvider(modelPath);

            // Load tokenizer - BertTokenizer needs vocab.txt for MiniLM/BERT models
            var vocabPath = Path.Combine(modelsPath, "minilm", "vocab.txt");
            Tokenizer tokenizer;

            if (File.Exists(vocabPath))
            {
                // Use vocab.txt for proper BERT/WordPiece tokenization
                tokenizer = await Task.Run(() => BertTokenizer.Create(vocabPath), ct);
            }
            else if (File.Exists(tokenizerPath))
            {
                // Try tokenizer.json as fallback
                try
                {
                    tokenizer = await Task.Run(() => BertTokenizer.Create(tokenizerPath), ct);
                }
                catch
                {
                    // Last resort fallback - will produce incorrect token IDs!
                    tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
                }
            }
            else
            {
                tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
            }

            return new OnnxEmbeddingProvider(session, tokenizer, executionProvider, isGpu);
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                or DirectoryNotFoundException
                                or InvalidDataException
                                or OnnxRuntimeException)
        {
            // Model not available or corrupted - fall back to hash embeddings
            return null;
        }
    }

    /// <summary>
    /// Tries to create an InferenceSession with the best available execution provider.
    /// Attempts DirectML (GPU) first, then falls back to CPU.
    /// </summary>
    private static (InferenceSession Session, string Provider, bool IsGpu) CreateSessionWithBestProvider(string modelPath)
    {
        // Try DirectML (Windows GPU - works with AMD, Intel, NVIDIA)
        try
        {
            var options = new SessionOptions();
            options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR; // Suppress warnings
            options.AppendExecutionProvider_DML(0); // Device ID 0
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            var session = new InferenceSession(modelPath, options);
            return (session, "DirectML (GPU)", true);
        }
        catch
        {
            // DirectML not available, continue to CPU fallback
        }

        // Fallback to CPU with optimizations
        {
            var options = new SessionOptions();
            options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR; // Suppress warnings
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.InterOpNumThreads = Environment.ProcessorCount;
            options.IntraOpNumThreads = Environment.ProcessorCount;
            var session = new InferenceSession(modelPath, options);
            return (session, "CPU", false);
        }
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var encoding = _tokenizer.EncodeToIds(text);
        var tokenIds = encoding.Take(MaxSequenceLength).ToList();
        var attentionMask = Enumerable.Repeat(1L, tokenIds.Count).ToList();

        // Pad to max length if needed
        while (tokenIds.Count < MaxSequenceLength)
        {
            tokenIds.Add(0);
            attentionMask.Add(0);
        }

        // Create input tensors
        var inputIdsTensor = new DenseTensor<long>(
            tokenIds.Select(id => (long)id).ToArray(),
            [1, MaxSequenceLength]
        );

        var attentionMaskTensor = new DenseTensor<long>(
            attentionMask.ToArray(),
            [1, MaxSequenceLength]
        );

        // Token type IDs (all zeros for single sentence)
        var tokenTypeIds = new long[MaxSequenceLength];
        var tokenTypeIdsTensor = new DenseTensor<long>(
            tokenTypeIds,
            [1, MaxSequenceLength]
        );

        // Run inference with lock (InferenceSession is not thread-safe)
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var results = _session.Run(inputs);
            var resultsList = results.ToList();
            var output = resultsList[0].AsTensor<float>();

            // Mean pooling
            var embedding = MeanPooling(output, attentionMask.ToArray());

            // Normalize
            Normalize(embedding);

            return embedding;
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            throw new ArgumentException("Texts collection cannot be empty.", nameof(texts));
        }
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = new float[texts.Count][];

        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchEnd = Math.Min(i + BatchSize, texts.Count);
            var batchSize = batchEnd - i;

            for (int j = 0; j < batchSize; j++)
            {
                results[i + j] = await EmbedAsync(texts[i + j], cancellationToken);
            }
        }

        return results;
    }

    /// <summary>
    /// L2-normalizes a vector in-place.
    /// </summary>
    /// <param name="vector">The vector to normalize.</param>
    public static void Normalize(Span<float> vector)
    {
        var sumOfSquares = 0.0f;

        for (int i = 0; i < vector.Length; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }

        if (sumOfSquares > 0)
        {
            var magnitude = MathF.Sqrt(sumOfSquares);

            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }
    }

    private static float[] MeanPooling(Tensor<float> output, long[] attentionMask)
    {
        var sequenceLength = attentionMask.Length;
        var hiddenSize = VectorDimensions;

        var pooled = new float[hiddenSize];
        var maskSum = 0L;

        for (int i = 0; i < sequenceLength; i++)
        {
            if (attentionMask[i] > 0)
            {
                maskSum++;
                for (int j = 0; j < hiddenSize; j++)
                {
                    // Handle different tensor shapes - most models output [batch, sequence, hidden]
                    var index = new[] { 0, i, j };
                    pooled[j] += output[index];
                }
            }
        }

        if (maskSum > 0)
        {
            for (int i = 0; i < hiddenSize; i++)
            {
                pooled[i] /= maskSum;
            }
        }

        return pooled;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _session?.Dispose();
        (_tokenizer as IDisposable)?.Dispose();
        _inferenceLock.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Extracts the bundled MiniLM model to the models path.
    /// </summary>
    private static async Task<bool> ExtractBundledModelAsync(string modelsPath, CancellationToken ct)
    {
        try
        {
            var assembly = typeof(OnnxEmbeddingProvider).Assembly;
            var modelDir = Path.Combine(modelsPath, "minilm");
            Directory.CreateDirectory(modelDir);

            // Try different resource name patterns
            var resourceNames = assembly.GetManifestResourceNames();

            // Find model.onnx resource
            var modelResourceName = resourceNames.FirstOrDefault(n =>
                n.EndsWith("all-MiniLM-L6-v2.onnx", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("model.onnx", StringComparison.OrdinalIgnoreCase));

            if (modelResourceName == null)
            {
                return false; // Bundled model not found
            }

            var modelStream = assembly.GetManifestResourceStream(modelResourceName);
            if (modelStream == null)
            {
                return false; // Bundled model not found
            }

            var modelPath = Path.Combine(modelDir, "model.onnx");
            await using (var fileStream = File.Create(modelPath))
            {
                await modelStream.CopyToAsync(fileStream, ct);
            }
            modelStream.Dispose();

            // Extract vocab.txt (required for BertTokenizer)
            var vocabResourceName = resourceNames.FirstOrDefault(n =>
                n.EndsWith("vocab.txt", StringComparison.OrdinalIgnoreCase));

            if (vocabResourceName != null)
            {
                var vocabStream = assembly.GetManifestResourceStream(vocabResourceName);
                if (vocabStream != null)
                {
                    var vocabPath = Path.Combine(modelDir, "vocab.txt");
                    await using (var fileStream = File.Create(vocabPath))
                    {
                        await vocabStream.CopyToAsync(fileStream, ct);
                    }
                    vocabStream.Dispose();
                }
            }

            // Extract tokenizer.json (optional fallback)
            var tokenizerResourceName = resourceNames.FirstOrDefault(n =>
                n.EndsWith("tokenizer.json", StringComparison.OrdinalIgnoreCase));

            if (tokenizerResourceName != null)
            {
                var tokenizerStream = assembly.GetManifestResourceStream(tokenizerResourceName);
                if (tokenizerStream != null)
                {
                    var tokenizerPath = Path.Combine(modelDir, "tokenizer.json");
                    await using (var fileStream = File.Create(tokenizerPath))
                    {
                        await tokenizerStream.CopyToAsync(fileStream, ct);
                    }
                    tokenizerStream.Dispose();
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
