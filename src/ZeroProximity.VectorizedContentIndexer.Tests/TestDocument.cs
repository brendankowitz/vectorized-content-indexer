using Lucene.Net.Documents;

namespace ZeroProximity.VectorizedContentIndexer.Tests;

/// <summary>
/// Simple test document implementing ISearchable for unit tests.
/// </summary>
public record TestDocument : ISearchable
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => CreatedAt;

    /// <summary>
    /// Creates a test document with generated content.
    /// </summary>
    public static TestDocument Create(int index, string? contentOverride = null)
    {
        return new TestDocument
        {
            Id = $"doc-{index}",
            Content = contentOverride ?? $"This is test document number {index} with some searchable content about topic {index % 5}.",
            CreatedAt = DateTime.UtcNow.AddMinutes(-index)
        };
    }
}

/// <summary>
/// Test document with metadata implementing IDocument for extended field mapping tests.
/// </summary>
public record TestDocumentWithMetadata : IDocument
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? Category { get; init; }
    public int Priority { get; init; }
    public bool IsActive { get; init; } = true;

    public string GetSearchableText() => Content;
    public DateTime GetTimestamp() => CreatedAt;

    public IDictionary<string, object> GetMetadata()
    {
        var metadata = new Dictionary<string, object>();

        if (Category != null)
        {
            metadata["Category"] = Category;
        }

        metadata["Priority"] = Priority;
        metadata["IsActive"] = IsActive;

        return metadata;
    }

    /// <summary>
    /// Creates a test document with metadata.
    /// </summary>
    public static TestDocumentWithMetadata Create(int index, string? category = null, int priority = 0)
    {
        return new TestDocumentWithMetadata
        {
            Id = $"doc-meta-{index}",
            Content = $"This is a categorized document number {index}.",
            CreatedAt = DateTime.UtcNow.AddMinutes(-index),
            Category = category ?? $"category-{index % 3}",
            Priority = priority,
            IsActive = index % 2 == 0
        };
    }
}

/// <summary>
/// Custom document mapper for TestDocument that supports full reconstruction.
/// </summary>
public sealed class TestDocumentMapper : ILuceneDocumentMapper<TestDocument>
{
    public string IdField => "Id";
    public string ContentField => "Content";
    public string TimestampField => "Timestamp";

    public Document MapToLuceneDocument(TestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var doc = new Document
        {
            new StringField(IdField, document.Id, Field.Store.YES),
            new TextField(ContentField, document.Content, Field.Store.YES),
            new Int64Field(TimestampField, document.CreatedAt.Ticks, Field.Store.YES)
        };

        return doc;
    }

    public TestDocument MapFromLuceneDocument(Document luceneDoc)
    {
        ArgumentNullException.ThrowIfNull(luceneDoc);

        var id = luceneDoc.Get(IdField);
        var content = luceneDoc.Get(ContentField);
        var timestampTicks = long.Parse(luceneDoc.Get(TimestampField));

        return new TestDocument
        {
            Id = id,
            Content = content,
            CreatedAt = new DateTime(timestampTicks, DateTimeKind.Utc)
        };
    }

    public string? GetHighlight(string? content, string query, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(query))
        {
            return null;
        }

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lowerContent = content.ToLowerInvariant();

        foreach (var term in queryTerms)
        {
            var lowerTerm = term.ToLowerInvariant().Trim('"', '\'');
            var index = lowerContent.IndexOf(lowerTerm, StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                var start = Math.Max(0, index - 50);
                var end = Math.Min(content.Length, index + lowerTerm.Length + 150);
                var highlight = content.Substring(start, end - start);

                if (start > 0) highlight = string.Concat("...", highlight);
                if (end < content.Length) highlight = string.Concat(highlight, "...");

                return highlight;
            }
        }

        return content.Length > maxLength
            ? string.Concat(content.AsSpan(0, maxLength), "...")
            : content;
    }
}
