// consumer-verify.BothProviders — the second half of P-07: proves, against real compiled code, that
// registering BOTH SharedKernel.Search.Meilisearch AND SharedKernel.Search.ElasticSearch against the
// SAME TDocument is a hard violation, because the neutral interfaces (ISearchIndex<TDocument>,
// ISearchIndexProvisioner, ISearchProviderDescriptor) are unkeyed — the LAST registration silently wins
// for every one of them. Neither provider's AddSharedKernelXxxSearch() offers a keyed-registration
// overload (unlike 08.Storage, whose stores are named: each AddStore(name) registers a keyed IFileStorage, so S3
// and OBS stores coexist as [FromKeyedServices("name")] IFileStorage). There is currently no
// supported way to register two search providers against the same TDocument side by side; the
// documented, supported pattern is one TDocument (and one index) per provider.
//
// THIS PROGRAM DELIBERATELY DEMONSTRATES THE VIOLATION — do not copy this composition into real
// service code.

using System.Diagnostics.Metrics;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Search.Abstractions.Telemetry;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
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
await SurfaceStreamExceptionIsASharedKernelException();
await Surface_TwoProvidersShareOneMeterAndAreSeparableByTheProviderTag();

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

    // The keyed resolution is what a two-provider host must use for the two non-generic contracts. Both
    // provider packages register them keyed by provider name as well as unkeyed, and both resolutions
    // return the same instance.
    var meilisearchDescriptor = host.Services.GetRequiredKeyedService<ISearchProviderDescriptor>(
        SearchWellKnown.MeilisearchProviderName);
    var elasticSearchDescriptor = host.Services.GetRequiredKeyedService<ISearchProviderDescriptor>(
        SearchWellKnown.ElasticSearchProviderName);

    Verify(
        meilisearchDescriptor.ProviderName == SearchWellKnown.MeilisearchProviderName
        && elasticSearchDescriptor.ProviderName == SearchWellKnown.ElasticSearchProviderName,
        "each provider's descriptor IS reachable, unambiguously, under its own provider-name key — "
        + "which is how a host that genuinely runs both engines addresses them");

    await host.StopAsync();
    Console.WriteLine(
        "Surface PASS: registering both providers against the SAME TDocument makes the last unkeyed "
        + "registration win for ISearchIndex<TDocument> — never do that; give each provider its own "
        + "document type. The two NON-GENERIC contracts (ISearchIndexProvisioner, "
        + "ISearchProviderDescriptor) have no type parameter to tell them apart, so a distinct TDocument "
        + "does NOT disambiguate them and their unkeyed resolution collapses to the last provider "
        + "registered in ANY two-provider host. Resolve those two by provider-name key instead — "
        + "GetRequiredKeyedService<T>(SearchWellKnown.MeilisearchProviderName). Readiness is unaffected: each "
        + "provider registers its own per-index SearchIndexReadinessProbe, named by provider and index. "
        + "Verified above, both ways.");
}

