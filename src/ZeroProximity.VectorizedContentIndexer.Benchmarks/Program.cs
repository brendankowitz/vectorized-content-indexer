using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace ZeroProximity.VectorizedContentIndexer.Benchmarks;

/// <summary>
/// Entry point for performance benchmarks.
/// </summary>
/// <remarks>
/// <para>
/// Run specific benchmarks:
///   dotnet run -c Release -- --filter *Embedding*
///   dotnet run -c Release -- --filter *AjviIndex*
///   dotnet run -c Release -- --filter *Lucene*
///   dotnet run -c Release -- --filter *Vector*
///   dotnet run -c Release -- --filter *Hybrid*
///   dotnet run -c Release -- --filter *Throughput*
/// </para>
/// <para>
/// Run all benchmarks:
///   dotnet run -c Release
/// </para>
/// <para>
/// List available benchmarks:
///   dotnet run -c Release -- --list flat
/// </para>
/// <para>
/// Run with memory diagnostics:
///   dotnet run -c Release -- --memory
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>
    /// Main entry point - runs benchmarks with enhanced configuration.
    /// </summary>
    /// <param name="args">Command line arguments passed to BenchmarkDotNet.</param>
    public static void Main(string[] args)
    {
        // Configure BenchmarkDotNet with comprehensive output
        var config = ManualConfig.Create(DefaultConfig.Instance)
            // Statistics columns
            .AddColumn(StatisticColumn.Mean)
            .AddColumn(StatisticColumn.StdDev)
            .AddColumn(StatisticColumn.Median)
            .AddColumn(StatisticColumn.Min)
            .AddColumn(StatisticColumn.Max)
            // Comparison columns
            .AddColumn(BaselineRatioColumn.RatioMean)
            .AddColumn(RankColumn.Arabic)
            // Multiple export formats
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(HtmlExporter.Default)
            .AddExporter(JsonExporter.Full)
            // Allow benchmarks with non-optimized code (for development)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        // Run benchmarks from this assembly
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
