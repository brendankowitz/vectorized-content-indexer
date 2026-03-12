using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Models;

namespace ZeroProximity.VectorizedContentIndexer.Search.Vector;

/// <summary>
/// Vector-based semantic search engine using AJVI index and embedding generation.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="ISearchable"/>.</typeparam>
/// <remarks>
/// <para>
/// This search engine uses dense vector embeddings for semantic similarity search.
/// Documents are converted to embedding vectors using an <see cref="IEmbeddingProvider"/>
/// and stored in a memory-mapped AJVI index for efficient retrieval.
/// </para>
/// <para>
/// Thread safety: Uses ReaderWriterLockSlim for concurrent reads and serialized writes.
/// </para>
/// <para>
/// Key features:
/// <list type="bullet">
///   <item><description>Cosine similarity scoring (via dot product of normalized vectors)</description></item>
///   <item><description>Content hash-based deduplication</description></item>
///   <item><description>Memory-mapped index for efficient search</description></item>
///   <item><description>Float16 precision for storage optimization</description></item>
///   <item><description>Document caching for fast retrieval</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class VectorSearchEngine<TDocument> : ISearchEngine<TDocument>
    where TDocument : ISearchable
{
    private readonly string _indexPath;
    private readonly IEmbeddingProvider _embedder;
    private readonly ILogger<VectorSearchEngine<TDocument>>? _logger;
    private readonly VectorPrecision _precision;

    private readonly ConcurrentDictionary<string, TDocument> _documentCache = new();
    private readonly ConcurrentDictionary<Guid, string> _vectorToDocumentMap = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);

    private AjviIndex? _index;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of <see cref="VectorSearchEngine{TDocument}"/>.
    /// </summary>
    /// <param name="indexPath">Path to the directory for the AJVI index.</param>
    /// <param name="embedder">Embedding provider for generating document vectors.</param>
    /// <param name="precision">Vector storage precision. Defaults to Float16 for optimal storage/accuracy tradeoff.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public VectorSearchEngine(
        string indexPath,
        IEmbeddingProvider embedder,
        VectorPrecision precision = VectorPrecision.Float16,
        ILogger<VectorSearchEngine<TDocument>>? logger = null)
    {
        _indexPath = indexPath ?? throw new ArgumentNullException(nameof(indexPath));
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _precision = precision;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the vector search engine, creating or opening the AJVI index.
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

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return; // Double-check after acquiring lock
            }

            var ajviPath = Path.Combine(_indexPath, "index.ajvi");

            if (File.Exists(ajviPath))
            {
                _index = AjviIndex.Open(ajviPath, readOnly: false);
                await LoadMappingsAsync(cancellationToken).ConfigureAwait(false);
                if (_logger?.IsEnabled(LogLevel.Information) == true)
                {
                    Log.OpenedIndex(_logger, ajviPath, _index.EntryCount);
                }
            }
            else
            {
                Directory.CreateDirectory(_indexPath);
                _index = AjviIndex.Create(ajviPath, _embedder.Dimensions, _precision);
                if (_logger?.IsEnabled(LogLevel.Information) == true)
                {
                    Log.CreatedIndex(_logger, ajviPath, _embedder.Dimensions);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task IndexAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureInitialized();

        var content = document.GetSearchableText();
        if (string.IsNullOrWhiteSpace(content))
        {
            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.SkippedEmptyDocument(_logger, document.Id);
            }
            return;
        }

        // Cache the document
        _documentCache[document.Id] = document;

        // Generate embedding
        var embedding = await _embedder.EmbedAsync(content, cancellationToken).ConfigureAwait(false);

        _rwLock.EnterWriteLock();
        try
        {
            // Calculate content hash for deduplication
            var contentHash = ComputeContentHash(content);

            // Skip if already indexed
            if (_index!.ContainsHash(contentHash))
            {
                if (_logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    Log.SkippedDuplicateDocument(_logger, document.Id);
                }
                return;
            }

            // Normalize the embedding vector (should already be normalized by embedder, but ensure)
            Normalize(embedding);

            // Convert timestamp to Unix milliseconds
            var timestamp = new DateTimeOffset(document.GetTimestamp()).ToUnixTimeMilliseconds();

            // Create deterministic GUID from document ID
            var documentGuid = CreateGuidFromString(document.Id);

            // Add entry to index
            _index.AddEntry(contentHash, documentGuid, 0, timestamp, embedding);

            // Map vector entry to document ID
            _vectorToDocumentMap[documentGuid] = document.Id;

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.IndexedDocument(_logger, document.Id);
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        SaveMappingsSync();
    }

    /// <inheritdoc />
    public async Task IndexManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        EnsureInitialized();

        var documentList = documents.ToList();
        if (documentList.Count == 0)
        {
            return;
        }

        // Filter documents with content and cache them
        var documentsWithContent = documentList
            .Where(d => !string.IsNullOrWhiteSpace(d.GetSearchableText()))
            .ToList();

        foreach (var doc in documentsWithContent)
        {
            _documentCache[doc.Id] = doc;
        }

        if (documentsWithContent.Count == 0)
        {
            return;
        }

        // Batch embed all document contents
        var contents = documentsWithContent.Select(d => d.GetSearchableText()).ToList();
        var embeddings = await _embedder.EmbedBatchAsync(contents, cancellationToken).ConfigureAwait(false);

        _rwLock.EnterWriteLock();
        try
        {
            for (int i = 0; i < documentsWithContent.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var document = documentsWithContent[i];
                var embedding = embeddings[i];
                var content = contents[i];

                // Calculate content hash for deduplication
                var contentHash = ComputeContentHash(content);

                // Skip if already indexed
                if (_index!.ContainsHash(contentHash))
                {
                    continue;
                }

                // Normalize
                Normalize(embedding);

                // Convert timestamp to Unix milliseconds
                var timestamp = new DateTimeOffset(document.GetTimestamp()).ToUnixTimeMilliseconds();

                // Create deterministic GUID from document ID
                var documentGuid = CreateGuidFromString(document.Id);

                // Add entry to index
                _index.AddEntry(contentHash, documentGuid, 0, timestamp, embedding);

                // Map vector entry to document ID
                _vectorToDocumentMap[documentGuid] = document.Id;
            }

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.IndexedDocuments(_logger, documentsWithContent.Count);
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        SaveMappingsSync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult<TDocument>>> SearchAsync(
        string query,
        int maxResults = 10,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (mode != SearchMode.Semantic && mode != SearchMode.Hybrid)
        {
            // For Hybrid mode, VectorSearchEngine handles the semantic part
            // but accepts the mode for interface compatibility
            if (mode == SearchMode.Lexical)
            {
                throw new NotSupportedException(
                    $"VectorSearchEngine does not support {mode} search mode. Use LuceneSearchEngine for lexical search.");
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResult<TDocument>>();
        }

        EnsureInitialized();

        // Embed the query
        var queryEmbedding = await _embedder.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        Normalize(queryEmbedding);

        // Aggregate results by document
        var documentScores = new Dictionary<string, (double Score, string DocumentId)>();

        _rwLock.EnterReadLock();
        try
        {
            // Search the index for similar vectors
            var topK = Math.Max(maxResults * 3, 50); // Get more results for better aggregation
            var searchResults = _index!.Search(queryEmbedding, topK);

            foreach (var (index, score) in searchResults)
            {
                var vectorGuid = _index.GetDocumentId(index);

                if (_vectorToDocumentMap.TryGetValue(vectorGuid, out var documentId))
                {
                    if (!documentScores.TryGetValue(documentId, out var existing) || score > existing.Score)
                    {
                        documentScores[documentId] = (score, documentId);
                    }
                }
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        // Build search results
        var results = new List<SearchResult<TDocument>>();

        foreach (var (_, (score, documentId)) in documentScores
            .OrderByDescending(kvp => kvp.Value.Score)
            .Take(maxResults))
        {
            if (_documentCache.TryGetValue(documentId, out var document))
            {
                var content = document.GetSearchableText();
                var highlight = GetHighlight(content, query);

                results.Add(new SearchResult<TDocument>(
                    Document: document,
                    Score: score,
                    Highlight: highlight
                ));
            }
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.SearchCompleted(_logger, query, results.Count);
        }

        return results;
    }

    /// <summary>
    /// Searches with context expansion for hierarchical documents.
    /// </summary>
    /// <typeparam name="TChild">The type of child documents.</typeparam>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="contextBefore">Number of child documents to include before each match.</param>
    /// <param name="contextAfter">Number of child documents to include after each match.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Search results with expanded context.</returns>
    /// <remarks>
    /// This method only works when TDocument implements <see cref="IHierarchicalDocument{TChild}"/>.
    /// </remarks>
    public async Task<IReadOnlyList<SearchResultWithContext<TDocument, TChild>>> SearchWithContextAsync<TChild>(
        string query,
        int maxResults = 10,
        int contextBefore = 0,
        int contextAfter = 0,
        CancellationToken cancellationToken = default)
        where TChild : ISearchable
    {
        if (!typeof(IHierarchicalDocument<TChild>).IsAssignableFrom(typeof(TDocument)))
        {
            throw new InvalidOperationException(
                $"SearchWithContextAsync requires TDocument to implement IHierarchicalDocument<{typeof(TChild).Name}>.");
        }

        var baseResults = await SearchAsync(query, maxResults, SearchMode.Semantic, cancellationToken)
            .ConfigureAwait(false);

        if (contextBefore == 0 && contextAfter == 0)
        {
            return baseResults.Select(r => new SearchResultWithContext<TDocument, TChild>(
                r.Document,
                r.Score,
                Array.Empty<TChild>(),
                r.Highlight,
                r.DecayFactor
            )).ToList();
        }

        var resultsWithContext = new List<SearchResultWithContext<TDocument, TChild>>();

        foreach (var result in baseResults)
        {
            if (result.Document is IHierarchicalDocument<TChild> hierarchical)
            {
                var children = hierarchical.GetChildren();

                // Find the best-matching child by scanning for query terms
                TChild? bestMatch = default;
                if (children.Count > 0)
                {
                    var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    bestMatch = children
                        .FirstOrDefault(c => queryTerms.Any(t =>
                            c.GetSearchableText().Contains(t, StringComparison.OrdinalIgnoreCase)));
                }

                IReadOnlyList<TChild> contextChildren;
                if (bestMatch != null)
                {
                    var before = contextBefore > 0
                        ? hierarchical.GetChildrenBefore(bestMatch.Id, contextBefore)
                        : [];
                    var after = contextAfter > 0
                        ? hierarchical.GetChildrenAfter(bestMatch.Id, contextAfter)
                        : [];
                    contextChildren = [.. before, bestMatch, .. after];
                }
                else
                {
                    contextChildren = children;
                }

                resultsWithContext.Add(new SearchResultWithContext<TDocument, TChild>(
                    result.Document,
                    result.Score,
                    contextChildren,
                    result.Highlight,
                    result.DecayFactor
                ));
            }
            else
            {
                resultsWithContext.Add(new SearchResultWithContext<TDocument, TChild>(
                    result.Document,
                    result.Score,
                    Array.Empty<TChild>(),
                    result.Highlight,
                    result.DecayFactor
                ));
            }
        }

        return resultsWithContext;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        EnsureInitialized();

        // Note: AJVI index doesn't support deletion - we only remove from cache
        // The orphaned vector entry will be ignored during search
        var removed = _documentCache.TryRemove(documentId, out _);

        // Also remove from mapping
        var documentGuid = CreateGuidFromString(documentId);
        _vectorToDocumentMap.TryRemove(documentGuid, out _);

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.RemovedFromCache(_logger, documentId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        EnsureInitialized();

        var count = 0;
        foreach (var documentId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_documentCache.TryRemove(documentId, out _))
            {
                count++;
            }

            var documentGuid = CreateGuidFromString(documentId);
            _vectorToDocumentMap.TryRemove(documentGuid, out _);
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.RemovedManyFromCache(_logger, count);
        }

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (_index != null)
            {
                _index.Dispose();
                _index = null;
            }

            // Delete index files
            var indexFile = Path.Combine(_indexPath, "index.ajvi");
            if (File.Exists(indexFile))
            {
                File.Delete(indexFile);
            }

            // Delete mapping files
            var mappingsFile = Path.Combine(_indexPath, "mappings.json");
            if (File.Exists(mappingsFile))
            {
                File.Delete(mappingsFile);
            }

            var cacheFile = Path.Combine(_indexPath, "documents.json");
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }

            _documentCache.Clear();
            _vectorToDocumentMap.Clear();
            _initialized = false;

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                Log.Cleared(_logger);
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        // Reinitialize
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _rwLock.EnterReadLock();
        try
        {
            return Task.FromResult((int)_index!.EntryCount);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        // AJVI index doesn't need optimization - it's append-only
        // Could implement compaction to remove orphaned entries in the future
        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Log.OptimizeNoOp(_logger);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets statistics about the vector index.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Vector index statistics.</returns>
    public Task<VectorIndexStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _rwLock.EnterReadLock();
        try
        {
            var indexFile = Path.Combine(_indexPath, "index.ajvi");
            var sizeBytes = File.Exists(indexFile) ? new FileInfo(indexFile).Length : 0;

            return Task.FromResult(new VectorIndexStats(
                EntryCount: (int)_index!.EntryCount,
                Dimensions: _index.Dimensions,
                Precision: _index.Precision,
                SizeBytes: sizeBytes,
                CachedDocuments: _documentCache.Count
            ));
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Adds a document to the cache for faster retrieval during search.
    /// </summary>
    /// <param name="document">The document to cache.</param>
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

    private static byte[] ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return SHA256.HashData(bytes);
    }

    /// <summary>
    /// Creates a deterministic GUID from a string input using SHA256.
    /// Uses first 16 bytes of SHA256 hash to create a version 8 UUID.
    /// </summary>
    private static Guid CreateGuidFromString(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // Take first 16 bytes for the GUID
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void Normalize(float[] vector)
    {
        float sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares > 0)
        {
            float magnitude = MathF.Sqrt(sumSquares);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }
    }

    private static string? GetHighlight(string? content, string query, int maxLength = 200)
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
            var lowerTerm = term.ToLowerInvariant();
            var index = lowerContent.IndexOf(lowerTerm, StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                var start = Math.Max(0, index - 50);
                var end = Math.Min(content.Length, index + lowerTerm.Length + 150);
                var highlight = content.Substring(start, end - start);

                if (start > 0)
                    highlight = string.Concat("...", highlight);
                if (end < content.Length)
                    highlight = string.Concat(highlight, "...");

                return highlight;
            }
        }

        // If no match, return beginning of content
        return content.Length > maxLength
            ? string.Concat(content.AsSpan(0, maxLength), "...")
            : content;
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _index == null)
        {
            throw new InvalidOperationException(
                "VectorSearchEngine must be initialized before use. Call InitializeAsync first.");
        }
    }

    private async Task LoadMappingsAsync(CancellationToken ct)
    {
        try
        {
            var mappingsPath = Path.Combine(_indexPath, "mappings.json");
            if (File.Exists(mappingsPath))
            {
                var json = await File.ReadAllTextAsync(mappingsPath, ct).ConfigureAwait(false);
                var mappingsData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (mappingsData != null)
                {
                    foreach (var kvp in mappingsData)
                    {
                        if (Guid.TryParse(kvp.Key, out var guid))
                        {
                            _vectorToDocumentMap[guid] = kvp.Value;
                        }
                    }
                }

                if (_logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    Log.LoadedMappings(_logger, _vectorToDocumentMap.Count);
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger?.IsEnabled(LogLevel.Warning) == true)
            {
                Log.FailedToLoadMappings(_logger, ex);
            }
        }
    }

    private void SaveMappingsSync()
    {
        try
        {
            var mappingsPath = Path.Combine(_indexPath, "mappings.json");
            var mappingsData = _vectorToDocumentMap.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => kvp.Value);
            var json = JsonSerializer.Serialize(mappingsData);
            File.WriteAllText(mappingsPath, json);

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Log.SavedMappings(_logger, _vectorToDocumentMap.Count);
            }
        }
        catch (Exception ex)
        {
            if (_logger?.IsEnabled(LogLevel.Warning) == true)
            {
                Log.FailedToSaveMappings(_logger, ex);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // Save mappings before disposing
        SaveMappingsSync();

        _rwLock.EnterWriteLock();
        try
        {
            _index?.Dispose();
            _index = null;
            _disposed = true;
        }
        finally
        {
            _rwLock.ExitWriteLock();
            _rwLock.Dispose();
        }

        _initLock.Dispose();

        GC.SuppressFinalize(this);

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// High-performance logging methods using source generators.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "VectorSearchEngine opened existing index at {Path} with {Count} entries")]
        public static partial void OpenedIndex(ILogger logger, string path, long count);

        [LoggerMessage(Level = LogLevel.Information, Message = "VectorSearchEngine created new index at {Path} with {Dimensions} dimensions")]
        public static partial void CreatedIndex(ILogger logger, string path, int dimensions);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping document {DocumentId} with empty content")]
        public static partial void SkippedEmptyDocument(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Document {DocumentId} already indexed (duplicate content hash)")]
        public static partial void SkippedDuplicateDocument(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed document {DocumentId}")]
        public static partial void IndexedDocument(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed {Count} documents")]
        public static partial void IndexedDocuments(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Semantic search for '{Query}' returned {Count} results")]
        public static partial void SearchCompleted(ILogger logger, string query, int count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Removed document {DocumentId} from cache (vector entry remains in index)")]
        public static partial void RemovedFromCache(ILogger logger, string documentId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Removed {Count} documents from cache")]
        public static partial void RemovedManyFromCache(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Cleared all data from vector index")]
        public static partial void Cleared(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "VectorSearchEngine.OptimizeAsync called (no-op for AJVI index)")]
        public static partial void OptimizeNoOp(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {Count} vector-to-document mappings")]
        public static partial void LoadedMappings(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load mappings - they will be rebuilt on index")]
        public static partial void FailedToLoadMappings(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved {Count} vector-to-document mappings")]
        public static partial void SavedMappings(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to save mappings")]
        public static partial void FailedToSaveMappings(ILogger logger, Exception ex);
    }
}

/// <summary>
/// Statistics about a vector index.
/// </summary>
/// <param name="EntryCount">Number of vector entries in the index.</param>
/// <param name="Dimensions">Vector dimensions.</param>
/// <param name="Precision">Vector storage precision.</param>
/// <param name="SizeBytes">Total size of index file in bytes.</param>
/// <param name="CachedDocuments">Number of documents in the memory cache.</param>
public record VectorIndexStats(
    int EntryCount,
    int Dimensions,
    VectorPrecision Precision,
    long SizeBytes,
    int CachedDocuments
)
{
    /// <summary>
    /// Gets the index size in megabytes.
    /// </summary>
    public double SizeMB => SizeBytes / (1024.0 * 1024.0);
}

/// <summary>
/// Search result with context from hierarchical documents.
/// </summary>
/// <typeparam name="TDocument">The parent document type.</typeparam>
/// <typeparam name="TChild">The child document type.</typeparam>
/// <param name="Document">The matched document.</param>
/// <param name="Score">The relevance score.</param>
/// <param name="ContextChildren">Child documents providing context.</param>
/// <param name="Highlight">Optional highlight snippet.</param>
/// <param name="DecayFactor">Optional temporal decay factor.</param>
public record SearchResultWithContext<TDocument, TChild>(
    TDocument Document,
    double Score,
    IReadOnlyList<TChild> ContextChildren,
    string? Highlight = null,
    double? DecayFactor = null
) where TDocument : ISearchable
  where TChild : ISearchable;
