using BenchmarkDotNet.Attributes;

namespace SharedKernel.Benchmarks.Configurations;

/// <summary>
/// Shorthand attribute that applies <see cref="SharedKernelBenchmarkConfig"/> to a benchmark class.
/// Equivalent to <c>[Config(typeof(SharedKernelBenchmarkConfig))]</c>.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [SharedKernelBenchmark]
/// public class MyBenchmark
/// {
///     [Benchmark]
///     public void BenchmarkOperation() { ... }
/// }
/// </code>
/// Run via <c>BenchmarkRunner.Run&lt;MyBenchmark&gt;()</c> — never via <c>dotnet test</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class SharedKernelBenchmarkAttribute : ConfigAttribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="SharedKernelBenchmarkAttribute"/>
    /// wiring up <see cref="SharedKernelBenchmarkConfig"/>.
    /// </summary>
    public SharedKernelBenchmarkAttribute()
        : base(typeof(SharedKernelBenchmarkConfig)) { }
}
