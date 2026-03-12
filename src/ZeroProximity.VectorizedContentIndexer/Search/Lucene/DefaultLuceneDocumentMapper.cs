using Lucene.Net.Documents;
using ZeroProximity.VectorizedContentIndexer.Models;

namespace ZeroProximity.VectorizedContentIndexer.Search.Lucene;

/// <summary>
/// Default implementation of <see cref="ILuceneDocumentMapper{TDocument}"/> for basic ISearchable types.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="ISearchable"/>.</typeparam>
/// <remarks>
/// <para>
/// This mapper handles:
/// <list type="bullet">
///   <item><description><see cref="ISearchable.Id"/> mapped to "Id" field (StringField, stored)</description></item>
///   <item><description><see cref="ISearchable.GetSearchableText"/> mapped to "Content" field (TextField, stored, analyzed)</description></item>
///   <item><description><see cref="ISearchable.GetTimestamp"/> mapped to "Timestamp" field (Int64Field, stored)</description></item>
///   <item><description>If <see cref="IDocument"/>, GetMetadata() values mapped to additional fields</description></item>
/// </list>
/// </para>
/// <para>
/// For custom field mapping or specialized document types, implement <see cref="ILuceneDocumentMapper{TDocument}"/> directly.
/// </para>
/// </remarks>
public sealed class DefaultLuceneDocumentMapper<TDocument> : ILuceneDocumentMapper<TDocument>
    where TDocument : ISearchable
{
    /// <summary>
    /// Field name constants for consistent indexing.
    /// </summary>
    public static class Fields
    {
        /// <summary>Document ID field name.</summary>
        public const string Id = "Id";

        /// <summary>Main searchable content field name.</summary>
        public const string Content = "Content";

        /// <summary>Timestamp field name (Unix ticks).</summary>
        public const string Timestamp = "Timestamp";
    }

    private readonly Func<Document, TDocument>? _reconstructor;

    /// <inheritdoc />
    public string IdField => Fields.Id;

    /// <inheritdoc />
    public string ContentField => Fields.Content;

    /// <inheritdoc />
    public string TimestampField => Fields.Timestamp;

    /// <summary>
    /// Creates a new instance of <see cref="DefaultLuceneDocumentMapper{TDocument}"/>.
    /// </summary>
    public DefaultLuceneDocumentMapper()
    {
    }

    /// <summary>
    /// Creates a new instance with a custom document reconstructor function.
    /// </summary>
    /// <param name="reconstructor">
    /// A function that reconstructs a domain document from a Lucene document.
    /// Used when TDocument requires custom instantiation logic.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reconstructor"/> is null.</exception>
    public DefaultLuceneDocumentMapper(Func<Document, TDocument> reconstructor)
    {
        _reconstructor = reconstructor ?? throw new ArgumentNullException(nameof(reconstructor));
    }

    /// <inheritdoc />
    public Document MapToLuceneDocument(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var doc = new Document();

        // Core fields from ISearchable
        doc.Add(new StringField(Fields.Id, document.Id, Field.Store.YES));

        var content = document.GetSearchableText();
        if (!string.IsNullOrEmpty(content))
        {
            doc.Add(new TextField(Fields.Content, content, Field.Store.YES));
        }

        var timestamp = document.GetTimestamp();
        doc.Add(new Int64Field(Fields.Timestamp, timestamp.Ticks, Field.Store.YES));

        // Additional metadata fields from IDocument
        if (document is IDocument docWithMetadata)
        {
            var metadata = docWithMetadata.GetMetadata();
            foreach (var (key, value) in metadata)
            {
                AddMetadataField(doc, key, value);
            }
        }

        return doc;
    }

    private static void AddMetadataField(Document doc, string key, object? value)
    {
        if (value == null)
        {
            return;
        }

        switch (value)
        {
            case string strValue:
                doc.Add(new StringField(key, strValue, Field.Store.YES));
                break;

            case int intValue:
                doc.Add(new Int32Field(key, intValue, Field.Store.YES));
                break;

            case long longValue:
                doc.Add(new Int64Field(key, longValue, Field.Store.YES));
                break;

            case double doubleValue:
                doc.Add(new DoubleField(key, doubleValue, Field.Store.YES));
                break;

            case float floatValue:
                doc.Add(new SingleField(key, floatValue, Field.Store.YES));
                break;

            case bool boolValue:
                doc.Add(new StringField(key, boolValue.ToString(), Field.Store.YES));
                break;

            case DateTime dateValue:
                doc.Add(new Int64Field(key, dateValue.Ticks, Field.Store.YES));
                break;

            case DateTimeOffset dateOffsetValue:
                doc.Add(new Int64Field(key, dateOffsetValue.UtcTicks, Field.Store.YES));
                break;

            case IEnumerable<string> strings:
                foreach (var s in strings)
                {
                    doc.Add(new StringField(key, s, Field.Store.YES));
                }
                break;

            default:
                // For other types, store as string
                doc.Add(new StringField(key, value.ToString() ?? string.Empty, Field.Store.YES));
                break;
        }
    }

    /// <inheritdoc />
    public TDocument MapFromLuceneDocument(Document luceneDoc)
    {
        ArgumentNullException.ThrowIfNull(luceneDoc);

        if (_reconstructor != null)
        {
            return _reconstructor(luceneDoc);
        }

        // For types without a custom reconstructor, we throw an exception
        // because we cannot generically instantiate TDocument
        throw new InvalidOperationException(
            $"Cannot reconstruct {typeof(TDocument).Name} from Lucene document without a reconstructor function. " +
            "Provide a reconstructor in the constructor or use a document cache for retrieval.");
    }

    /// <inheritdoc />
    public string? GetHighlight(string? content, string query, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(query))
        {
            return null;
        }

        // Simple highlighting: find query terms and return context
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

        // If no match, return beginning of content
        return content.Length > maxLength
            ? string.Concat(content.AsSpan(0, maxLength), "...")
            : content;
    }
}
