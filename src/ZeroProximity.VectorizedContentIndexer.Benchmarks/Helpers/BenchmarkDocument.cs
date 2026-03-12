using Lucene.Net.Documents;
using ZeroProximity.VectorizedContentIndexer.Search.Lucene;

namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Helpers;

/// <summary>
/// Document type used for benchmarking search operations.
/// </summary>
public sealed record BenchmarkDocument : ISearchable
{
    /// <summary>
    /// Gets the unique document identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the searchable content of the document.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the timestamp when the document was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the category of the document for filtering tests.
    /// </summary>
    public string? Category { get; init; }

    /// <inheritdoc />
    public string GetSearchableText() => Content;

    /// <inheritdoc />
    public DateTime GetTimestamp() => CreatedAt;

    /// <summary>
    /// Creates a benchmark document with the specified index and content.
    /// </summary>
    /// <param name="index">The document index for generating unique IDs.</param>
    /// <param name="content">Optional custom content. If null, generates default content.</param>
    /// <param name="category">Optional category for the document.</param>
    /// <returns>A new benchmark document.</returns>
    public static BenchmarkDocument Create(int index, string? content = null, string? category = null)
    {
        return new BenchmarkDocument
        {
            Id = $"bench-doc-{index:D6}",
            Content = content ?? TestDataGenerator.GenerateDocument(index),
            CreatedAt = DateTime.UtcNow.AddMinutes(-index),
            Category = category ?? $"category-{index % 5}"
        };
    }

    /// <summary>
    /// Creates a batch of benchmark documents.
    /// </summary>
    /// <param name="count">The number of documents to create.</param>
    /// <param name="wordCount">Optional word count per document.</param>
    /// <returns>A list of benchmark documents.</returns>
    public static List<BenchmarkDocument> CreateBatch(int count, int? wordCount = null)
    {
        var documents = new List<BenchmarkDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var content = wordCount.HasValue
                ? TestDataGenerator.GenerateText(wordCount.Value)
                : TestDataGenerator.GenerateDocument(i);
            documents.Add(Create(i, content));
        }
        return documents;
    }
}

/// <summary>
/// Lucene document mapper for BenchmarkDocument.
/// </summary>
public sealed class BenchmarkDocumentMapper : ILuceneDocumentMapper<BenchmarkDocument>
{
    /// <inheritdoc />
    public string IdField => "Id";

    /// <inheritdoc />
    public string ContentField => "Content";

    /// <inheritdoc />
    public string TimestampField => "Timestamp";

    /// <inheritdoc />
    public Document MapToLuceneDocument(BenchmarkDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var doc = new Document
        {
            new StringField(IdField, document.Id, Field.Store.YES),
            new TextField(ContentField, document.Content, Field.Store.YES),
            new Int64Field(TimestampField, document.CreatedAt.Ticks, Field.Store.YES)
        };

        if (document.Category != null)
        {
            doc.Add(new StringField("Category", document.Category, Field.Store.YES));
        }

        return doc;
    }

    /// <inheritdoc />
    public BenchmarkDocument MapFromLuceneDocument(Document luceneDoc)
    {
        ArgumentNullException.ThrowIfNull(luceneDoc);

        var id = luceneDoc.Get(IdField);
        var content = luceneDoc.Get(ContentField);
        var timestampTicks = long.Parse(luceneDoc.Get(TimestampField));
        var category = luceneDoc.Get("Category");

        return new BenchmarkDocument
        {
            Id = id,
            Content = content,
            CreatedAt = new DateTime(timestampTicks, DateTimeKind.Utc),
            Category = category
        };
    }

    /// <inheritdoc />
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
