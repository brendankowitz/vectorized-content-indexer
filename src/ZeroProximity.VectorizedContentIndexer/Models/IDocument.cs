namespace ZeroProximity.VectorizedContentIndexer.Models;

/// <summary>
/// Extended interface for documents with metadata support.
/// Implement this interface when you need custom Lucene field mapping,
/// filtering, or faceting capabilities.
/// </summary>
/// <remarks>
/// <para>
/// The metadata dictionary allows you to expose arbitrary fields that can be:
/// - Indexed for search (analyzed text fields)
/// - Stored for retrieval (non-analyzed string fields)
/// - Used for filtering (exact match fields)
/// - Used for faceting (categorical fields)
/// - Used for sorting (numeric/date fields)
/// </para>
/// <para>
/// Example implementation:
/// <code>
/// public class SessionDocument : IDocument
/// {
///     private readonly Session _session;
///
///     public string Id => _session.Id;
///     public string GetSearchableText() =>
///         string.Join("\n", _session.Messages.Select(m => m.Content));
///     public DateTime GetTimestamp() => _session.StartedAt;
///
///     public IDictionary&lt;string, object&gt; GetMetadata() => new Dictionary&lt;string, object&gt;
///     {
///         ["AgentType"] = _session.AgentType,
///         ["ProjectPath"] = _session.ProjectPath,
///         ["IsActive"] = _session.IsActive,
///         ["MessageCount"] = _session.MessageCount
///     };
/// }
/// </code>
/// </para>
/// </remarks>
public interface IDocument : ISearchable
{
    /// <summary>
    /// Gets the metadata dictionary for Lucene field mapping and filtering.
    /// </summary>
    /// <returns>
    /// A dictionary of field names to values. Values can be:
    /// - <see cref="string"/> - Text content (analyzed or exact match)
    /// - <see cref="int"/>, <see cref="long"/>, <see cref="double"/> - Numeric values
    /// - <see cref="bool"/> - Boolean filters
    /// - <see cref="DateTime"/> - Date/time values
    /// - <see cref="IEnumerable{T}"/> - Multi-valued fields
    /// </returns>
    IDictionary<string, object> GetMetadata();
}
