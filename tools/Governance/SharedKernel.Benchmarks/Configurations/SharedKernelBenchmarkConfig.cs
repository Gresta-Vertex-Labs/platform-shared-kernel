using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace SharedKernel.Benchmarks.Configurations;

/// <summary>
/// Standard BenchmarkDotNet configuration for all SharedKernel micro-benchmarks.
/// </summary>
/// <remarks>
/// Features:
/// <list type="bullet">
/// <item><description>Short-run job — fastest meaningful run suitable for CI gates.</description></item>
/// <item><description><see cref="MemoryDiagnoser"/> — allocation tracking (Gen0/Gen1/Gen2 and allocated bytes).</description></item>
/// <item><description><see cref="MarkdownExporter"/> — deterministic Markdown summary for CI artifact comparison.</description></item>
/// <item><description>HardwareCounters disabled — unstable in CI containers.</description></item>
/// </list>
/// Apply this config via the <see cref="SharedKernelBenchmarkAttribute"/> attribute.
/// </remarks>
public sealed class SharedKernelBenchmarkConfig : ManualConfig
{
    /// <summary>
    /// Initializes a new instance of <see cref="SharedKernelBenchmarkConfig"/> with the
    /// SharedKernel standard job, diagnosers, and exporter wired up.
    /// </summary>
    public SharedKernelBenchmarkConfig()
    {
        // Short-run job: 1 warmup iteration + 3 target iterations — keeps CI time reasonable
        AddJob(
            Job.Default
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithId("ShortRun")
        );

        // Allocation diagnoser — tracks Gen0/1/2 collections and total allocated bytes
        AddDiagnoser(MemoryDiagnoser.Default);

        // Deterministic Markdown output for CI artifact comparison
        // GitHub flavour produces a clean table consumable by diff tooling
        AddExporter(MarkdownExporter.GitHub);

        // Deterministic column order: hide noisy columns that vary across CI machines
        HideColumns(
            Column.RatioSD,
            Column.Median,
            Column.Gen0,
            Column.Gen1,
            Column.Gen2
        );

        // HardwareCounters are explicitly not added — they are unstable in CI containers
        // (require perf_event_paranoid = 0 on Linux; not universally available on Windows CI)
    }
}
