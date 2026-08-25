using Xunit;

// Every test in this assembly writes to the same static "SharedKernel.Caching" Meter, and a
// MeterListener only observes instruments it enabled. Running classes in parallel let cache
// operations from one class land while another class's listener was being set up, so
// OtelMetricsTests intermittently observed 0 measurements and failed under CI load while
// passing in isolation. The assembly runs in ~2s, so serialising it is cheap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
