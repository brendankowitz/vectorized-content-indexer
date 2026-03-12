# ZeroProximity.VectorizedContentIndexer Benchmarks

Comprehensive performance benchmarks for the VectorizedContentIndexer library using BenchmarkDotNet.

## Quick Start

```bash
# Run all benchmarks (Release mode required)
cd src/ZeroProximity.VectorizedContentIndexer.Benchmarks
dotnet run -c Release

# Run specific benchmark category
dotnet run -c Release -- --filter *Embedding*
dotnet run -c Release -- --filter *AjviIndex*
dotnet run -c Release -- --filter *Lucene*
dotnet run -c Release -- --filter *Vector*
dotnet run -c Release -- --filter *Hybrid*
dotnet run -c Release -- --filter *Throughput*

# List all available benchmarks
dotnet run -c Release -- --list flat

# Run with memory diagnostics
dotnet run -c Release -- --memory

# Run with specific job configuration
dotnet run -c Release -- --job short
```

## Benchmark Categories

### 1. Embedding Benchmarks (`EmbeddingBenchmarks.cs`)

Measures embedding generation performance:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| Single embed (ONNX) | Short/Medium/Long text | ~15ms CPU, ~2ms GPU |
| Batch embed (ONNX) | 10/50/100 texts | Linear scaling |
| Hash embed | Baseline comparison | <1ms |
| Text length scaling | 50/100/200/300/500 words | Sub-linear |

### 2. AJVI Index Benchmarks (`AjviIndexBenchmarks.cs`)

Measures vector index performance:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| Add entry | Float16/Float32 | <1ms |
| Search top-K | 1/10/100 results | ~8ms per 10K vectors |
| Index size scaling | 1K/10K/100K vectors | Linear scaling |
| Float16 vs Float32 | Precision comparison | Float16 ~1.5x faster |
| Hash lookup | Duplicate detection | O(n) scan |

### 3. Lucene Search Benchmarks (`LuceneSearchBenchmarks.cs`)

Measures BM25 lexical search performance:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| Index single | Single document | ~1-5ms |
| Index batch | 100 documents | ~50-100ms |
| Search top-K | 10/50/100 results | ~20ms (10K docs) |
| Index size scaling | 1K/10K/100K docs | Sub-linear |
| Query types | Simple/Multi-term/Phrase | Phrase slower |

### 4. Vector Search Benchmarks (`VectorSearchBenchmarks.cs`)

Measures end-to-end semantic search:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| End-to-end search | Embed + AJVI search | ~25ms |
| Result size scaling | 5/10/20/50 results | ~5% overhead per 10 |
| Query complexity | Short/Medium/Long | Embedding dominated |
| Float16 vs Float32 | Precision comparison | Float16 recommended |

### 5. Hybrid Search Benchmarks (`HybridSearchBenchmarks.cs`)

Measures combined lexical + semantic search:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| Lexical only | BM25 baseline | ~20ms |
| Semantic only | Vector baseline | ~25ms |
| Hybrid (RRF) | Combined search | ~30ms (parallel) |
| Weight configs | 0.5/0.5, 0.7/0.3, 0.3/0.7 | Similar perf |
| Parallel benefit | Sequential vs parallel | ~40% faster |

### 6. Throughput Benchmarks (`ThroughputBenchmarks.cs`)

Measures sustained system performance:

| Benchmark | Description | Expected |
|-----------|-------------|----------|
| Indexing throughput | Docs/second | ~100-500 (with embed) |
| Query throughput | Queries/second | ~30-50 QPS |
| Concurrent queries | 5/10 parallel | Linear scaling |
| Mixed read/write | Index + Search | Some contention |
| Sustained load | 50/100 queries | Stable latency |

## Expected Performance Ranges

Based on typical hardware (Intel i7/Ryzen 7, 16GB RAM, SSD):

| Operation | CPU | GPU (DirectML) |
|-----------|-----|----------------|
| Single embedding | ~15ms | ~2ms |
| Batch embed (32) | ~400ms | ~50ms |
| AJVI search (100K) | ~80ms | N/A |
| Lucene search (10K) | ~20ms | N/A |
| Hybrid search | ~30ms | ~20ms |

## Output Formats

Benchmarks export results in multiple formats:
- `BenchmarkDotNet.Artifacts/results/*.md` - GitHub Markdown
- `BenchmarkDotNet.Artifacts/results/*.html` - HTML report
- `BenchmarkDotNet.Artifacts/results/*.json` - JSON data

## Configuration

The benchmark configuration (`Program.cs`) includes:

```csharp
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddColumn(StatisticColumn.Mean)
    .AddColumn(StatisticColumn.StdDev)
    .AddColumn(StatisticColumn.Median)
    .AddColumn(BaselineRatioColumn.RatioMean)
    .AddColumn(RankColumn.Arabic)
    .AddExporter(MarkdownExporter.GitHub)
    .AddExporter(HtmlExporter.Default)
    .AddExporter(JsonExporter.Full);
```

## Memory Diagnostics

All benchmarks include `[MemoryDiagnoser]` to track:
- Allocated bytes per operation
- Gen0/Gen1/Gen2 collections

## Interpreting Results

### Key Metrics

1. **Mean**: Average execution time
2. **StdDev**: Variation in execution time (lower is better)
3. **Median**: Middle value (more robust to outliers)
4. **Ratio**: Comparison to baseline benchmark
5. **Rank**: Relative performance within category

### Baseline Comparisons

Benchmarks marked with `[Benchmark(Baseline = true)]` serve as reference points:
- `Ratio < 1.0`: Faster than baseline
- `Ratio > 1.0`: Slower than baseline

### Memory Allocation

- **Allocated**: Total bytes allocated per operation
- **Gen0/1/2**: Garbage collection counts (fewer is better)

## Running in CI/CD

For automated benchmarking in CI:

```bash
# Run with shorter iterations for CI
dotnet run -c Release -- --job short --filter *

# Export only (no console output)
dotnet run -c Release -- --job dry --exporters json
```

## Troubleshooting

### ONNX Model Not Found

If ONNX embedding benchmarks show 0ms, the model may not be available:

1. The model is extracted from embedded resources on first run
2. Check `%LOCALAPPDATA%/ZeroProximity/Models/minilm/`
3. Ensure model files exist: `model.onnx`, `vocab.txt`

### Out of Memory

For large index benchmarks (100K+), ensure:
- At least 8GB RAM available
- Temp directory has sufficient space
- Consider running subsets: `--filter *Small*`

### Inconsistent Results

For stable measurements:
- Close other applications
- Run on AC power (not battery)
- Use Release configuration
- Allow warmup iterations

## Adding New Benchmarks

1. Create a new class in `Benchmarks/` directory
2. Add `[MemoryDiagnoser]` and `[Orderer]` attributes
3. Use `[GlobalSetup]` for expensive initialization
4. Mark baseline with `[Benchmark(Baseline = true)]`
5. Use `[Params]` for parameterized tests
6. Implement `IDisposable` for cleanup

Example:

```csharp
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MyBenchmarks
{
    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() { /* ... */ }

    [Benchmark(Baseline = true)]
    public void BaselineMethod() { /* ... */ }

    [Benchmark]
    public void OptimizedMethod() { /* ... */ }
}
```
