// consumer-verify.SemanticKernel — exercises SharedKernel.AI.SemanticKernel exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed NuGet
// reference) driven through a real Host.CreateApplicationBuilder() -> IHost.StartAsync() composition,
// never just BuildServiceProvider().
//
// This project references ONLY SharedKernel.AI.Abstractions + SharedKernel.AI.SemanticKernel — never
// SharedKernel.AI.Qdrant (P-05), the mirror image of consumer-verify.Qdrant/Program.cs.
// IQdrantQuantizationProfileAccessor, IQdrantHybridQueryAccessor<> and IQdrantRawClientAccessor
// (declared exclusively in SharedKernel.AI.Qdrant) are not nameable anywhere in this compilation unit.
// VERIFIED as a genuine build-time fact during this Published-phase session (2026-07-24): a
// `using SharedKernel.AI.Qdrant.Raw;` plus a bare `IQdrantRawClientAccessor? field;` were appended to
// this file — with NO matching <ProjectReference> added to this project's csproj — and built with
// `dotnet build`. The compiler emitted, then the probe was reverted:
//     Program.cs(57,23): error CS0234: The type or namespace name 'Qdrant' does not exist in the
//       namespace 'SharedKernel.AI' (are you missing an assembly reference?)
// (reproduced here in English; the local build environment emits the Turkish-locale text of this same
// diagnostic ID) — a genuine compile-time error naming the exact non-portable call site, never a
// runtime GetRequiredService failure. See consumer-verify.Qdrant/Program.cs for the mirror probe
// (IKernelRawClientAccessor unnameable from that side).
//
// The third pairing named by P-05 ("Milvus-only cannot name Qdrant-/SemanticKernel-exclusive
// contracts") is structurally moot, not merely unattempted: SharedKernel.AI.Milvus does not exist on
// disk at all (S-05, Milvus.Client never shipped a stable release — see
// 10.Intelligence/state-map.md's ## Blocked) — there is no assembly, no namespace, and no third
// consumer-verify project to build that compilation closure against. Recorded here, not fabricated.
//
// Six surfaces:
//   1. AddSharedKernelSemanticKernel(...).Build() resolves IEmbeddingGenerator, ISemanticKernel and
//      ICompletionProviderDescriptor through a real IHost.StartAsync(), zero DI exceptions (P-05)
//   2. OpenAIClient resolves as a singleton — two resolutions return the same instance (P-05)
//   3. IKernelRawClientAccessor is NOT resolvable without .AllowRawClientAccess() (P-04-mirrored gate)
//   4. IKernelRawClientAccessor IS resolvable once .AllowRawClientAccess() is called
//   5. IKernelPluginAccessor (SemanticKernel-exclusive) resolves cleanly from this compilation unit —
//      the positive half of the P-05 capability-segregation proof
//   6. A missing Intelligence:SemanticKernel configuration section throws OptionsValidationException at
//      IHost.StartAsync(), naming the missing ApiKey property — not a silent default and not a
//      first-call failure (P-05)
//
// No live model endpoint is called for any surface below: OpenAIClient's constructor only wires a
// System.ClientModel pipeline transport (lazy — no eager HTTP call), AddValidatedOptions'
// ValidateOnStart() only runs DataAnnotations validation, and Kernel.CreateBuilder().Build() (backing
// IKernelPluginAccessor) performs no I/O either. Domain Invariant #5/#6 forbid ever reaching a live,
// billed model endpoint from this harness — that guard is SK.10.Tests' own opt-in, environment-
// variable-gated live-model tests' job, never the default suite or this harness.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.SemanticKernel.Extensions;
using SharedKernel.AI.SemanticKernel.Options;
using SharedKernel.AI.SemanticKernel.Plugins;
using SharedKernel.AI.SemanticKernel.Raw;

await Surface1_ResolvesWithZeroDiExceptions();
await Surface2_OpenAiClientResolvesAsSingleton();
await Surface3_RawClientAccessorNotResolvableByDefault();
await Surface4_RawClientAccessorResolvableWhenAllowed();
await Surface5_SemanticKernelExclusiveContractsResolve();
await Surface6_MissingConfigFailsAtHostStartAsync();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify.SemanticKernel PASSED");
return;

// ── Surface 1: AddSharedKernelSemanticKernel().Build() — P-05 ────────────────
static async Task Surface1_ResolvesWithZeroDiExceptions()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var embeddingGenerator = host.Services.GetRequiredService<IEmbeddingGenerator>();
    var semanticKernel = host.Services.GetRequiredService<ISemanticKernel>();
    var descriptor = host.Services.GetRequiredService<ICompletionProviderDescriptor>();

    Verify(embeddingGenerator.ModelId == "text-embedding-3-small", "IEmbeddingGenerator.ModelId matches the configured EmbeddingModelId");
    Verify(embeddingGenerator.Dimension == 1536, "IEmbeddingGenerator.Dimension matches the configured EmbeddingDimension");
    Verify(
        embeddingGenerator.GetType().Namespace!.StartsWith("SharedKernel.AI.SemanticKernel", StringComparison.Ordinal),
        "IEmbeddingGenerator resolves to a SharedKernel.AI.SemanticKernel implementation");
    Verify(semanticKernel is not null, "ISemanticKernel resolves, zero DI exceptions");
    Verify(
        descriptor.ProviderName == IntelligenceWellKnown.SemanticKernelProviderName,
        "ICompletionProviderDescriptor.ProviderName is IntelligenceWellKnown.SemanticKernelProviderName (\"semantickernel\")");
    Verify(descriptor.ContextWindowTokens == 128_000, "ICompletionProviderDescriptor.ContextWindowTokens matches the configured value");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelSemanticKernel().Build() resolves "
        + "IEmbeddingGenerator/ISemanticKernel/ICompletionProviderDescriptor, zero DI exceptions");
}

