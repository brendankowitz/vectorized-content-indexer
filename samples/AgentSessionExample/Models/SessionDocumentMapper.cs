using Lucene.Net.Documents;
using ZeroProximity.VectorizedContentIndexer.Search.Lucene;

namespace AgentSessionExample.Models;

/// <summary>
/// Custom Lucene document mapper for SessionDocument.
/// </summary>
/// <remarks>
/// <para>
/// This mapper demonstrates how to create a custom field mapping for complex documents.
/// It extends beyond the basic ISearchable fields to include session-specific metadata
/// that can be used for filtering and faceting.
/// </para>
/// <para>
/// Field types used:
/// <list type="bullet">
///   <item><description>StringField - Exact match, not analyzed (IDs, categories)</description></item>
///   <item><description>TextField - Full-text search, analyzed (content)</description></item>
///   <item><description>Int32Field - Numeric filtering and sorting</description></item>
///   <item><description>Int64Field - Timestamps and large numbers</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class SessionDocumentMapper : ILuceneDocumentMapper<SessionDocument>
{
    // Field name constants
    public const string FieldId = "Id";
    public const string FieldContent = "Content";
    public const string FieldTimestamp = "Timestamp";
    public const string FieldAgentType = "AgentType";
    public const string FieldProjectPath = "ProjectPath";
    public const string FieldIsActive = "IsActive";
    public const string FieldMessageCount = "MessageCount";
    public const string FieldSummary = "Summary";

    /// <inheritdoc />
    public string IdField => FieldId;

    /// <inheritdoc />
    public string ContentField => FieldContent;

    /// <inheritdoc />
    public string TimestampField => FieldTimestamp;

    /// <summary>
    /// Maps a SessionDocument to a Lucene document for indexing.
    /// </summary>
    public Document MapToLuceneDocument(SessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var session = document.Session;
        var luceneDoc = new Document
        {
            // Core searchable fields
            new StringField(FieldId, document.Id, Field.Store.YES),
            new TextField(FieldContent, document.GetSearchableText(), Field.Store.YES),
            new Int64Field(FieldTimestamp, document.GetTimestamp().Ticks, Field.Store.YES),

            // Session metadata fields
            new StringField(FieldAgentType, session.AgentType, Field.Store.YES),
            new StringField(FieldProjectPath, session.ProjectPath, Field.Store.YES),
            new StringField(FieldIsActive, session.IsActive.ToString(), Field.Store.YES),
            new Int32Field(FieldMessageCount, session.MessageCount, Field.Store.YES)
        };

        // Optional summary field
        if (!string.IsNullOrEmpty(session.Summary))
        {
            luceneDoc.Add(new TextField(FieldSummary, session.Summary, Field.Store.YES));
        }

        return luceneDoc;
    }

    /// <summary>
    /// Maps a Lucene document back to a SessionDocument.
    /// </summary>
    /// <remarks>
    /// This reconstruction is partial - we rebuild the Session with minimal data
    /// stored in Lucene. In production, you might use a document cache to return
    /// the full Session object with all messages.
    /// </remarks>
    public SessionDocument MapFromLuceneDocument(Document luceneDoc)
    {
        ArgumentNullException.ThrowIfNull(luceneDoc);

        var id = luceneDoc.Get(FieldId);
        var content = luceneDoc.Get(FieldContent);
        var timestampTicks = long.Parse(luceneDoc.Get(FieldTimestamp));
        var agentType = luceneDoc.Get(FieldAgentType);
        var projectPath = luceneDoc.Get(FieldProjectPath);
        var isActive = bool.Parse(luceneDoc.Get(FieldIsActive));
        var messageCount = int.Parse(luceneDoc.Get(FieldMessageCount));
        var summary = luceneDoc.Get(FieldSummary);

        // Reconstruct a minimal Session
        // Note: Messages are not reconstructed from Lucene - they would need to come
        // from the original data source or a document cache
        var session = new Session
        {
            Id = id,
            AgentType = agentType,
            ProjectPath = projectPath,
            StartedAt = new DateTime(timestampTicks, DateTimeKind.Utc),
            IsActive = isActive,
            Summary = summary,
            Messages = new List<Message>() // Empty - messages not stored in Lucene index
        };

        return new SessionDocument(session);
    }

    /// <summary>
    /// Generates a highlight snippet showing matched terms in context.
    /// </summary>
    public string? GetHighlight(string? content, string query, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(query))
        {
            return null;
        }

        // Split query into terms
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lowerContent = content.ToLowerInvariant();

        // Find the first matching term and return surrounding context
        foreach (var term in queryTerms)
        {
            var lowerTerm = term.ToLowerInvariant().Trim('"', '\'');
            if (lowerTerm.Length < 2) continue; // Skip very short terms

            var index = lowerContent.IndexOf(lowerTerm, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // Find the start of the line containing the match
                var lineStart = content.LastIndexOf('\n', Math.Max(0, index - 1));
                if (lineStart < 0) lineStart = 0;
                else lineStart++; // Skip the newline character

                // Find a reasonable end point
                var contextStart = Math.Max(lineStart, index - 50);
                var contextEnd = Math.Min(content.Length, index + lowerTerm.Length + 150);

                // Extend to end of word if we're in the middle
                while (contextEnd < content.Length && !char.IsWhiteSpace(content[contextEnd]))
                {
                    contextEnd++;
                }

                var highlight = content.Substring(contextStart, contextEnd - contextStart);

                // Add ellipsis if truncated
                if (contextStart > lineStart) highlight = "..." + highlight;
                if (contextEnd < content.Length) highlight += "...";

                return highlight.Trim();
            }
        }

        // If no match found, return the beginning of the content
        if (content.Length <= maxLength)
        {
            return content;
        }

        return string.Concat(content.AsSpan(0, maxLength), "...");
    }
}
