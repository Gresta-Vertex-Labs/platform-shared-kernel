using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi;

/// <summary>One index provider's answer to <c>GET /ops/verify</c>.</summary>
/// <param name="Ok">Whether every index of the provider matches this build's definitions.</param>
/// <param name="Error">The error code of the mismatch, when there is one.</param>
/// <param name="Detail">What differs, as a client may see it.</param>
public sealed record IndexVerification(bool Ok, string? Error, string? Detail);

/// <summary>
/// Provisioning, seeding and diagnostics — what a deployment pipeline and an operator would call,
/// rather than what a user would.
/// </summary>
/// <remarks>
/// The provision and verify reports list per-index outcomes in their own bodies, so they show each error's message
/// through <see cref="ErrorPresentation.GetClientMessage"/> — the same text a problem response would carry: a
/// definition conflict in full, an engine outage (whose message names internal endpoints) only in Development.
/// </remarks>
public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/ops").WithTags("Operations");

        // Idempotent and additive-only. Run it on every deploy; it creates what is missing and reports
        // a conflict rather than silently rewriting an incompatible mapping or analysis chain.
        ops.MapPost("/provision", async (
            IEnumerable<ISearchIndexProvisioner> provisioners,
            IEnumerable<ISearchProviderDescriptor> descriptors,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Two providers are registered, so ISearchIndexProvisioner resolves twice. A real service
            // registers one; this sample deliberately runs both engines side by side, which is legal
            // precisely because they serve different document types.
            //
            // Each provisioner is given only ITS OWN index. Handing every definition to every
            // provisioner would create a "products" index on ElasticSearch and an "order-lines-read"
            // index on Meilisearch that nothing ever reads — and would make a conflict on one engine
            // look like a conflict on both. ISearchProviderDescriptor.RegisteredIndexes is what says
            // which indexes a given provider was actually configured for.
            var outcomes = new List<object>();
            foreach (var (provisioner, descriptor) in provisioners.Zip(descriptors))
            {
                foreach (var definition in Definitions.All.Where(d => descriptor.RegisteredIndexes.Contains(d.Name)))
                {
                    var result = await provisioner.EnsureIndexAsync(definition, ct);
                    outcomes.Add(new
                    {
                        provider = descriptor.ProviderName,
                        index = definition.Name,
                        ok = result.IsSuccess,
                        error = result.IsFailure ? result.Error.Code : null,
                        message = result.IsFailure ? ErrorPresentation.GetClientMessage(result.Error, http) : null,
                    });
                }
            }

            return TypedResults.Ok(outcomes);
        });

        // Drops every registered index so the sample can be re-provisioned from scratch — a convenience
        // for exploring it, and the manual stand-in for the staging -> bulk-load -> CutoverAsync rebuild
        // a real service performs when a mapping, synonym or stop-word list has to change. Both
        // providers refuse to change those in place, on purpose.
        ops.MapDelete("/indexes", async (
            IEnumerable<ISearchIndexProvisioner> provisioners,
            IEnumerable<ISearchProviderDescriptor> descriptors,
            CancellationToken ct) =>
        {
            var outcomes = new List<object>();
            foreach (var (provisioner, descriptor) in provisioners.Zip(descriptors))
            {
                foreach (var indexName in descriptor.RegisteredIndexes)
                {
                    var result = await provisioner.DeleteIndexAsync(indexName, ct);
                    outcomes.Add(new
                    {
                        provider = descriptor.ProviderName,
                        index = indexName,
                        ok = result.IsSuccess,
                        error = result.IsFailure ? result.Error.Code : null,
                    });
                }
            }

            return TypedResults.Ok(outcomes);
        });

        // Seeds both engines. SearchWriteConsistency.Searchable blocks until the write is visible, so
        // the endpoints below can be called immediately afterwards without a sleep. The order lines are
        // written only once the products are; the first failure is the answer.
        ops.MapPost("/seed", (
            ISearchIndex<ProductDocument> products,
            ISearchIndex<OrderLineDocument> orderLines,
            CancellationToken ct) =>
            products.IndexManyAsync(SeedData.Products, SearchWriteConsistency.Searchable, ct)
                .Bind(productWrite => orderLines.IndexManyAsync(SeedData.OrderLines, SearchWriteConsistency.Searchable, ct)
                    .Map(orderWrite => new
                    {
                        products = new { submitted = SeedData.Products.Count, succeeded = productWrite.SucceededCount },
                        orderLines = new { submitted = SeedData.OrderLines.Count, succeeded = orderWrite.SucceededCount },
                    }))
                .ToOk());

        // The deployment check: does every live index still match what this build declares? Catches the
        // quiet failure where code ships declaring a field, synonym or stop word the index was never
        // rebuilt for, so filters silently match nothing while the index looks perfectly healthy.
        // A report rather than one error: 200 when everything matches, 503 — failing a deployment gate —
        // with every provider's outcome when something does not.
        ops.MapGet("/verify", async (
            IEnumerable<ISearchIndexProvisioner> provisioners,
            HttpContext http,
            CancellationToken ct) =>
        {
            var outcomes = new List<IndexVerification>();
            foreach (var provisioner in provisioners)
            {
                var result = await provisioner.VerifyRegisteredIndexesAsync(ct);
                outcomes.Add(result.IsSuccess
                    ? new IndexVerification(Ok: true, Error: null, Detail: null)
                    : new IndexVerification(Ok: false, result.Error.Code, ErrorPresentation.GetClientMessage(result.Error, http)));
            }

            return outcomes.TrueForAll(outcome => outcome.Ok)
                ? Results.Ok(outcomes)
                : Results.Json(outcomes, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        // Readiness for one index on one named provider, as 13.ServiceDefaults' health check consumes it.
        //
        // The provider is addressed by key rather than guessed at. Iterating every registered provisioner
        // and returning the first that answers would "work" here and be wrong in principle: it would
        // report an index as healthy because some *other* engine happens to have one by the same name.
        ops.MapGet("/probe/{providerKey}/{indexName}", (
            string providerKey,
            string indexName,
            IServiceProvider services,
            CancellationToken ct) =>
            ProvisionerFor(services, providerKey)
                .Bind(provisioner => provisioner.ProbeAsync(indexName, ct))
                .ToOk());

        // Proof that search telemetry is live. Both provider packages declared an ActivitySource and a
        // Meter and never wrote to either until the pre-publish pass, while WithSearchTelemetry()
        // subscribed to both — a green dashboard with no data.
        app.MapGet("/diagnostics/telemetry", (TelemetryProbe probe) => TypedResults.Ok(new
        {
            spanCount = probe.Spans.Count,
            measurementCount = probe.Measurements.Count,
            spans = probe.Spans.TakeLast(20),
            measurements = probe.Measurements.TakeLast(20),
        })).WithTags("Operations");
    }

    /// <summary>The provisioner registered under <paramref name="providerKey"/>, or a 404 naming the key.</summary>
    private static Result<ISearchIndexProvisioner> ProvisionerFor(IServiceProvider services, string providerKey) =>
        services.GetKeyedService<ISearchIndexProvisioner>(providerKey) is { } provisioner
            ? Result<ISearchIndexProvisioner>.Success(provisioner)
            : Error.NotFound("catalog.provider_not_registered", $"No search provider is registered under '{providerKey}'.");
}

/// <summary>
/// The index definitions, rebuilt from the same declarations Program.cs registers.
/// </summary>
/// <remarks>
/// A real service would expose the definitions it registered rather than restating them — the provider
/// builders hold them internally. They are restated here only so <c>/ops/provision</c> can run without
/// a registry type this sample does not need for anything else. Keep them in step with Program.cs;
/// <c>/ops/verify</c> will report a fingerprint mismatch if they drift, which is itself a demonstration.
/// </remarks>
public static class Definitions
{
    public static IReadOnlyList<SearchIndexDefinition> All { get; } = BuildAll();

    private static IReadOnlyList<SearchIndexDefinition> BuildAll()
    {
        var products = new SearchIndexDefinitionBuilder(Catalog.ProductsIndex)
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
            .MaxTotalHits(8)
            .Synonym("rodent", "mouse")
            .Synonym("notepad", "notebook")
            .StopWords("the", "a", "an", "with", "and")
            .Build();

        var orderLines = new SearchIndexDefinitionBuilder(Catalog.OrderLinesRead)
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
            .StopWords("the", "a", "an", "with", "and")
            .Build();

        if (products.IsFailure || orderLines.IsFailure)
        {
            throw new InvalidOperationException(
                "A sample index definition is invalid: " +
                (products.IsFailure ? products.Error.Message : orderLines.Error.Message));
        }

        return [products.Value, orderLines.Value];
    }
}
