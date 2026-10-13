using Xunit;

// The recorders listen to the "SharedKernel.Caching" meter and ActivitySource by name, so they also see the
// measurements and spans of every other test running at the same time (the ActivitySource is static, and
// every provider's IMeterFactory meter carries the same name). Serialising the assembly keeps each test's
// view its own; the assembly runs in a few seconds, so this is cheap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
