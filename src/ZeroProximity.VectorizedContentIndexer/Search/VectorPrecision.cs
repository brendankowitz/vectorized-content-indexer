namespace ZeroProximity.VectorizedContentIndexer.Search;

/// <summary>
/// Specifies the precision for vector storage in the AJVI index.
/// </summary>
public enum VectorPrecision
{
    /// <summary>
    /// 32-bit floating point precision.
    /// Higher accuracy, larger storage (4 bytes per dimension).
    /// </summary>
    /// <remarks>
    /// Storage per vector (384 dimensions): 1,536 bytes
    /// </remarks>
    Float32,

    /// <summary>
    /// 16-bit floating point precision.
    /// Slightly reduced accuracy, 50% smaller storage (2 bytes per dimension).
    /// </summary>
    /// <remarks>
    /// Storage per vector (384 dimensions): 768 bytes
    /// Recommended for most use cases - minimal quality impact in practice.
    /// </remarks>
    Float16
}
