using CatalogApi;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry and health endpoints, plus the search ActivitySource and Meter.
// Until the pre-publish pass this call subscribed to a source and a meter that nothing ever wrote to;
// /diagnostics/telemetry below is this sample's proof that it now receives data.
builder.AddServiceDefaults();
builder.WithSearchTelemetry();

// Neither provider self-registers IClock — registration is uniformly the host's responsibility,
// the same precedent as ILogger<T>.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<TelemetryProbe>();

// ── Meilisearch: the storefront (BFF/fast) ────────────────────────────────────────────────────────
//
// Note the two engines serve DIFFERENT document types. Registering both providers against the same
// TDocument is a hard violation — the last unkeyed registration silently wins for every neutral
// interface — and 09.Search/consumer-verify/BothProviders exists to document exactly that.
builder.Services
    .AddSharedKernelMeilisearchSearch(builder.Configuration)
    .AddIndex<ProductDocument>(Catalog.ProductsIndex, index => index
        .PrimaryKey(ProductFields.DocumentId)
        .TenantField(ProductFields.TenantId)
        .Field(ProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
        .Field(ProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
        .Field(ProductFields.Name, SearchFieldKind.Text, searchable: true)
        .Field(ProductFields.Description, SearchFieldKind.Text, searchable: true)
        .Field(ProductFields.Brand, SearchFieldKind.Keyword, searchable: true, filterable: true, facetable: true)
        .Field(ProductFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true, sortable: true)
        .Field(ProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true)
        .Field(ProductFields.InStock, SearchFieldKind.Boolean, filterable: true)
        .Field(ProductFields.Rating, SearchFieldKind.Decimal, filterable: true, sortable: true)
        .Field(ProductFields.ReleasedOn, SearchFieldKind.DateTimeOffset, filterable: true, sortable: true)
        // Deliberately low, so this sample can SHOW what a real ceiling does rather than describe it:
        // tenant-north holds 10 products, so /storefront/tenant-north/products/count crosses it and
        // Meilisearch reports a LOWER BOUND instead of a figure it cannot actually vouch for. It also
        // makes page 2 at pageSize 5 exceed the pagination ceiling, which is a real, reachable error.
        // A real storefront leaves this at the 1000 default.
        .MaxTotalHits(8)
        // Index-level text analysis — the one text-analysis surface both engines implement
        // identically. Declared here, applied when the index is CREATED, and part of its fingerprint.
        //
        // READ THE DIRECTION CAREFULLY: Synonym(term, replacements) means a query containing TERM also
        // matches REPLACEMENTS — not the reverse. So "rodent" finds the Wireless Mouse, while a search
        // for "mouse" is unaffected by this line. Getting it backwards is silent: the declaration is
        // accepted by both engines and simply never fires. Declare both directions when you want
        // symmetry; the asymmetry being visible in your own configuration is the point.
        .Synonym("rodent", "mouse")
        .Synonym("notepad", "notebook")
        .StopWords("the", "a", "an", "with", "and"))
    // Ranking rules are Meilisearch-exclusive — ElasticSearch has no ordered tie-breaker list — so
    // they live on the provider builder, and a call site that uses them takes a compile-time
    // dependency on this package.
    .WithRankingRules(
        Catalog.ProductsIndex,
        MeilisearchRankingRule.Words,
        MeilisearchRankingRule.Typo,
        MeilisearchRankingRule.Proximity,
        MeilisearchRankingRule.Attribute,
        MeilisearchRankingRule.Sort,
        MeilisearchRankingRule.Exactness,
        // In-stock products outrank out-of-stock ones at equal relevance.
        MeilisearchRankingRule.Descending(ProductFields.Rating))
    // A signed, expiring token a browser can hold, whose tenant filter the ENGINE enforces.
    .WithTenantTokens()
    .Build();

// ── ElasticSearch: the back office (analytics/heavy) ──────────────────────────────────────────────
builder.Services
    .AddSharedKernelElasticSearchSearch(builder.Configuration)
    .AddIndex<OrderLineDocument>(Catalog.OrderLinesRead, Catalog.OrderLinesWrite, index => index
        .PrimaryKey(OrderLineFields.DocumentId)
        .TenantField(OrderLineFields.TenantId)
        .Field(OrderLineFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
        .Field(OrderLineFields.TenantId, SearchFieldKind.Keyword, filterable: true)
        .Field(OrderLineFields.ProductName, SearchFieldKind.Text, searchable: true)
        .Field(OrderLineFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field(OrderLineFields.Region, SearchFieldKind.Keyword, filterable: true, facetable: true, sortable: true)
        .Field(OrderLineFields.Quantity, SearchFieldKind.Integer, filterable: true, sortable: true)
        .Field(OrderLineFields.Revenue, SearchFieldKind.Decimal, filterable: true, sortable: true)
        .Field(OrderLineFields.OrderedAt, SearchFieldKind.DateTimeOffset, filterable: true, sortable: true)
        .Synonym("rodent", "mouse")
        .StopWords("the", "a", "an", "with", "and"))
    // The completion suggester needs a completion-typed field in the mapping BEFORE any document is
    // indexed, so this is a provisioning-time declaration, not a query option. On a tenanted index the
    // tenant is registered as a category context, because the suggester ignores query filters entirely.
    .WithCompletionField<OrderLineDocument>(Catalog.OrderLinesRead, Catalog.OrderLineSuggestField)
    .Build();

// Readiness fails while an index is not addressable, so a replica is not sent traffic it cannot serve.
// Each check names the provider that actually owns its index. ISearchIndexProvisioner is non-generic,
// so in a two-engine host an unkeyed resolution returns whichever provider was registered last — and the
// check would then ask ElasticSearch about a Meilisearch index and report this service unready forever.
// A single-provider service omits providerKey entirely.
builder.Services
    .AddHealthChecks()
    .AddSearchReadinessCheck(Catalog.ProductsIndex, providerKey: SearchWellKnown.MeilisearchProviderName)
    .AddSearchReadinessCheck(Catalog.OrderLinesRead, providerKey: SearchWellKnown.ElasticSearchProviderName);

// 14.Presentation — the HTTP boundary in one call (SharedKernel:Presentation:WebApi). Every search failure is a
// Result, and every Result failure an RFC 9457 problem: search.unreachable is 503, search.timeout and
// search.write_timeout 504 — with the engine's own message, which names internal endpoints, shown only in Development.
builder.AddSharedKernelWebApi();

var app = builder.Build();

// Before any endpoint: correlation id, security headers, the exception handler, problem bodies and routing.
app.UseSharedKernelWebApi();

// The telemetry probe listens from process start, not from the first call to /diagnostics/telemetry.
// Resolving a singleton lazily means its constructor — and therefore its ActivityListener — does not
// exist until something asks for it, so every span emitted before that first request is missed. This is
// a sample affordance; a real service exports through the OpenTelemetry pipeline AddServiceDefaults()
// already wired, which is registered eagerly for exactly this reason.
_ = app.Services.GetRequiredService<TelemetryProbe>();

// Readiness stays closed until the host says it is ready. A real service opens the gate after its own
// startup work — here that would be provisioning the indexes; this sample exposes provisioning as an
// explicit /ops/provision call instead, so there is nothing to wait for.
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.MapDefaultHealthCheckEndpoints();
app.MapStorefrontEndpoints();
app.MapBackOfficeEndpoints();
app.MapOperationsEndpoints();

await app.RunAsync();

/// <summary>Exposed so the test project can drive this host with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
