// consumer-verify — scaffolded ahead of its verification content (S-14/P-362, WO-056). This project
// exists so all four 11.Communication production packages are wired into one real executable via
// ProjectReference — standing in for a packed NuGet reference, exactly as
// 17.Workflows/consumer-verify does for SharedKernel.Workflows.Temporal — proving today only that
// the four packages compile and link together into a single dependency graph with no ambiguous-type
// or version-conflict resolution failure.
//
// The full verification harness this project will eventually carry — a real
// Host.CreateApplicationBuilder() -> IHost.StartAsync() composition (never a bare
// BuildServiceProvider()) resolving all four DI entry points
// (AddSharedKernelRestCommunication/AddSharedKernelGrpcCommunication/AddSharedKernelGraphQL/
// AddK8sServiceDiscovery-or-AddStaticServiceDiscovery) cleanly, plus the two P-358
// OptionsValidationException regression proofs (a deliberately invalid RestClientOptions/
// GraphQLOptions each failing IHost.StartAsync() loudly, not silently) — is tracked separately as
// PB-07 in the Published phase of 11.Communication/state-map.md, and is explicitly gated on P-358's
// R-23/GQ-10 (the RestClientOptionsValidator/GraphQLOptionsValidator validate-at-point-of-consumption
// fix) landing first. Do not add that content here until PB-07 is dispatched — see
// 17.Workflows/consumer-verify/Program.cs for the target shape this harness will eventually take.

using SharedKernel.Communication.Grpc.Builders;
using SharedKernel.Communication.GraphQL.Options;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Communication.Rest.Builders;

// A minimal, load-bearing smoke check: touching one public type per package proves every
// ProjectReference resolves at both compile time and runtime (assembly loads, no missing-dependency
// failure) — nothing more. This is intentionally NOT a DI composition test; that is PB-07's job.
string[] linkedAssemblies =
[
    typeof(IRestCommunicationBuilder).Assembly.GetName().Name ?? "SharedKernel.Communication.Rest",
    typeof(IGrpcCommunicationBuilder).Assembly.GetName().Name ?? "SharedKernel.Communication.Grpc",
    typeof(GraphQLOptions).Assembly.GetName().Name ?? "SharedKernel.Communication.GraphQL",
    typeof(IServiceEndpointResolver).Assembly.GetName().Name ?? "SharedKernel.Communication.Internal",
];

Console.WriteLine("11.Communication consumer-verify — scaffolded (S-14/P-362, WO-056).");
Console.WriteLine("Linked assemblies: " + string.Join(", ", linkedAssemblies));
Console.WriteLine(
    "Full DI-composition + OptionsValidationException verification is tracked separately as PB-07 " +
    "(Published phase), gated on P-358's R-23/GQ-10 — not implemented in this scaffold pass.");
