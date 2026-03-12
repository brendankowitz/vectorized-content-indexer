namespace ZeroProximity.VectorizedContentIndexer.Embeddings;

/// <summary>
/// Factory for creating embedding providers based on available resources.
/// </summary>
/// <remarks>
/// <para>
/// The factory attempts to create providers in order of preference:
/// <list type="number">
///   <item><description>ONNX-based semantic models (preferred for quality)</description></item>
///   <item><description>Hash-based fallback (when models unavailable)</description></item>
/// </list>
/// </para>
/// </remarks>
public static class EmbeddingProviderFactory
{
    /// <summary>
    /// Creates an embedding provider, preferring ONNX-based semantic models
    /// but falling back to hash-based embeddings if models are unavailable.
    /// </summary>
    /// <param name="modelsPath">Path to the directory containing model files. If null, uses bundled models only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An embedding provider (never null).</returns>
    /// <remarks>
    /// This method never fails - it always returns a working provider.
    /// Check <see cref="IEmbeddingProvider.ModelName"/> to determine which type was created.
    /// </remarks>
    public static async Task<IEmbeddingProvider> CreateAsync(
        string? modelsPath = null,
        CancellationToken ct = default)
    {
        // Try ONNX provider if models path is specified
        if (!string.IsNullOrWhiteSpace(modelsPath))
        {
            var onnxProvider = await OnnxEmbeddingProvider.TryCreateAsync(modelsPath, ct);
            if (onnxProvider is not null)
            {
                return onnxProvider;
            }
        }

        // Fallback to hash-based embeddings
        return new HashEmbeddingProvider();
    }

    /// <summary>
    /// Creates a hash-based embedding provider (non-semantic fallback).
    /// </summary>
    /// <returns>A hash-based embedding provider.</returns>
    /// <remarks>
    /// Use this when you explicitly want hash-based embeddings, or when
    /// you know ML models are unavailable.
    /// </remarks>
    public static IEmbeddingProvider CreateHashProvider()
    {
        return new HashEmbeddingProvider();
    }

    /// <summary>
    /// Attempts to create an ONNX-based semantic embedding provider.
    /// </summary>
    /// <param name="modelsPath">Path to the directory containing model files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ONNX embedding provider, or null if model files are not found.</returns>
    public static async Task<OnnxEmbeddingProvider?> TryCreateOnnxProviderAsync(
        string modelsPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsPath);
        return await OnnxEmbeddingProvider.TryCreateAsync(modelsPath, ct);
    }
}
