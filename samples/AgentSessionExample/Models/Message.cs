using ZeroProximity.VectorizedContentIndexer.Models;

namespace AgentSessionExample.Models;

/// <summary>
/// Represents a single message in an agent conversation session.
/// Implements IChildDocument to maintain parent session reference.
/// </summary>
/// <remarks>
/// Messages can be individually indexed for fine-grained search,
/// or combined at the session level for broader context retrieval.
/// </remarks>
public sealed record Message : IChildDocument
{
    /// <summary>
    /// Unique identifier for this message.
    /// Format: {sessionId}:msg:{messageIndex}
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The session this message belongs to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Role of the message sender: "user", "assistant", or "system".
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The actual text content of the message.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// When this message was sent.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Optional tool calls made by the assistant.
    /// </summary>
    public List<string>? ToolCalls { get; init; }

    /// <summary>
    /// Returns the message content for search indexing.
    /// </summary>
    public string GetSearchableText() => Content;

    /// <summary>
    /// Returns the message timestamp.
    /// </summary>
    public DateTime GetTimestamp() => Timestamp;

    /// <summary>
    /// Returns the parent session ID for hierarchical document support.
    /// </summary>
    public string GetParentId() => SessionId;

    /// <summary>
    /// Creates a user message.
    /// </summary>
    public static Message CreateUserMessage(string sessionId, int index, string content, DateTime? timestamp = null)
    {
        return new Message
        {
            Id = $"{sessionId}:msg:{index}",
            SessionId = sessionId,
            Role = "user",
            Content = content,
            Timestamp = timestamp ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an assistant message.
    /// </summary>
    public static Message CreateAssistantMessage(
        string sessionId,
        int index,
        string content,
        DateTime? timestamp = null,
        List<string>? toolCalls = null)
    {
        return new Message
        {
            Id = $"{sessionId}:msg:{index}",
            SessionId = sessionId,
            Role = "assistant",
            Content = content,
            Timestamp = timestamp ?? DateTime.UtcNow,
            ToolCalls = toolCalls
        };
    }
}
