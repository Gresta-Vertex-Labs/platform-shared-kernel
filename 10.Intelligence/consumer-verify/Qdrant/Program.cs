// consumer-verify.Qdrant — exercises SharedKernel.AI.Qdrant exactly as a downstream microservice
// would: real DI composition through ProjectReference (standing in for a packed NuGet reference)
// driven through a real Host.CreateApplicationBuilder() -> IHost.StartAsync() composition, never just
// BuildServiceProvider().
//
// This project references ONLY SharedKernel.AI.Abstractions + SharedKernel.AI.Qdrant — never
// SharedKernel.AI.SemanticKernel (P-05), the mirror image of consumer-verify.SemanticKernel/Program.cs.
// IKernelPluginAccessor and IKernelRawClientAccessor (declared exclusively in
// SharedKernel.AI.SemanticKernel) are not nameable anywhere in this compilation unit.
// VERIFIED as a genuine build-time fact during this Published-phase session (2026-07-24): a
// `using SharedKernel.AI.SemanticKernel.Raw;` plus a bare `IKernelRawClientAccessor? field;` were
// appended to this file — with NO matching <ProjectReference> added to this project's csproj — and
// built with `dotnet build`. The compiler emitted, then the probe was reverted:
//     Program.cs(62,23): error CS0234: The type or namespace name 'SemanticKernel' does not exist in
//       the namespace 'SharedKernel.AI' (are you missing an assembly reference?)
// (reproduced here in English; the local build environment emits the Turkish-locale text of this same
// diagnostic ID) — a genuine compile-time error naming the exact non-portable call site, never a
// runtime GetRequiredService failure. See consumer-verify.SemanticKernel/Program.cs for the mirror
// probe (IQdrantRawClientAccessor/IQdrantHybridQueryAccessor<> unnameable from that side).
//
// The third pairing named by P-05 ("Milvus-only cannot name Qdrant-/SemanticKernel-exclusive
// contracts") is structurally moot, not merely unattempted: SharedKernel.AI.Milvus does not exist on
// disk at all (S-05, Milvus.Client never shipped a stable release — see
// 10.Intelligence/state-map.md's ## Blocked) — there is no assembly, no namespace, and no third
// consumer-verify project to build that compilation closure against. Recorded here, not fabricated.
//
// Six surfaces:
//   1. AddSharedKernelQdrant(...).AddCollection<TRecord>(...).Build() resolves
//      IVectorCollection<ProductRecord>, IVectorCollectionProvisioner and IVectorProviderDescriptor
//      through a real IHost.StartAsync(), zero DI exceptions (P-04)
//   2. QdrantClient resolves as a singleton — two resolutions return the same instance (P-04)
//   3. IQdrantRawClientAccessor is NOT resolvable without .AllowRawClientAccess() (P-04)
//   4. IQdrantRawClientAccessor IS resolvable once .AllowRawClientAccess() is called (P-04)
//   5. Qdrant-exclusive contracts (IQdrantQuantizationProfileAccessor,
//      IQdrantHybridQueryAccessor<ProductRecord>) resolve cleanly from this compilation unit — the
//      positive half of the P-05 capability-segregation proof (this provider's OWN exclusive surface
//      stays reachable; only the SIBLING providers' exclusive surfaces are unreachable)
//   6. A missing Intelligence:Qdrant configuration section throws OptionsValidationException at
//      IHost.StartAsync(), naming the missing Host property — not a silent default and not a
//      first-query failure (P-05)
//
// No live Qdrant server is required for any surface below: QdrantClient's constructor only builds a
// gRPC channel (lazy — no eager connection), AddValidatedOptions' ValidateOnStart() only runs
// DataAnnotations validation, and none of the resolved services below issue any network call. That
// live-backend guard is already covered by SK.10.Tests' T-03 real-container conformance suite.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Extensions;
using SharedKernel.AI.Qdrant.Options;
using SharedKernel.AI.Qdrant.Quantization;
using SharedKernel.AI.Qdrant.Raw;
using SharedKernel.AI.Qdrant.Sparse;
using SharedKernel.Primitives.Clocks;

await Surface1_ResolvesWithZeroDiExceptions();
await Surface2_QdrantClientResolvesAsSingleton();
await Surface3_RawClientAccessorNotResolvableByDefault();
await Surface4_RawClientAccessorResolvableWhenAllowed();
await Surface5_QdrantExclusiveContractsResolve();
await Surface6_MissingConfigFailsAtHostStartAsync();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify.Qdrant PASSED");
return;

// ── Surface 1: AddSharedKernelQdrant().AddCollection().Build() — P-04 ────────
static async Task Surface1_ResolvesWithZeroDiExceptions()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var collection = host.Services.GetRequiredService<IVectorCollection<ProductRecord>>();
    var provisioner = host.Services.GetRequiredService<IVectorCollectionProvisioner>();
    var descriptor = host.Services.GetRequiredService<IVectorProviderDescriptor>();

    Verify(collection.CollectionName == "products", "IVectorCollection<ProductRecord>.CollectionName is \"products\"");
    Verify(
        collection.GetType().Namespace!.StartsWith("SharedKernel.AI.Qdrant", StringComparison.Ordinal),
        "IVectorCollection<ProductRecord> resolves to a SharedKernel.AI.Qdrant implementation");
    Verify(provisioner is not null, "IVectorCollectionProvisioner resolves, zero DI exceptions");
    Verify(
        descriptor.ProviderName == IntelligenceWellKnown.QdrantProviderName,
        "IVectorProviderDescriptor.ProviderName is IntelligenceWellKnown.QdrantProviderName (\"qdrant\")");
    Verify(descriptor.RegisteredCollections.Contains("products"), "IVectorProviderDescriptor.RegisteredCollections contains \"products\"");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelQdrant().AddCollection().Build() resolves "
        + "IVectorCollection/IVectorCollectionProvisioner/IVectorProviderDescriptor, zero DI exceptions");
}

