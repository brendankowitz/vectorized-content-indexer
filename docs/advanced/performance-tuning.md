# Performance Tuning

This guide covers optimizing ZeroProximity.VectorizedContentIndexer for your specific workload, from indexing speed to search latency and memory usage.

## Table of Contents

1. [Performance Overview](#performance-overview)
2. [Indexing Optimization](#indexing-optimization)
3. [Search Optimization](#search-optimization)
4. [Memory Optimization](#memory-optimization)
5. [Disk I/O Optimization](#disk-io-optimization)
6. [Benchmarking](#benchmarking)
7. [Scaling Strategies](#scaling-strategies)

## Performance Overview

### Bottleneck Identification

Most performance issues fall into these categories:

1. **Embedding Generation** (70% of indexing time)
   - Solution: GPU acceleration, batch processing
2. **Vector Search** (80% of search time for large indexes)
   - Solution: Reduce index size, optimize precision
3. **Disk I/O** (10-20% of operations)
   - Solution: SSD, memory-mapped I/O, reduce stored fields
4. **Memory Usage** (for large indexes)
   - Solution: Float16 precision, optimize field storage

### Baseline Performance

On typical hardware (Intel i7, 16GB RAM, SSD):

**Indexing:**
- Single document: 15-20ms (CPU), 3-5ms (GPU)
- 100 documents: 1.5s (CPU), 300ms (GPU)
- 1000 documents: 15s (CPU), 3s (GPU)

**Searching:**
- Lexical (10K docs): 10-20ms
- Semantic (100K vectors): 50-100ms
- Hybrid (10K docs): 60-120ms

## Indexing Optimization

### 1. Enable GPU Acceleration

```csharp
// Automatically uses DirectML if available
var embeddings = await EmbeddingProviderFactory.CreateAsync();

// Verify GPU usage
if (embeddings is OnnxEmbeddingProvider onnx)
{
    Console.WriteLine("Using GPU acceleration");
    // 10-20x faster than CPU
}
```

**Impact:** 10-20x faster embedding generation

### 2. Batch Indexing

```csharp
// ❌ Slow: One at a time
foreach (var doc in documents)
{
    await searchEngine.IndexAsync(doc);
}

// ✅ Fast: Batch indexing
await searchEngine.IndexManyAsync(documents);
```

**Impact:** 10-20% overhead reduction

### 3. Optimize Batch Size

```csharp
// Too small: Overhead from multiple calls
const int batchSize = 10;

// Too large: Memory pressure, longer commits
const int batchSize = 10000;

// Optimal: 100-1000 documents per batch
const int batchSize = 500;

for (int i = 0; i < documents.Count; i += batchSize)
{
    var batch = documents.Skip(i).Take(batchSize);
    await searchEngine.IndexManyAsync(batch);
}
```

**Recommended batch sizes:**
- CPU embedding: 100-500 documents
- GPU embedding: 500-1000 documents
- Memory constrained: 50-100 documents

### 4. Parallel Embedding Generation

```csharp
public class ParallelIndexingService
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly ISearchEngine<Article> _searchEngine;

    public async Task IndexInParallelAsync(
        IEnumerable<Article> documents,
        int parallelism = 4)
    {
        var partitions = Partition(documents, parallelism);

        var tasks = partitions.Select(partition =>
            Task.Run(async () =>
            {
                await _searchEngine.IndexManyAsync(partition);
            }));

        await Task.WhenAll(tasks);
    }

    private IEnumerable<IEnumerable<T>> Partition<T>(
        IEnumerable<T> items,
        int partitions)
    {
        var list = items.ToList();
        var partitionSize = (int)Math.Ceiling(list.Count / (double)partitions);

        for (int i = 0; i < partitions; i++)
        {
            yield return list.Skip(i * partitionSize).Take(partitionSize);
        }
    }
}
```

**Impact:** Near-linear scaling up to CPU core count

**Warning:** Don't use with GPU embedding (single GPU session)

### 5. Reduce Stored Fields

```csharp
// ❌ Storing everything
public Document MapToLuceneDocument(Article article)
{
    var doc = new Document();
    doc.Add(new TextField("FullBodyText", article.BodyText, Field.Store.YES));  // Large!
    doc.Add(new TextField("AllComments", allComments, Field.Store.YES));  // Large!
    return doc;
}

// ✅ Store only what's needed
public Document MapToLuceneDocument(Article article)
{
    var doc = new Document();
    doc.Add(new StringField("Id", article.Id, Field.Store.YES));  // Small
    doc.Add(new TextField("Title", article.Title, Field.Store.YES));  // Small
    doc.Add(new TextField("FullBodyText", article.BodyText, Field.Store.NO));  // Indexed only
    // Retrieve full article from database using ID
    return doc;
}
```

**Impact:** 30-50% smaller index, faster indexing

### 6. Use Float16 Precision

```csharp
var vectorEngine = new VectorSearchEngine<Article>(
    indexPath: "./index/vector",
    embedder: embeddings,
    precision: VectorPrecision.Float16  // 50% storage savings
);
```

**Impact:**
- 50% smaller AJVI index
- 50% faster disk I/O
- < 1% quality degradation

### 7. Disable Decay for Bulk Indexing

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    // Disable during bulk indexing
    options.ApplyDecay = false;

    // Re-enable for production searches
    // options.ApplyDecay = true;
});
```

**Impact:** Skip decay calculations during indexing

## Search Optimization

### 1. Limit Result Count

```csharp
// ❌ Retrieving too many results
var results = await searchEngine.SearchAsync("query", maxResults: 1000);

// ✅ Limit to what you actually need
var results = await searchEngine.SearchAsync("query", maxResults: 10);
```

**Impact:** 10x faster for large result sets

### 2. Choose the Right Search Mode

```csharp
// Fastest: Lexical only (BM25)
var results = await searchEngine.SearchAsync(query, mode: SearchMode.Lexical);
// ~10-20ms for 10K documents

// Medium: Semantic only (vector)
var results = await searchEngine.SearchAsync(query, mode: SearchMode.Semantic);
// ~50-100ms for 100K vectors

// Slowest: Hybrid (both + RRF)
var results = await searchEngine.SearchAsync(query, mode: SearchMode.Hybrid);
// Sum of both + fusion overhead
```

**Use Cases:**
- **Lexical**: Searching for specific terms, code, IDs
- **Semantic**: RAG retrieval, conceptual search
- **Hybrid**: General-purpose, best quality

### 3. Optimize Index Size

```csharp
// Periodically optimize Lucene index
await searchEngine.OptimizeAsync();

// Impact:
// - Merges segments
// - Removes deleted documents
// - Faster search (fewer segments to search)
```

**Run optimization:**
- After bulk deletes
- During maintenance windows
- When search latency increases

### 4. Cache Embedding Provider

```csharp
// ✅ Singleton - create once, reuse everywhere
services.AddSingleton<IEmbeddingProvider>(sp =>
    EmbeddingProviderFactory.CreateAsync().GetAwaiter().GetResult());

// ❌ Don't create new provider per request
// Each provider loads ~100MB ONNX model into memory
```

**Impact:** 100MB memory savings per instance

### 5. Use Lexical Pre-Filtering

```csharp
// For large indexes, use Lucene to pre-filter before semantic search
public async Task<List<SearchResult<Article>>> HybridWithPrefilterAsync(
    string query,
    int maxResults = 10)
{
    // Step 1: Fast lexical pre-filter (top 100)
    var candidates = await _luceneEngine.SearchAsync(
        query,
        maxResults: 100,
        mode: SearchMode.Lexical
    );

    // Step 2: Semantic rerank (top 100 → top 10)
    var candidateIds = candidates.Select(c => c.Document.Id).ToHashSet();

    // Filter vector search to candidates
    var semanticResults = await _vectorEngine.SearchAsync(
        query,
        maxResults: 100,
        mode: SearchMode.Semantic
    );

    var reranked = semanticResults
        .Where(r => candidateIds.Contains(r.Document.Id))
        .Take(maxResults)
        .ToList();

    return reranked;
}
```

**Impact:** 10x faster than full semantic search on large indexes

### 6. Warm Up the Index

```csharp
// Perform a dummy search on application startup
public async Task WarmUpAsync()
{
    await searchEngine.SearchAsync("warmup query", maxResults: 1);
    // Loads Lucene segments and AJVI into memory
}

// In Program.cs or Startup.cs
await searchService.WarmUpAsync();
```

**Impact:** First search latency reduced by 50-90%

## Memory Optimization

### 1. Float16 vs Float32

```csharp
// Float16: 768 bytes per vector (384 dims)
precision: VectorPrecision.Float16

// Float32: 1,536 bytes per vector (384 dims)
precision: VectorPrecision.Float32

// Example: 100K vectors
// Float16: 76.8 MB
// Float32: 153.6 MB
// Savings: 76.8 MB (50%)
```

### 2. Limit Lucene Field Storage

```csharp
// Calculate storage per document
public int CalculateStorageSize(Article article)
{
    int size = 0;
    size += article.Id.Length * 2;  // StringField (UTF-16)
    size += article.Title.Length * 2;
    size += article.Body.Length * 2;  // If stored
    // ...
    return size;
}

// Limit what you store
// - Store IDs and small fields
// - Don't store large text fields
// - Retrieve from database using ID
```

### 3. Memory-Mapped I/O Tuning

```csharp
// AJVI uses memory-mapped files
// OS automatically manages paging

// Monitor memory usage
var process = Process.GetCurrentProcess();
Console.WriteLine($"Working Set: {process.WorkingSet64 / 1024 / 1024} MB");
Console.WriteLine($"Virtual Memory: {process.VirtualMemorySize64 / 1024 / 1024} MB");

// Typical values for 100K vectors (Float16):
// - Virtual Memory: ~80 MB (file size)
// - Working Set: ~20-40 MB (actually loaded pages)
```

### 4. Dispose Resources

```csharp
// Always dispose search engines
await using var searchEngine = new HybridSearcher<Article>(...);

// Or explicitly
try
{
    var searchEngine = new HybridSearcher<Article>(...);
    // Use engine
}
finally
{
    await searchEngine.DisposeAsync();
}
```

## Disk I/O Optimization

### 1. Use SSD Storage

| Storage Type | Read Latency | Write Throughput | Impact |
|--------------|--------------|------------------|--------|
| HDD (7200rpm) | 10-15ms | 100 MB/s | Slow indexing & search |
| SATA SSD | 0.1ms | 500 MB/s | Good |
| NVMe SSD | 0.02ms | 3000 MB/s | Excellent |

**Recommendation:** Use SSD for index storage

### 2. Separate Index Paths

```csharp
// Put Lucene and AJVI on different drives
var luceneEngine = new LuceneSearchEngine<Article>(
    indexPath: "D:/fast-ssd/lucene"  // SSD
);

var vectorEngine = new VectorSearchEngine<Article>(
    indexPath: "E:/slower-ssd/vector",  // Separate SSD
    embedder: embeddings
);
```

**Impact:** Parallel I/O, reduced contention

### 3. Batch Commits

```csharp
// Lucene commits after each IndexAsync by default
// For bulk indexing, batch commits:

// Disable auto-commit (future feature)
// Index many documents
// Explicit commit
await searchEngine.OptimizeAsync();  // Forces commit + optimize
```

### 4. Optimize File Placement

```
Recommended directory structure:

D:/indexes/
├── lucene/             (Frequently accessed, fast SSD)
│   ├── segments_N
│   └── *.cfs
├── vector/             (Large sequential reads, can be slower SSD)
│   └── index.ajvi
└── temp/               (Temporary files, RAM disk if possible)
```

## Benchmarking

### 1. Indexing Benchmark

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class IndexingBenchmark
{
    private ISearchEngine<Article> _searchEngine;
    private List<Article> _documents;

    [GlobalSetup]
    public async Task Setup()
    {
        var embeddings = await EmbeddingProviderFactory.CreateAsync();
        _searchEngine = new VectorSearchEngine<Article>("./bench/index", embeddings);
        _documents = GenerateTestDocuments(1000);
    }

    [Benchmark]
    public async Task IndexSingleDocument()
    {
        await _searchEngine.IndexAsync(_documents[0]);
    }

    [Benchmark]
    public async Task IndexBatch100()
    {
        await _searchEngine.IndexManyAsync(_documents.Take(100));
    }

    [Benchmark]
    public async Task IndexBatch1000()
    {
        await _searchEngine.IndexManyAsync(_documents);
    }
}

// Run: dotnet run -c Release
BenchmarkRunner.Run<IndexingBenchmark>();
```

### 2. Search Benchmark

```csharp
[MemoryDiagnoser]
public class SearchBenchmark
{
    private ISearchEngine<Article> _searchEngine;

    [GlobalSetup]
    public async Task Setup()
    {
        // Pre-index 10K documents
        var embeddings = await EmbeddingProviderFactory.CreateAsync();
        _searchEngine = new HybridSearcher<Article>(...);

        var docs = GenerateTestDocuments(10000);
        await _searchEngine.IndexManyAsync(docs);
    }

    [Benchmark]
    public async Task SearchLexical()
    {
        await _searchEngine.SearchAsync("test query", mode: SearchMode.Lexical);
    }

    [Benchmark]
    public async Task SearchSemantic()
    {
        await _searchEngine.SearchAsync("test query", mode: SearchMode.Semantic);
    }

    [Benchmark]
    public async Task SearchHybrid()
    {
        await _searchEngine.SearchAsync("test query", mode: SearchMode.Hybrid);
    }
}
```

### 3. Custom Metrics

```csharp
public class PerformanceMonitor
{
    private readonly Stopwatch _stopwatch = new();

    public async Task<(TResult result, TimeSpan duration)> MeasureAsync<TResult>(
        Func<Task<TResult>> operation)
    {
        _stopwatch.Restart();
        var result = await operation();
        _stopwatch.Stop();
        return (result, _stopwatch.Elapsed);
    }

    public async Task MonitorSearchPerformance()
    {
        var (results, duration) = await MeasureAsync(() =>
            _searchEngine.SearchAsync("query", maxResults: 10));

        Console.WriteLine($"Search completed in {duration.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Results: {results.Count}");
        Console.WriteLine($"ms per result: {duration.TotalMilliseconds / results.Count:F2}");
    }
}
```

## Scaling Strategies

### 1. Vertical Scaling (Single Machine)

**Hardware Recommendations:**

| Index Size | CPU | RAM | Storage |
|------------|-----|-----|---------|
| < 10K docs | 4 cores | 8 GB | 10 GB SSD |
| 10K - 100K | 8 cores | 16 GB | 50 GB SSD |
| 100K - 1M | 16 cores | 32 GB | 200 GB NVMe |
| > 1M | 32+ cores | 64+ GB | 500+ GB NVMe |

### 2. Index Partitioning

```csharp
public class PartitionedSearchService
{
    private readonly Dictionary<string, ISearchEngine<Article>> _partitions = new();

    public ISearchEngine<Article> GetPartition(Article article)
    {
        var partitionKey = article.Category;  // Or date range, etc.

        if (!_partitions.ContainsKey(partitionKey))
        {
            _partitions[partitionKey] = CreateEngine(partitionKey);
        }

        return _partitions[partitionKey];
    }

    public async Task<List<SearchResult<Article>>> SearchAllPartitionsAsync(
        string query,
        int maxResults = 10)
    {
        // Search all partitions in parallel
        var tasks = _partitions.Values.Select(engine =>
            engine.SearchAsync(query, maxResults: maxResults));

        var partitionResults = await Task.WhenAll(tasks);

        // Merge and re-rank
        return partitionResults
            .SelectMany(results => results)
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }
}
```

**Impact:** Near-linear scaling for independent partitions

### 3. Caching Layer

```csharp
public class CachedSearchService
{
    private readonly ISearchEngine<Article> _searchEngine;
    private readonly IMemoryCache _cache;

    public async Task<List<SearchResult<Article>>> SearchAsync(
        string query,
        int maxResults = 10)
    {
        var cacheKey = $"search:{query}:{maxResults}";

        if (_cache.TryGetValue(cacheKey, out List<SearchResult<Article>> cached))
        {
            return cached;
        }

        var results = await _searchEngine.SearchAsync(query, maxResults);

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(5));

        return results;
    }
}
```

**Impact:** 100x faster for repeated queries

**Caution:** Cache invalidation on index updates

### 4. Read Replicas

```csharp
// Copy index to multiple read-only locations
// - Primary: Read-write (indexing)
// - Replica 1, 2, N: Read-only (searching)

public class ReplicatedSearchService
{
    private readonly ISearchEngine<Article> _primary;
    private readonly List<ISearchEngine<Article>> _replicas;
    private int _nextReplica = 0;

    public async Task IndexAsync(Article article)
    {
        // Index only to primary
        await _primary.IndexAsync(article);

        // Sync to replicas (background task)
        _ = Task.Run(() => SyncReplicasAsync());
    }

    public async Task<List<SearchResult<Article>>> SearchAsync(string query)
    {
        // Load balance searches across replicas
        var replica = _replicas[_nextReplica++ % _replicas.Count];
        return await replica.SearchAsync(query);
    }
}
```

## Performance Checklist

- [ ] Enable GPU acceleration for embeddings
- [ ] Use batch indexing (100-1000 docs)
- [ ] Use Float16 precision for vectors
- [ ] Limit stored fields in Lucene
- [ ] Optimize index regularly
- [ ] Use appropriate search mode for use case
- [ ] Cache embedding provider (singleton)
- [ ] Warm up index on startup
- [ ] Use SSD for index storage
- [ ] Monitor memory usage
- [ ] Benchmark your specific workload
- [ ] Consider partitioning for > 100K documents
- [ ] Implement caching for common queries
- [ ] Profile slow operations

## See Also

- [Architecture](../architecture.md) - Performance characteristics
- [API Documentation](../api/README.md) - Configuration options
- [Benchmarks Project](../../src/ZeroProximity.VectorizedContentIndexer.Benchmarks/) - BenchmarkDotNet tests
