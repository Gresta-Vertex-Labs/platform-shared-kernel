// Verification: proves the packed SharedKernel.Linter package is genuinely consumable.
//
// Everything that package delivers is build-time — MSBuild props/targets and two config files —
// so the real assertions live in LinterConsumer.csproj's VerifySharedKernelLinterWiring target,
// which has already run by the time this executes. Reaching this line means the package imported
// its props and targets, pinned CSharpier to report-only, and installed both config files.
//
// Keep this file formatted: when CI sets ContinuousIntegrationBuild=true the package enforces its
// own format check on this directory, which is the end-to-end proof that enforcement works.
Console.WriteLine(
    "SharedKernel.Linter consumer verification passed: build-time wiring asserted in MSBuild."
);