// ── Surface 2: OpenAIClient resolves as a singleton — P-05 ───────────────────
static async Task Surface2_OpenAiClientResolvesAsSingleton()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var first = host.Services.GetRequiredService<OpenAIClient>();
    var second = host.Services.GetRequiredService<OpenAIClient>();

    Verify(ReferenceEquals(first, second), "OpenAIClient resolves as a singleton — two resolutions return the same instance");

    await host.StopAsync();
    Console.WriteLine("Surface 2 PASS: OpenAIClient resolves as a singleton");
}

// ── Surface 3: IKernelRawClientAccessor NOT resolvable by default ────────────
static async Task Surface3_RawClientAccessorNotResolvableByDefault()
{
    using var host = BuildHost(configureBuilder: null); // Deliberately does NOT call .AllowRawClientAccess().
    await host.StartAsync();

    var accessor = host.Services.GetService<IKernelRawClientAccessor>();
    Verify(accessor is null, "IKernelRawClientAccessor is NOT resolvable without .AllowRawClientAccess()");

    await host.StopAsync();
    Console.WriteLine("Surface 3 PASS: IKernelRawClientAccessor stays gated off by default");
}

// ── Surface 4: IKernelRawClientAccessor resolvable once allowed ──────────────
static async Task Surface4_RawClientAccessorResolvableWhenAllowed()
{
    using var host = BuildHost(configureBuilder: b => b.AllowRawClientAccess());
    await host.StartAsync();

    var accessor = host.Services.GetRequiredService<IKernelRawClientAccessor>();
    Verify(accessor is not null, "IKernelRawClientAccessor resolves once .AllowRawClientAccess() was called");

    await host.StopAsync();
    Console.WriteLine("Surface 4 PASS: IKernelRawClientAccessor resolves once .AllowRawClientAccess() was called");
}

// ── Surface 5: SemanticKernel-exclusive contracts resolve — positive half of P-05
static async Task Surface5_SemanticKernelExclusiveContractsResolve()
{
    using var host = BuildHost(configureBuilder: null);
    await host.StartAsync();

    var pluginAccessor = host.Services.GetRequiredService<IKernelPluginAccessor>();

    Verify(pluginAccessor is not null, "IKernelPluginAccessor (SemanticKernel-exclusive) resolves, zero DI exceptions");
    Verify(
        pluginAccessor?.Kernel.GetType().Name == "Kernel",
        "IKernelPluginAccessor.Kernel is a genuine Microsoft.SemanticKernel.Kernel instance");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 5 PASS: SemanticKernel-exclusive contracts resolve from a compilation unit referencing "
        + "only Abstractions + SemanticKernel — the positive half of the capability-segregation claim");
}

// ── Surface 6: missing Intelligence:SemanticKernel fails at IHost.StartAsync() — P-05
static async Task Surface6_MissingConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit Intelligence:SemanticKernel entirely — ApiKey/ChatModelId/EmbeddingModelId are all [Required].
    builder.Services.AddSharedKernelSemanticKernel(builder.Configuration).Build();

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

    Verify(caught is not null, "missing Intelligence:SemanticKernel config throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("ApiKey", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing ApiKey property (actionable, not generic)");

    Console.WriteLine("Surface 6 PASS: missing Intelligence:SemanticKernel config fails at IHost.StartAsync() with a clear, actionable message");
}

static IHost BuildHost(Action<SemanticKernelBuilder>? configureBuilder)
{
    var hostBuilder = Host.CreateApplicationBuilder();
    hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{SemanticKernelOptions.SectionName}:ApiKey"] = "sk-consumer-verify-placeholder",
        [$"{SemanticKernelOptions.SectionName}:ChatModelId"] = "gpt-4o-mini",
        [$"{SemanticKernelOptions.SectionName}:EmbeddingModelId"] = "text-embedding-3-small",
        [$"{SemanticKernelOptions.SectionName}:EmbeddingDimension"] = "1536",
        [$"{SemanticKernelOptions.SectionName}:ContextWindowTokens"] = "128000",
    });

    var semanticKernelBuilder = hostBuilder.Services.AddSharedKernelSemanticKernel(hostBuilder.Configuration);
    configureBuilder?.Invoke(semanticKernelBuilder);
    semanticKernelBuilder.Build();

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
