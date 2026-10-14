using Xunit;

// The telemetry tests listen to the process-wide SharedKernel.Reporting source and meter; running test classes in
// parallel would mix their spans and measurements.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