// ── Telemetry: both providers share one meter and stay separable by search.provider ──────────────
// Each provider creates the same two instruments on the meter IMeterFactory returns for "SharedKernel.Search", so
// the factory hands both the SAME Meter instance. Duplicate instrument names on one meter are legal; the
// search.provider tag is what tells the two series apart. Both engines are pointed at a refused port, so each call
// fails fast as search.unavailable and still records its duration.
static async Task Surface_TwoProvidersShareOneMeterAndAreSeparableByTheProviderTag()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{MeilisearchOptions.SectionName}:Url"] = "http://127.0.0.1:1",
        [$"{MeilisearchOptions.SectionName}:ApiKey"] = "development-master-key",
        [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://127.0.0.1:1",
        [$"{ElasticSearchOptions.SectionName}:ValidateEngineVersionOnStart"] = "false",
    });
    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services
        .AddSharedKernelMeilisearchSearch(builder.Configuration)
        .AddIndex<MeilisearchOnlyDocument>("meili-products", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();
    builder.Services
        .AddSharedKernelElasticSearchSearch(builder.Configuration)
        .AddIndex<ElasticSearchOnlyDocument>(
            "es-products-read", "es-products-write", index => index.Field("name", SearchFieldKind.Text, searchable: true))
        .Build();

    var meters = new List<Meter>();
    var durations = new List<(string? Provider, string? Outcome, string? ErrorType, string Instrument)>();
    using var listener = new MeterListener();
    listener.InstrumentPublished = (instrument, meterListener) =>
    {
        if (instrument.Meter.Name == SearchWellKnown.MeterName)
        {
            lock (meters)
            {
                meters.Add(instrument.Meter);
            }

            meterListener.EnableMeasurementEvents(instrument);
        }
    };
    listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
    {
        string? provider = null;
        string? outcome = null;
        string? errorType = null;
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case SearchTagKeys.Provider: provider = tag.Value as string; break;
                case WellKnownTagKeys.Outcome: outcome = tag.Value as string; break;
                case WellKnownTagKeys.ErrorType: errorType = tag.Value as string; break;
            }
        }

        lock (durations)
        {
            durations.Add((provider, outcome, errorType, instrument.Name));
        }
    });
    listener.Start();

    using var host = builder.Build();
    await host.StartAsync();
    using var scope = host.Services.CreateScope();

    var meili = await scope.ServiceProvider.GetRequiredService<ISearchIndex<MeilisearchOnlyDocument>>()
        .GetAsync("doc-1", TenantScope.Global);
    var elastic = await scope.ServiceProvider.GetRequiredService<ISearchIndex<ElasticSearchOnlyDocument>>()
        .GetAsync("doc-1", TenantScope.Global);
    await host.StopAsync();

    Verify(
        meili.IsFailure && meili.Error.Code == SearchErrorCodes.Unavailable
        && elastic.IsFailure && elastic.Error.Code == SearchErrorCodes.Unavailable,
        "the same outage is search.unavailable on both providers");
    Verify(meters.Distinct().Count() == 1, "both providers created their instruments on ONE Meter instance named SharedKernel.Search");
    Verify(
        durations.Any(d => d.Provider == SearchWellKnown.MeilisearchProviderName && d.Instrument == SearchInstrumentNames.OperationDuration
            && d.Outcome == WellKnownOutcomes.Failure && d.ErrorType == "Unavailable"),
        "the Meilisearch duration is recorded under search.provider=meilisearch with outcome failure and error.type Unavailable");
    Verify(
        durations.Any(d => d.Provider == SearchWellKnown.ElasticSearchProviderName && d.Instrument == SearchInstrumentNames.OperationDuration
            && d.Outcome == WellKnownOutcomes.Failure && d.ErrorType == "Unavailable"),
        "the ElasticSearch duration is recorded under search.provider=elasticsearch, separable from the Meilisearch series");
    Console.WriteLine("Surface PASS: two providers, one SharedKernel.Search meter, series separable by search.provider.");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  ok — {label}");
}


// ── Stream faults are SharedKernelExceptions: a consumer catches by the kernel base type ─
static Task SurfaceStreamExceptionIsASharedKernelException()
{
    Error? caught = null;
    try
    {
        throw new SearchStreamException(SearchErrors.Unavailable(SearchWellKnown.MeilisearchProviderName));
    }
    catch (SharedKernelException e) when (e is SearchStreamException)
    {
        caught = e.Error;
    }

    if (caught is null || caught.Type != ErrorType.Unavailable)
    {
        throw new InvalidOperationException("FAIL: SearchStreamException is caught as a SharedKernelException and carries its Unavailable error");
    }

    Console.WriteLine("Surface PASS: SearchStreamException is a SharedKernelException carrying its structured error");
    return Task.CompletedTask;
}

/// <summary>Minimal <see cref="ISearchDocument"/> used only by this verification harness.</summary>
internal sealed record ProductDocument(string DocumentId, string Name) : ISearchDocument;


/// <summary>A document registered with Meilisearch only, for the shared-meter surface.</summary>
internal sealed record MeilisearchOnlyDocument(string DocumentId) : ISearchDocument;

/// <summary>A document registered with ElasticSearch only, for the shared-meter surface.</summary>
internal sealed record ElasticSearchOnlyDocument(string DocumentId) : ISearchDocument;
