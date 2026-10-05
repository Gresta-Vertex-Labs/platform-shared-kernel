// consumer-verify.ElasticSearch — exercises SharedKernel.Search.ElasticSearch exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed NuGet
// reference) driven through a real Host.CreateApplicationBuilder() → IHost.StartAsync() composition,
// never just BuildServiceProvider().
//
// This project references ONLY SharedKernel.Search.Abstractions + SharedKernel.Search.ElasticSearch —
// never SharedKernel.Search.Meilisearch (P-06), the mirror image of
// consumer-verify.Meilisearch/Program.cs. IInstantSearch<> and ITenantSearchTokenIssuer (declared
// exclusively in SharedKernel.Search.Meilisearch) are not nameable anywhere in this compilation unit.
// VERIFIED as a genuine build-time fact during this Published-phase session (2026-07-20): a
// `using SharedKernel.Search.Meilisearch.Instant;` plus a bare `IInstantSearch<object>? field;` were
// appended to this file — with NO matching <ProjectReference> added to this project's csproj — and
// built with `dotnet build`. The compiler emitted, then the probe was reverted:
//     Program.cs(251,31): error CS0234: The type or namespace name 'Meilisearch' does not exist in the
//       namespace 'SharedKernel.Search' (are you missing an assembly reference?)
//     Program.cs(255,18): error CS0246: The type or namespace name 'IInstantSearch<>' could not be
//       found (are you missing a using directive or an assembly reference?)
// (reproduced here in English; the local build environment emitted the Turkish-locale text of these
// same diagnostic IDs) — a genuine compile-time error enumerating the exact non-portable call site,
// never a runtime GetRequiredService failure. See consumer-verify.Meilisearch/Program.cs for the mirror
// probe (IAnalyticsSearch<>/ICursorSearch<> unnameable from that side) and src/Infrastructure/Search/CLAUDE.md's
// Published-phase changelog entry for both transcripts in full.
//
// Six surfaces:
//   1. AddSharedKernelElasticSearchSearch(...).AddIndex<TDoc>(...).Build() resolves ISearchIndex<TDoc>,
//      IAnalyticsSearch<TDoc>, ICursorSearch<TDoc>, ISearchIndexProvisioner and ISearchProviderDescriptor
//      through a real IHost.StartAsync(), zero DI exceptions (P-05)
//   2. ElasticsearchClient resolves as a singleton — two resolutions return the same instance (P-05)
//   3. IElasticSearchRawClientAccessor is NOT resolvable without .AllowRawClientAccess() (P-05)
//   4. IElasticSearchRawClientAccessor IS resolvable once .AllowRawClientAccess() is called (P-05)
//   5. A missing Search:ElasticSearch configuration section throws OptionsValidationException at
//      IHost.StartAsync(), naming the missing Nodes property — not a silent default and not a
//      first-query failure (P-07)
//   6. .WithCompletionField<TDoc>(...) resolves ISuggestSearch<TDoc> — the ElasticSearch-exclusive
//      completion suggester added by the pre-publish pass
//
// Every surface here proves the DI composition shape, not live cluster connectivity (that is covered by
// the real-backend suites against a real Testcontainers Elasticsearch). This harness must never depend
// on Docker being available — which is also why the engine-version check is no longer a side effect of
// resolving the client: it is now the explicit, asynchronous VerifyElasticSearchEngineVersionAsync,
// called from a startup task or a deployment smoke test rather than fired implicitly at first resolve.

using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Raw;
using SharedKernel.Search.ElasticSearch.Suggest;

await Surface1_ResolvesWithZeroDiExceptions();
await Surface2_ElasticsearchClientResolvesAsSingleton();
await Surface3_RawClientAccessorNotResolvableByDefault();
await Surface4_RawClientAccessorResolvableWhenAllowed();
await Surface5_MissingConfigFailsAtHostStartAsync();
await Surface6_CompletionFieldResolvesSuggestSearch();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify.ElasticSearch PASSED");
return;

// ── Surface 6: .WithCompletionField() resolves ISuggestSearch<TDoc> ─
static async Task Surface6_CompletionFieldResolvesSuggestSearch()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
    });
    builder.Services.AddSingleton<IClock, SystemClock>();

    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("suggest-read", "suggest-write", index => index
            .PrimaryKey("documentId")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true))
        .WithCompletionField<ProductDocument>("suggest-read", "nameSuggest")
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var suggest = host.Services.GetRequiredService<ISuggestSearch<ProductDocument>>();
    if (suggest is null)
    {
        throw new InvalidOperationException("Surface 6 FAIL: ISuggestSearch<ProductDocument> did not resolve.");
    }

    await host.StopAsync();
    Console.WriteLine(
        "Surface 6 PASS: .WithCompletionField<TDoc>() resolves ISuggestSearch<TDoc> through a real IHost.StartAsync().");
}

