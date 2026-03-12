using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using ZeroProximity.VectorizedContentIndexer.Models;

namespace ZeroProximity.VectorizedContentIndexer.Search.Lucene;

/// <summary>
/// Lucene.NET-based lexical search engine with BM25 scoring for generic document types.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="ISearchable"/>.</typeparam>
/// <remarks>
/// <para>
/// This search engine provides full-text keyword search using the BM25 ranking algorithm,
/// which considers term frequency, inverse document frequency, and document length normalization.
/// </para>
/// <para>
/// Thread safety: Uses SemaphoreSlim for serialized writes. Reads are concurrent-safe via SearcherManager.
/// </para>
/// <para>
/// Key features:
/// <list type="bullet">
///   <item><description>BM25 similarity scoring</description></item>
///   <item><description>Standard analyzer with tokenization</description></item>
///   <item><description>Query parsing with AND default operator</description></item>
///   <item><description>Automatic phrase query fallback on parse errors</description></item>
///   <item><description>Document deduplication via ID field</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class LuceneSearchEngine<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    private readonly string _indexPath;
    private readonly ILuceneDocumentMapper<TDocument> _mapper;
    private readonly ILogger<LuceneSearchEngine<TDocument>>? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TDocument> _documentCache = new();

    private FSDirectory? _directory;
    private Analyzer? _analyzer;
    private IndexWriter? _writer;
    private SearcherManager? _searcherManager;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of <see cref="LuceneSearchEngine{TDocument}"/>.
    /// </summary>
    /// <param name="indexPath">Path to the Lucene index directory.</param>
    /// <param name="mapper">Document mapper for converting between domain and Lucene documents.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public LuceneSearchEngine(
        string indexPath,
        ILuceneDocumentMapper<TDocument> mapper,
        ILogger<LuceneSearchEngine<TDocument>>? logger = null)
    {
        _indexPath = indexPath ?? throw new ArgumentNullException(nameof(indexPath));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger;
    }

    /// <summary>
    /// Initializes the Lucene index, creating the directory and index files if needed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method is idempotent - calling it multiple times has no effect after the first call.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return; // Double-check after acquiring lock
            }

            // Create index directory if it doesn't exist
            if (!System.IO.Directory.Exists(_indexPath))
            {
                System.IO.Directory.CreateDirectory(_indexPath);
            }

            _directory = FSDirectory.Open(_indexPath);
            _analyzer = new StandardAnalyzer(LUCENE_VERSION);

            var indexConfig = new IndexWriterConfig(LUCENE_VERSION, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                // Use BM25 similarity for better ranking
                Similarity = new BM25Similarity()
            };

            _writer = new IndexWriter(_directory, indexConfig);
            _writer.Commit(); // Ensure index is created

            _searcherManager = new SearcherManager(_writer, true, null);
            _initialized = true;

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                Log.Initialized(_logger, _indexPath);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task IndexAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Delete any existing document with the same ID (for updates)
            var idTerm = new Term(_mapper.IdField, document.Id);
            _writer!.DeleteDocuments(idTerm);

            // Cache the document for retrieval
            _documentCache[document.Id] = document;

            // Index the document
            var luceneDoc = _mapper.MapToLuceneDocument(document);
            _writer.AddDocument(luceneDoc);

            // Commit changes and refresh searcher
            _writer.Commit();
            _searcherManager?.MaybeRefreshBlocking();

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.IndexedDocument(_logger, document.Id);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task IndexManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var count = 0;
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Delete any existing document with the same ID
                var idTerm = new Term(_mapper.IdField, document.Id);
                _writer!.DeleteDocuments(idTerm);

                // Cache the document
                _documentCache[document.Id] = document;

                // Index the document
                var luceneDoc = _mapper.MapToLuceneDocument(document);
                _writer.AddDocument(luceneDoc);
                count++;
            }

            // Commit changes and refresh searcher
            _writer!.Commit();
            _searcherManager?.MaybeRefreshBlocking();

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.IndexedDocuments(_logger, count);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (mode != SearchMode.Lexical && mode != SearchMode.Hybrid)
        {
            // For Hybrid mode, LuceneSearchEngine only handles the lexical part
            // but accepts the mode for interface compatibility
            if (mode == SearchMode.Semantic)
            {
                throw new NotSupportedException(
                    $"LuceneSearchEngine does not support {mode} search mode. Use VectorSearchEngine for semantic search.");
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchResult<TDocument>>>(Array.Empty<SearchResult<TDocument>>());
        }

        EnsureInitialized();

        _searcherManager?.MaybeRefresh();
        var searcher = _searcherManager?.Acquire();

        try
        {
            if (searcher == null)
            {
                return Task.FromResult<IReadOnlyList<SearchResult<TDocument>>>(Array.Empty<SearchResult<TDocument>>());
            }

            // Parse the query
            var parser = new QueryParser(LUCENE_VERSION, _mapper.ContentField, _analyzer!);
            parser.DefaultOperator = Operator.AND;

            Query luceneQuery;
            try
            {
                luceneQuery = parser.Parse(query);
            }
            catch (ParseException)
            {
                // If parsing fails, try as phrase query
                luceneQuery = parser.Parse($"\"{QueryParserBase.Escape(query)}\"");
            }

            // Execute search - get more results for deduplication
            var topDocs = searcher.Search(luceneQuery, maxResults * 3);
            var results = new List<SearchResult<TDocument>>();
            var seenIds = new HashSet<string>();

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var documentId = doc.Get(_mapper.IdField);

                // Skip duplicates
                if (!seenIds.Add(documentId))
                {
                    continue;
                }

                // Try to get document from cache first
                TDocument document;
                if (_documentCache.TryGetValue(documentId, out var cached))
                {
                    document = cached;
                }
                else
                {
                    // Reconstruct from Lucene document
                    document = _mapper.MapFromLuceneDocument(doc);
                }

                // Get highlight
                var content = doc.Get(_mapper.ContentField);
                var highlight = _mapper.GetHighlight(content, query);

                results.Add(new SearchResult<TDocument>(
                    Document: document,
                    Score: scoreDoc.Score,
                    Highlight: highlight
                ));

                // Stop if we have enough results
                if (results.Count >= maxResults)
                {
                    break;
                }
            }

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.SearchCompleted(_logger, query, results.Count);
            }

            return Task.FromResult<IReadOnlyList<SearchResult<TDocument>>>(results);
        }
        finally
        {
            if (searcher != null)
            {
                _searcherManager?.Release(searcher);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var idTerm = new Term(_mapper.IdField, documentId);
            _writer!.DeleteDocuments(idTerm);
            _writer.Commit();
            _documentCache.TryRemove(documentId, out _);
            _searcherManager?.MaybeRefresh();

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.DeletedDocument(_logger, documentId);
            }

            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var count = 0;
            foreach (var documentId in documentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var idTerm = new Term(_mapper.IdField, documentId);
                _writer!.DeleteDocuments(idTerm);
                _documentCache.TryRemove(documentId, out _);
                count++;
            }

            _writer!.Commit();
            _searcherManager?.MaybeRefresh();

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.DeletedDocuments(_logger, count);
            }

            return count;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _writer!.DeleteAll();
            _writer.Commit();
            _documentCache.Clear();
            _searcherManager?.MaybeRefresh();

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                Log.Cleared(_logger);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _writer!.NumDocs;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Force merge to a single segment for optimal read performance
            _writer!.ForceMerge(1);
            _writer.Commit();
            _searcherManager?.MaybeRefresh();

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                Log.Optimized(_logger);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Gets the index statistics.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Index statistics including document count and size.</returns>
    public async Task<LuceneIndexStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var docCount = _writer!.NumDocs;
            var maxDoc = _writer.MaxDoc;

            // Calculate directory size
            var directoryInfo = new DirectoryInfo(_indexPath);
            var sizeBytes = directoryInfo.Exists
                ? directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;

            return new LuceneIndexStats(
                DocumentCount: docCount,
                MaxDocuments: maxDoc,
                SizeBytes: sizeBytes,
                CachedDocuments: _documentCache.Count
            );
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Adds a document to the cache for faster retrieval during search.
    /// </summary>
    /// <param name="document">The document to cache.</param>
    /// <remarks>
    /// Use this method when documents are loaded from external storage and you want to
    /// avoid reconstruction from Lucene documents during search.
    /// </remarks>
    public void CacheDocument(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documentCache[document.Id] = document;
    }

    /// <summary>
    /// Adds multiple documents to the cache.
    /// </summary>
    /// <param name="documents">The documents to cache.</param>
    public void CacheDocuments(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        foreach (var document in documents)
        {
            _documentCache[document.Id] = document;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _directory == null || _writer == null || _analyzer == null)
        {
            throw new InvalidOperationException(
                "LuceneSearchEngine must be initialized before use. Call InitializeAsync first.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _searcherManager?.Dispose();
            _writer?.Dispose();
            _analyzer?.Dispose();
            _directory?.Dispose();

            _searcherManager = null;
            _writer = null;
            _analyzer = null;
            _directory = null;

            _disposed = true;
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// High-performance logging methods using source generators.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "LuceneSearchEngine initialized at {IndexPath}")]
        public static partial void Initialized(ILogger logger, string indexPath);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed document {DocumentId}")]
        public static partial void IndexedDocument(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed {Count} documents")]
        public static partial void IndexedDocuments(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Search for '{Query}' returned {Count} results")]
        public static partial void SearchCompleted(ILogger logger, string query, int count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted document {DocumentId}")]
        public static partial void DeletedDocument(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted {Count} documents")]
        public static partial void DeletedDocuments(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Cleared all documents from index")]
        public static partial void Cleared(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Optimized Lucene index (force merged to 1 segment)")]
        public static partial void Optimized(ILogger logger);
    }
}

/// <summary>
/// Statistics about a Lucene index.
/// </summary>
/// <param name="DocumentCount">Number of documents in the index.</param>
/// <param name="MaxDocuments">Maximum document ID (includes deleted documents).</param>
/// <param name="SizeBytes">Total size of index files in bytes.</param>
/// <param name="CachedDocuments">Number of documents in the memory cache.</param>
public record LuceneIndexStats(
    int DocumentCount,
    int MaxDocuments,
    long SizeBytes,
    int CachedDocuments
)
{
    /// <summary>
    /// Gets the index size in megabytes.
    /// </summary>
    public double SizeMB => SizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Gets the number of deleted documents (tombstones) in the index.
    /// </summary>
    public int DeletedDocuments => MaxDocuments - DocumentCount;
}
