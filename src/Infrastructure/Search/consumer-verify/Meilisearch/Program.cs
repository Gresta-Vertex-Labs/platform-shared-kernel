// consumer-verify.Meilisearch — exercises SharedKernel.Search.Meilisearch exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed NuGet
// reference — the compiled surface is identical either way) driven through a real
// Host.CreateApplicationBuilder() → IHost.StartAsync() composition, never just BuildServiceProvider().
//
// This project references ONLY SharedKernel.Search.Abstractions + SharedKernel.Search.Meilisearch —
// never SharedKernel.Search.ElasticSearch (P-06). That is itself the capability-segregation proof:
// IAnalyticsSearch<> / ICursorSearch<> (declared exclusively in SharedKernel.Search.ElasticSearch) are
// not nameable anywhere in this compilation unit — the C# compiler enforces it structurally, there is
// no [InternalsVisibleTo]/reflection back door. VERIFIED as a genuine build-time fact during this
// Published-phase session (2026-07-20): a `using SharedKernel.Search.ElasticSearch.Analytics;` plus a
// bare `IAnalyticsSearch<object>? field;` were appended to this file — with NO matching
// <ProjectReference> added to this project's csproj (the whole point: the reference genuinely does not
// exist here) — and built with `dotnet build`. The compiler emitted, then the probe was reverted:
//     Program.cs(220,31): error CS0234: The type or namespace name 'ElasticSearch' does not exist in
//       the namespace 'SharedKernel.Search' (are you missing an assembly reference?)
//     Program.cs(224,18): error CS0246: The type or namespace name 'IAnalyticsSearch<>' could not be
//       found (are you missing a using directive or an assembly reference?)
// (reproduced here in English; the local build environment emitted the Turkish-locale text of these
// same diagnostic IDs). A genuine compile-time error naming the exact non-portable call site, never a
// runtime GetRequiredService failure. The mirror-image probe — IInstantSearch<>/ITenantSearchTokenIssuer
// unnameable from consumer-verify.ElasticSearch — was run the same way with the same result; see that
// project's Program.cs header and src/Infrastructure/Search/CLAUDE.md's Published-phase changelog entry for both
// transcripts in full.
//
// Five surfaces:
//   1. AddSharedKernelMeilisearchSearch(...).AddIndex<TDoc>(...).WithTenantTokens().Build() resolves
//      ISearchIndex<TDoc>, IInstantSearch<TDoc>, ITenantSearchTokenIssuer, ISearchIndexProvisioner and
//      ISearchProviderDescriptor through a real IHost.StartAsync(), zero DI exceptions (P-04)
//   2. IMeilisearchRawClientAccessor is NOT resolvable without .AllowRawClientAccess() (P-04)
//   3. IMeilisearchRawClientAccessor IS resolvable once .AllowRawClientAccess() is called (P-04)
//   4. A missing Search:Meilisearch configuration section throws OptionsValidationException at
//      IHost.StartAsync(), naming the missing Url/ApiKey properties — not a silent default and not a
//      first-query failure (P-07)
//   5. (documented, not executed here — see consumer-verify.BothProviders) registering BOTH providers
//      against the same TDocument is a hard violation; the last unkeyed registration silently wins for
//      the neutral interfaces (P-07)

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Raw;
using SharedKernel.Search.Meilisearch.Tenancy;

await Surface1_ResolvesWithZeroDiExceptions();
await Surface2_RawClientAccessorNotResolvableByDefault();
await Surface3_RawClientAccessorResolvableWhenAllowed();
await Surface4_MissingConfigFailsAtHostStartAsync();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify.Meilisearch PASSED");
return;

