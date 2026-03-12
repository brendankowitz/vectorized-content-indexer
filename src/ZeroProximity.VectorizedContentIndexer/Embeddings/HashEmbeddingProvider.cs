using System.Text.RegularExpressions;

namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

/// <summary>
/// Hash-based fallback embedding provider using FNV-1a algorithm.
/// </summary>
/// <remarks>
/// <para>
/// This provider generates vector embeddings using a deterministic hash-based approach.
/// It is NOT semantic - similar texts do not produce similar vectors unless they share
/// exact words. This is intended as a fallback when ML models are unavailable.
/// </para>
/// <para>
/// The algorithm:
/// <list type="number">
///   <item><description>Tokenize text into words</description></item>
///   <item><description>Hash each word using FNV-1a</description></item>
///   <item><description>Use hash as seed to randomly activate dimensions</description></item>
///   <item><description>Apply TF weighting and normalize</description></item>
/// </list>
/// </para>
/// <para>
/// Thread safety: This class is fully thread-safe with no shared mutable state.
/// </para>
/// </remarks>
public sealed partial class HashEmbeddingProvider : IEmbeddingProvider
{
    private const int VectorDimensions = 384; // Match MiniLM dimensions
    private const int ActivationsPerWord = 8;
    private const uint FnvOffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    [GeneratedRegex(@"\w+", RegexOptions.Compiled)]
    private static partial Regex WordTokenizer();

    /// <inheritdoc />
    public int Dimensions => VectorDimensions;

    /// <inheritdoc />
    public string ModelName => "Hash-FNV1a";

    /// <inheritdoc />
    public bool IsGpuAccelerated => false;

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var vector = new float[VectorDimensions];
        var words = TokenizeText(text);

        if (words.Count == 0)
        {
            return Task.FromResult(vector);
        }

        var tfWeight = 1.0f / words.Count;

        foreach (var word in words)
        {
            var hash = ComputeFnv1aHash(word.ToLowerInvariant());
            var random = new Random((int)hash);

            for (int i = 0; i < ActivationsPerWord; i++)
            {
                var dimension = random.Next(VectorDimensions);
                vector[dimension] += tfWeight;
            }
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    /// <inheritdoc />
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            throw new ArgumentException("Texts collection cannot be empty.", nameof(texts));
        }

        var results = new float[texts.Count][];

        for (int i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = await EmbedAsync(texts[i], cancellationToken);
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

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // No resources to dispose
        return ValueTask.CompletedTask;
    }

    private static List<string> TokenizeText(string text)
    {
        var matches = WordTokenizer().Matches(text);
        var words = new List<string>(matches.Count);

        foreach (Match match in matches)
        {
            words.Add(match.Value);
        }

        return words;
    }

    private static uint ComputeFnv1aHash(string text)
    {
        var hash = FnvOffsetBasis;

        foreach (var c in text)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return hash;
    }
}
