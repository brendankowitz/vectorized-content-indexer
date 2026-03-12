namespace ZeroProximity.VectorizedContentIndexer.Models;

/// <summary>
/// Represents a document that contains child documents for hierarchical indexing.
/// </summary>
/// <typeparam name="TChild">The type of child documents.</typeparam>
/// <remarks>
/// <para>
/// Hierarchical documents enable parent-child relationships like:
/// <list type="bullet">
///   <item><description>Sessions containing Messages</description></item>
///   <item><description>Documents containing Chunks</description></item>
///   <item><description>Threads containing Posts</description></item>
///   <item><description>Articles containing Sections</description></item>
/// </list>
/// </para>
/// <para>
/// Different indexing strategies can be applied:
/// <list type="bullet">
///   <item><description>ParentOnly - Index combined content</description></item>
///   <item><description>ChildrenOnly - Index individual children</description></item>
///   <item><description>Both - Index at both levels</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IHierarchicalDocument<TChild> : IDocument
    where TChild : ISearchable
{
    /// <summary>
    /// Gets the child documents for hierarchical indexing.
    /// </summary>
    /// <returns>The child documents in order.</returns>
    /// <remarks>
    /// This method may be lazily evaluated. It won't be called if using parent-only indexing.
    /// </remarks>
    IReadOnlyList<TChild> GetChildren();

    /// <summary>
    /// Gets a specific child document by ID.
    /// </summary>
    /// <param name="childId">The ID of the child to retrieve.</param>
    /// <returns>The child document, or null if not found.</returns>
    TChild? GetChildById(string childId) => default;

    /// <summary>
    /// Gets N children before the specified child for context expansion.
    /// </summary>
    /// <param name="childId">The ID of the reference child.</param>
    /// <param name="count">The number of children to retrieve.</param>
    /// <returns>Children before the specified child, in chronological order.</returns>
    /// <remarks>
    /// Used for context expansion (e.g., "show 3 messages before this match").
    /// </remarks>
    IReadOnlyList<TChild> GetChildrenBefore(string childId, int count) => [];

    /// <summary>
    /// Gets N children after the specified child for context expansion.
    /// </summary>
    /// <param name="childId">The ID of the reference child.</param>
    /// <param name="count">The number of children to retrieve.</param>
    /// <returns>Children after the specified child, in chronological order.</returns>
    IReadOnlyList<TChild> GetChildrenAfter(string childId, int count) => [];
}

/// <summary>
/// Represents a child document that maintains a reference to its parent.
/// </summary>
public interface IChildDocument : ISearchable
{
    /// <summary>
    /// Gets the parent document ID.
    /// </summary>
    /// <returns>The ID of the parent document.</returns>
    string GetParentId();
}