// ── Surface 1: AddSharedKernelMeilisearchSearch().AddIndex().Build() — P-04 ──
static async Task Surface1_ResolvesWithZeroDiExceptions()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
        [$"{MeilisearchOptions.SectionName}:ApiKey"] = "development-master-key",
        [$"{MeilisearchOptions.SectionName}:ApiKeyUid"] = "00000000-0000-0000-0000-000000000000",
    });

    // AddSharedKernelMeilisearchSearch() deliberately does NOT self-register IClock — registration is
    // uniformly the consuming host's responsibility across this platform (same precedent as ILogger<T>).
    builder.Services.AddSingleton<IClock, SystemClock>();

    builder.Services
        .AddSharedKernelMeilisearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("products", index => index
            .PrimaryKey("documentId")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true))
        .WithTenantTokens()
        .Build();

    using var host = builder.Build();
    // Exercises the real ValidateOnStart() path — a valid config must pass cleanly, not just
    // resolve via BuildServiceProvider().
    await host.StartAsync();

    var searchIndex = host.Services.GetRequiredService<ISearchIndex<ProductDocument>>();
    var instantSearch = host.Services.GetRequiredService<IInstantSearch<ProductDocument>>();
    var tenantTokenIssuer = host.Services.GetRequiredService<ITenantSearchTokenIssuer>();
    var provisioner = host.Services.GetRequiredService<ISearchIndexProvisioner>();
    var descriptor = host.Services.GetRequiredService<ISearchProviderDescriptor>();

    Verify(searchIndex.IndexName == "products", "ISearchIndex<ProductDocument>.IndexName is \"products\"");
    Verify(
        searchIndex.GetType().Namespace!.StartsWith("SharedKernel.Search.Meilisearch", StringComparison.Ordinal),
        "ISearchIndex<ProductDocument> resolves to a SharedKernel.Search.Meilisearch implementation");
    Verify(instantSearch is not null, "IInstantSearch<ProductDocument> resolves, zero DI exceptions");
    Verify(tenantTokenIssuer is not null, "ITenantSearchTokenIssuer resolves once .WithTenantTokens() was called");
    Verify(provisioner is not null, "ISearchIndexProvisioner resolves, zero DI exceptions");
    Verify(
        descriptor.ProviderName == SearchWellKnown.MeilisearchProviderName,
        "ISearchProviderDescriptor.ProviderName is SearchWellKnown.MeilisearchProviderName (\"meilisearch\")");
    Verify(
        descriptor.RegisteredIndexes.Contains("products"),
        "ISearchProviderDescriptor.RegisteredIndexes contains \"products\"");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelMeilisearchSearch().AddIndex().WithTenantTokens().Build() resolves "
        + "ISearchIndex/IInstantSearch/ITenantSearchTokenIssuer/ISearchIndexProvisioner/ISearchProviderDescriptor, "
        + "zero DI exceptions");
}

// ── Surface 2: IMeilisearchRawClientAccessor NOT resolvable by default — P-04 ─
static async Task Surface2_RawClientAccessorNotResolvableByDefault()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
        [$"{MeilisearchOptions.SectionName}:ApiKey"] = "development-master-key",
    });

    // Deliberately does NOT call .AllowRawClientAccess().
    builder.Services
        .AddSharedKernelMeilisearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("products", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var accessor = host.Services.GetService<IMeilisearchRawClientAccessor>();
    Verify(accessor is null, "IMeilisearchRawClientAccessor is NOT resolvable without .AllowRawClientAccess()");

    await host.StopAsync();
    Console.WriteLine("Surface 2 PASS: IMeilisearchRawClientAccessor stays ungated-off by default");
}

// ── Surface 3: IMeilisearchRawClientAccessor resolvable once allowed — P-04 ──
static async Task Surface3_RawClientAccessorResolvableWhenAllowed()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
        [$"{MeilisearchOptions.SectionName}:ApiKey"] = "development-master-key",
    });

    builder.Services
        .AddSharedKernelMeilisearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("products", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .AllowRawClientAccess()
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var accessor = host.Services.GetRequiredService<IMeilisearchRawClientAccessor>();
    Verify(accessor is not null, "IMeilisearchRawClientAccessor resolves once .AllowRawClientAccess() was called");

    await host.StopAsync();
    Console.WriteLine("Surface 3 PASS: IMeilisearchRawClientAccessor resolves once .AllowRawClientAccess() was called");
}

// ── Surface 4: missing MeilisearchOptions fails at IHost.StartAsync() — P-07 ─
static async Task Surface4_MissingConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit Search:Meilisearch entirely — Url/ApiKey are both [Required].
    builder.Services.AddSharedKernelMeilisearchSearch(builder.Configuration).Build();

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

    Verify(
        caught is not null,
        "missing Search:Meilisearch config throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("Url", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing Url property (actionable, not generic)");
    Verify(
        caught.Failures.Any(f => f.Contains("ApiKey", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing ApiKey property (actionable, not generic)");

    Console.WriteLine(
        "Surface 4 PASS: missing Search:Meilisearch config fails at IHost.StartAsync() with a clear, actionable message");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  ok — {label}");
}

/// <summary>Minimal <see cref="ISearchDocument"/> used only by this verification harness.</summary>
internal sealed record ProductDocument(string DocumentId, string Name) : ISearchDocument;