// ── Surface 2: QdrantClient resolves as a singleton — P-04 ───────────────────
static async Task Surface2_QdrantClientResolvesAsSingleton()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var first = host.Services.GetRequiredService<QdrantClient>();
    var second = host.Services.GetRequiredService<QdrantClient>();
    var viaInterface = host.Services.GetRequiredService<IQdrantClient>();

    Verify(ReferenceEquals(first, second), "QdrantClient resolves as a singleton — two resolutions return the same instance");
    Verify(ReferenceEquals(first, viaInterface), "IQdrantClient resolves to the SAME QdrantClient singleton, never a second connection");

    await host.StopAsync();
    Console.WriteLine("Surface 2 PASS: QdrantClient resolves as a singleton");
}

// ── Surface 3: IQdrantRawClientAccessor NOT resolvable by default — P-04 ─────
static async Task Surface3_RawClientAccessorNotResolvableByDefault()
{
    using var host = BuildHost(configureBuilder: null); // Deliberately does NOT call .AllowRawClientAccess().
    await host.StartAsync();

    var accessor = host.Services.GetService<IQdrantRawClientAccessor>();
    Verify(accessor is null, "IQdrantRawClientAccessor is NOT resolvable without .AllowRawClientAccess()");

    await host.StopAsync();
    Console.WriteLine("Surface 3 PASS: IQdrantRawClientAccessor stays gated off by default");
}

// ── Surface 4: IQdrantRawClientAccessor resolvable once allowed — P-04 ───────
static async Task Surface4_RawClientAccessorResolvableWhenAllowed()
{
    using var host = BuildHost(configureBuilder: b => b.AllowRawClientAccess());
    await host.StartAsync();

    var accessor = host.Services.GetRequiredService<IQdrantRawClientAccessor>();
    Verify(accessor is not null, "IQdrantRawClientAccessor resolves once .AllowRawClientAccess() was called");

    await host.StopAsync();
    Console.WriteLine("Surface 4 PASS: IQdrantRawClientAccessor resolves once .AllowRawClientAccess() was called");
}

// ── Surface 5: Qdrant-exclusive contracts resolve — positive half of P-05 ────
static async Task Surface5_QdrantExclusiveContractsResolve()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var quantization = host.Services.GetRequiredService<IQdrantQuantizationProfileAccessor>();
    var hybrid = host.Services.GetRequiredService<IQdrantHybridQueryAccessor<ProductRecord>>();

    Verify(quantization is not null, "IQdrantQuantizationProfileAccessor (Qdrant-exclusive) resolves, zero DI exceptions");
    Verify(hybrid is not null, "IQdrantHybridQueryAccessor<ProductRecord> (Qdrant-exclusive) resolves, zero DI exceptions");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 5 PASS: Qdrant-exclusive contracts resolve from a compilation unit referencing only "
        + "Abstractions + Qdrant — the positive half of the capability-segregation claim");
}

// ── Surface 6: missing Intelligence:Qdrant fails at IHost.StartAsync() — P-05 ─
static async Task Surface6_MissingConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit Intelligence:Qdrant entirely — Host is [Required].
    builder.Services.AddSharedKernelQdrant(builder.Configuration).Build();

    using var host = builder.Build();

    OptionsValidationException? caught = null;
    try
    {
        await host.StartAsync();
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "missing Intelligence:Qdrant config throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("Host", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing Host property (actionable, not generic)");

    Console.WriteLine("Surface 6 PASS: missing Intelligence:Qdrant config fails at IHost.StartAsync() with a clear, actionable message");
}

static IHost BuildHost(Action<QdrantBuilder>? configureBuilder)
{
    var hostBuilder = Host.CreateApplicationBuilder();
    hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{QdrantOptions.SectionName}:Host"] = "localhost",
    });

    // AddSharedKernelQdrant() deliberately does NOT self-register IClock — registration is uniformly
    // the consuming host's responsibility across this platform (same precedent as ILogger<T>).
    hostBuilder.Services.AddSingleton<IClock, SystemClock>();

    var qdrantBuilder = hostBuilder.Services
        .AddSharedKernelQdrant(hostBuilder.Configuration)
        .AddCollection<ProductRecord>("products", collection => collection
            .EmbeddingModel("text-embedding-3-small", 1536)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("tenantId", VectorFieldKind.String, filterable: true)
            .TenantField("tenantId"));

    configureBuilder?.Invoke(qdrantBuilder);
    qdrantBuilder.Build();

    return hostBuilder.Build();
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  ok — {label}");
}

/// <summary>Minimal <see cref="IVectorRecord"/> used only by this verification harness.</summary>
internal sealed record ProductRecord(string Id, ReadOnlyMemory<float> Vector, string ModelId, IReadOnlyDictionary<string, VectorValue> Metadata)
    : IVectorRecord;
