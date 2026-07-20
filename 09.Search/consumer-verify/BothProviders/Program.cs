// consumer-verify.BothProviders — the second half of P-07: proves, against real compiled code, that
// registering BOTH SharedKernel.Search.Meilisearch AND SharedKernel.Search.ElasticSearch against the
// SAME TDocument is a hard violation, because the neutral interfaces (ISearchIndex<TDocument>,
// ISearchIndexProvisioner, ISearchProviderDescriptor) are unkeyed — the LAST registration silently wins
// for every one of them. Neither provider's AddSharedKernelXxxSearch() offers a keyed-registration
// overload (unlike 08.Storage's IFileStorage/IBlobUriGenerator, which a consumer disambiguates via
// AddKeyedSingleton — see SharedKernel.Storage.Obs/README.md's worked example). There is currently no
// supported way to register two search providers against the same TDocument side by side; the
// documented, supported pattern is one TDocument (and one index) per provider.
//
// THIS PROGRAM DELIBERATELY DEMONSTRATES THE VIOLATION — do not copy this composition into real
// service code.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Options;

await Surface_BothProvidersAgainstSameTDocumentLastRegistrationWins();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify.BothProviders PASSED");
return;

static async Task Surface_BothProvidersAgainstSameTDocumentLastRegistrationWins()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
        [$"{MeilisearchOptions.SectionName}:ApiKey"] = "development-master-key",
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
        [$"{ElasticSearchOptions.SectionName}:ValidateEngineVersionOnStart"] = "false",
    });

    // AddSharedKernelXxxSearch() deliberately does NOT self-register IClock — registration is
    // uniformly the consuming host's responsibility across this platform (same precedent as ILogger<T>).
    builder.Services.AddSingleton<IClock, SystemClock>();

    // Meilisearch registered FIRST, against ProductDocument.
    builder.Services
        .AddSharedKernelMeilisearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>("products", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    // ElasticSearch registered SECOND, against the SAME ProductDocument — a hard violation. In a real
    // composition root, use distinct document types per provider (e.g. ProductSearchDocument for
    // Meilisearch vs. ProductAnalyticsDocument for ElasticSearch) instead.
    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ProductDocument>(
            "products-read", "products-write", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    using var host = builder.Build();
    await host.StartAsync();

    // ISearchIndex<TDocument> — the last (ElasticSearch) registration silently wins.
    var searchIndex = host.Services.GetRequiredService<ISearchIndex<ProductDocument>>();
    Verify(
        searchIndex.GetType().Namespace!.StartsWith("SharedKernel.Search.ElasticSearch", StringComparison.Ordinal),
        "ISearchIndex<ProductDocument> resolves to the LAST-registered provider (ElasticSearch) — "
        + "the Meilisearch registration is silently unreachable through this interface");
    Verify(
        !searchIndex.GetType().Namespace!.StartsWith("SharedKernel.Search.Meilisearch", StringComparison.Ordinal),
        "ISearchIndex<ProductDocument> does NOT resolve to the earlier-registered Meilisearch implementation");

    // ISearchIndexProvisioner / ISearchProviderDescriptor are also non-generic singletons per provider
    // — the same last-wins collision applies to them, independent of TDocument.
    var descriptor = host.Services.GetRequiredService<ISearchProviderDescriptor>();
    Verify(
        descriptor.ProviderName == SearchWellKnown.ElasticSearchProviderName,
        "ISearchProviderDescriptor also collapses to the LAST-registered provider (ElasticSearch) — "
        + "provisioning/pre-flight-validation calls silently target the wrong engine too");

    await host.StopAsync();
    Console.WriteLine(
        "Surface PASS (documenting a violation, not a supported pattern): registering both providers "
        + "against the same TDocument makes the LAST unkeyed registration win for every neutral "
        + "interface — ISearchIndex<TDocument>, ISearchIndexProvisioner, and ISearchProviderDescriptor "
        + "alike. Never do this in real composition; use a distinct TDocument (and index) per provider.");
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
