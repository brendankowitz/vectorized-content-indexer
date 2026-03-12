namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

/// <summary>
/// Defines the contract for generating vector embeddings from text.
/// </summary>
/// <remarks>
/// <para>
/// Embeddings are dense vector representations of text that capture semantic meaning.
/// Similar texts produce similar vectors, enabling semantic search via cosine similarity.
/// </para>
/// <para>
/// Implementations include:
/// <list type="bullet">
///   <item><description>OnnxEmbeddingProvider - Uses ONNX Runtime with MiniLM-L6-v2 model (384 dimensions)</description></item>
///   <item><description>HashEmbeddingProvider - Fallback using hash-based vectors (not semantic)</description></item>
/// </list>
/// </para>
/// <para>
/// Thread safety: Implementations must be thread-safe for concurrent embedding generation.
/// The ONNX provider uses a semaphore to serialize model inference (ONNX InferenceSession is not thread-safe).
/// </para>
/// </remarks>
public interface IEmbeddingProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the dimensionality of the embedding vectors.
    /// </summary>
    /// <remarks>
    /// Common dimensions:
    /// <list type="bullet">
    ///   <item><description>MiniLM-L6-v2: 384 dimensions</description></item>
    ///   <item><description>OpenAI Ada: 1536 dimensions</description></item>
    ///   <item><description>Hash-based: Configurable (default 384)</description></item>
    /// </list>
    /// </remarks>
    int Dimensions { get; }

    /// <summary>
    /// Gets the name of the embedding model.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets whether GPU acceleration is available.
    /// </summary>
    bool IsGpuAccelerated { get; }

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A normalized embedding vector with length equal to <see cref="Dimensions"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or empty.</exception>
    /// <remarks>
    /// The returned vector is L2-normalized (unit length), suitable for cosine similarity comparison.
    /// </remarks>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An array of normalized embedding vectors, one per input text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="texts"/> is null or empty.</exception>
    /// <remarks>
    /// Batch embedding is more efficient than individual calls due to vectorization.
    /// Maximum recommended batch size is 32 to avoid memory issues.
    /// </remarks>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
