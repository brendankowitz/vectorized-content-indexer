using ZeroProximity.VectorizedContentIndexer.Models;

namespace AgentSessionExample.Models;

/// <summary>
/// Adapter that wraps a Session for search indexing with IDocument and IHierarchicalDocument.
/// </summary>
/// <remarks>
/// <para>
/// This class demonstrates the adapter pattern for making domain models searchable.
/// Rather than modifying the Session class directly (which might be shared with other
/// systems), we create an adapter that implements the search interfaces.
/// </para>
/// <para>
/// Key features:
/// <list type="bullet">
///   <item><description>IDocument - Exposes metadata for Lucene field mapping</description></item>
///   <item><description>IHierarchicalDocument - Supports parent-child indexing with Messages</description></item>
///   <item><description>Context expansion - GetChildrenBefore/After for message context</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class SessionDocument : IHierarchicalDocument<Message>
{
    private readonly Session _session;

    /// <summary>
    /// Creates a new SessionDocument adapter.
    /// </summary>
    /// <param name="session">The session to wrap.</param>
    public SessionDocument(Session session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Gets the underlying session.
    /// </summary>
    public Session Session => _session;

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public string Id => _session.Id;

    /// <summary>
    /// Returns combined searchable text from all messages.
    /// Includes role prefixes for context.
    /// </summary>
    public string GetSearchableText()
    {
        // Combine all messages with role prefixes for better search context
        return string.Join("\n\n", _session.Messages.Select(m =>
            $"[{m.Role.ToUpperInvariant()}]: {m.Content}"));
    }

    /// <summary>
    /// Returns the session start time for temporal relevance.
    /// </summary>
    public DateTime GetTimestamp() => _session.StartedAt;

    /// <summary>
    /// Returns metadata for Lucene field mapping and filtering.
    /// </summary>
    /// <remarks>
    /// These fields can be used for:
    /// - Filtering (e.g., AgentType == "code-assistant")
    /// - Faceting (e.g., count sessions by agent type)
    /// - Sorting (e.g., by MessageCount descending)
    /// </remarks>
    public IDictionary<string, object> GetMetadata()
    {
        var metadata = new Dictionary<string, object>
        {
            ["AgentType"] = _session.AgentType,
            ["ProjectPath"] = _session.ProjectPath,
            ["IsActive"] = _session.IsActive,
            ["MessageCount"] = _session.MessageCount,
            ["StartedAt"] = _session.StartedAt
        };

        if (_session.EndedAt.HasValue)
        {
            metadata["EndedAt"] = _session.EndedAt.Value;
        }

        if (!string.IsNullOrEmpty(_session.Summary))
        {
            metadata["Summary"] = _session.Summary;
        }

        return metadata;
    }

    /// <summary>
    /// Returns child messages for hierarchical indexing.
    /// </summary>
    public IReadOnlyList<Message> GetChildren() => _session.Messages;

    /// <summary>
    /// Gets a specific message by ID.
    /// </summary>
    public Message? GetChildById(string childId)
    {
        return _session.Messages.FirstOrDefault(m => m.Id == childId);
    }

    /// <summary>
    /// Gets messages before the specified message for context expansion.
    /// </summary>
    /// <param name="childId">The ID of the reference message.</param>
    /// <param name="count">Number of messages to retrieve.</param>
    /// <returns>Messages before the reference, in chronological order.</returns>
    public IReadOnlyList<Message> GetChildrenBefore(string childId, int count)
    {
        var index = _session.Messages.FindIndex(m => m.Id == childId);
        if (index <= 0)
        {
            return Array.Empty<Message>();
        }

        var startIndex = Math.Max(0, index - count);
        return _session.Messages.GetRange(startIndex, index - startIndex);
    }

    /// <summary>
    /// Gets messages after the specified message for context expansion.
    /// </summary>
    /// <param name="childId">The ID of the reference message.</param>
    /// <param name="count">Number of messages to retrieve.</param>
    /// <returns>Messages after the reference, in chronological order.</returns>
    public IReadOnlyList<Message> GetChildrenAfter(string childId, int count)
    {
        var index = _session.Messages.FindIndex(m => m.Id == childId);
        if (index < 0 || index >= _session.Messages.Count - 1)
        {
            return Array.Empty<Message>();
        }

        var startIndex = index + 1;
        var availableCount = Math.Min(count, _session.Messages.Count - startIndex);
        return _session.Messages.GetRange(startIndex, availableCount);
    }

    /// <summary>
    /// Gets messages around the specified message for context.
    /// </summary>
    /// <param name="childId">The ID of the reference message.</param>
    /// <param name="before">Number of messages before.</param>
    /// <param name="after">Number of messages after.</param>
    /// <returns>Tuple of (before messages, matched message, after messages).</returns>
    public (IReadOnlyList<Message> Before, Message? Match, IReadOnlyList<Message> After) GetContextWindow(
        string childId,
        int before,
        int after)
    {
        var match = GetChildById(childId);
        var beforeMessages = GetChildrenBefore(childId, before);
        var afterMessages = GetChildrenAfter(childId, after);

        return (beforeMessages, match, afterMessages);
    }
}
