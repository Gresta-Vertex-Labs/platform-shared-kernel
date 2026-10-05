using BenchmarkDotNet.Running;

// Benchmark entry point — run specific benchmark types with BenchmarkRunner.Run<T>().
// Never run benchmarks via dotnet test; use this Program.cs as the benchmark harness.
// Example: BenchmarkRunner.Run<MyBenchmark>(new SharedKernel.Benchmarks.Configurations.SharedKernelBenchmarkConfig());

// No-op until benchmark types are added in Phase: Core (SK.00.Core).
Console.WriteLine("SharedKernel.Benchmarks: no benchmarks registered yet.");