// ── Surface 1: AddSharedKernelElasticSearchSearch().AddIndex().Build() — P-05 ─
static async Task Surface1_ResolvesWithZeroDiExceptions()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
    });

    // AddSharedKernelElasticSearchSearch() deliberately does NOT self-register IClock — registration is
    // uniformly the consuming host's responsibility across this platform (same precedent as ILogger<T>).
    builder.Services.AddSingleton<IClock, SystemClock>();

    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("products-read", "products-write", index => index
            .PrimaryKey("documentId")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true))
        .Build();

    using var host = builder.Build();
    // Exercises the real ValidateOnStart() path — a valid config must pass cleanly, not just
    // resolve via BuildServiceProvider().
    await host.StartAsync();

    var searchIndex = host.Services.GetRequiredService<ISearchIndex<ProductDocument>>();
    var analyticsSearch = host.Services.GetRequiredService<IAnalyticsSearch<ProductDocument>>();
    var cursorSearch = host.Services.GetRequiredService<ICursorSearch<ProductDocument>>();
    var provisioner = host.Services.GetRequiredService<ISearchIndexProvisioner>();
    var descriptor = host.Services.GetRequiredService<ISearchProviderDescriptor>();

    Verify(searchIndex.IndexName == "products-read", "ISearchIndex<ProductDocument>.IndexName is \"products-read\"");
    Verify(
        searchIndex.GetType().Namespace!.StartsWith("SharedKernel.Search.ElasticSearch", StringComparison.Ordinal),
        "ISearchIndex<ProductDocument> resolves to a SharedKernel.Search.ElasticSearch implementation");
    Verify(analyticsSearch is not null, "IAnalyticsSearch<ProductDocument> resolves, zero DI exceptions");
    Verify(cursorSearch is not null, "ICursorSearch<ProductDocument> resolves, zero DI exceptions");
    Verify(provisioner is not null, "ISearchIndexProvisioner resolves, zero DI exceptions");
    Verify(
        descriptor.ProviderName == SearchWellKnown.ElasticSearchProviderName,
        "ISearchProviderDescriptor.ProviderName is SearchWellKnown.ElasticSearchProviderName (\"elasticsearch\")");
    Verify(
        descriptor.RegisteredIndexes.Contains("products-read"),
        "ISearchProviderDescriptor.RegisteredIndexes contains \"products-read\"");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelElasticSearchSearch().AddIndex().Build() resolves "
        + "ISearchIndex/IAnalyticsSearch/ICursorSearch/ISearchIndexProvisioner/ISearchProviderDescriptor, "
        + "zero DI exceptions");
}

// ── Surface 2: ElasticsearchClient resolves as a singleton — P-05 ────────────
static async Task Surface2_ElasticsearchClientResolvesAsSingleton()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
    });

    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>(
            "products-read", "products-write", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var first = host.Services.GetRequiredService<ElasticsearchClient>();
    var second = host.Services.GetRequiredService<ElasticsearchClient>();

    Verify(
        ReferenceEquals(first, second),
        "ElasticsearchClient resolves as a singleton — two resolutions return the same instance");

    await host.StopAsync();
    Console.WriteLine("Surface 2 PASS: ElasticsearchClient resolves as a singleton");
}

// ── Surface 3: IElasticSearchRawClientAccessor NOT resolvable by default — P-05
static async Task Surface3_RawClientAccessorNotResolvableByDefault()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
    });

    // Deliberately does NOT call .AllowRawClientAccess().
    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>(
            "products-read", "products-write", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var accessor = host.Services.GetService<IElasticSearchRawClientAccessor>();
    Verify(accessor is null, "IElasticSearchRawClientAccessor is NOT resolvable without .AllowRawClientAccess()");

    await host.StopAsync();
    Console.WriteLine("Surface 3 PASS: IElasticSearchRawClientAccessor stays gated off by default");
}

// ── Surface 4: IElasticSearchRawClientAccessor resolvable once allowed — P-05 ─
static async Task Surface4_RawClientAccessorResolvableWhenAllowed()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
    });

    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>(
            "products-read", "products-write", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .AllowRawClientAccess()
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    var accessor = host.Services.GetRequiredService<IElasticSearchRawClientAccessor>();
    Verify(accessor is not null, "IElasticSearchRawClientAccessor resolves once .AllowRawClientAccess() was called");

    await host.StopAsync();
    Console.WriteLine("Surface 4 PASS: IElasticSearchRawClientAccessor resolves once .AllowRawClientAccess() was called");
}

// ── Surface 5: missing ElasticSearchOptions fails at IHost.StartAsync() — P-07
static async Task Surface5_MissingConfigFailsAtHostStartAsync()
{
    var builder = Host.CreateApplicationBuilder();
    // Deliberately omit Search:ElasticSearch entirely — Nodes is [Required][MinLength(1)].
    builder.Services.AddSharedKernelElasticSearchSearch(builder.Configuration).Build();

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
        "missing Search:ElasticSearch config throws OptionsValidationException at IHost.StartAsync() (not a silent default)");
    Verify(
        caught!.Failures.Any(f => f.Contains("Nodes", StringComparison.Ordinal)),
        "the OptionsValidationException message names the missing Nodes property (actionable, not generic)");

    Console.WriteLine(
        "Surface 5 PASS: missing Search:ElasticSearch config fails at IHost.StartAsync() with a clear, actionable message");
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
