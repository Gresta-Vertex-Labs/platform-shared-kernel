using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Testing.Clocks;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/SearchRequest<T> types;
// alias ours explicitly, mirroring the production translator's own convention.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Index;

/// <summary>
/// T-19: pre-flight validation tests (container-free) mirroring Meilisearch's T-11 one-for-one —
/// including the deliberately-enforced restrictions ElasticSearch itself does not impose (D-24): an
/// undeclared-but-mapped filter field is still rejected with <c>FieldNotFilterable</c>, and
/// <c>Page * PageSize &gt; 1000</c> is still rejected with <c>PaginationLimitExceeded</c> even though
/// <c>index.max_result_window</c> would allow it. Each case asserts no I/O was attempted. This suite is
/// the mechanical proof of the portability tax and the place a future reviewer will look when it is
/// questioned.
/// </summary>
/// <remarks>
/// <see cref="ElasticsearchClient"/> ships no interface, so — mirroring
/// <c>MeilisearchPreflightValidationTests</c> — <see cref="ElasticSearchIndex{TDocument}"/> is
/// constructed with a <see langword="null"/> client reference: <c>SearchAsync</c> validates the
/// request via <c>ElasticSearchRequestValidator</c> before ever dereferencing <c>_client</c>, so a
/// rejection reached without a <see cref="NullReferenceException"/> is structural proof that no I/O
/// was attempted.
/// </remarks>
public sealed class ElasticSearchPreflightValidationTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static ElasticSearchIndex<TestDocument> CreateIndexWithNoIoCapableClient(SearchIndexDefinition definition) =>
        new(
            client: null!,
            definition,
            writeAlias: definition.Name,
            new ElasticSearchOptions(),
            new FakeClock(),
            NullLogger<ElasticSearchIndex<TestDocument>>.Instance);

    private static SearchIndexDefinition Definition() => new SearchIndexDefinitionBuilder("products")
        .Field("name", SearchFieldKind.Text, searchable: true)
        .Field("status", SearchFieldKind.Keyword, filterable: true)
        .Field("category", SearchFieldKind.Keyword, facetable: true)
        .Field("createdAt", SearchFieldKind.DateTimeOffset, sortable: true)
        .MaxTotalHits(100)
        .MaxFacetValues(5)
        .Build()
        .Value;

    private static SearchIndexDefinition TenantedDefinition() => new SearchIndexDefinitionBuilder("products")
        .TenantField("tenantId")
        .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
        .Build()
        .Value;

    [Fact]
    public async Task FilterOnUndeclaredButEsMappableField_ReturnsFieldNotFilterable_WithNoIoAttempted()
    {
        // "Undeclared" here means absent from THIS package's own neutral SearchIndexDefinition — a
        // field ElasticSearch itself would happily filter on if it existed in the live mapping. The
        // rejection proves the deliberately-enforced restriction: this package rejects what
        // ElasticSearch itself would accept, so a query proven legal here is guaranteed legal on
        // Meilisearch too.
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with { Filter = SearchFilter.Eq("undeclared", SearchValue.From("x")) };

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync("validation must reject before any client dereference");
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.field_not_filterable");
    }

    [Fact]
    public async Task SortOnUndeclaredField_ReturnsFieldNotSortable_WithNoIoAttempted()
    {
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with { Sort = [SearchSort.Ascending("name")] };

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.field_not_sortable");
    }

    [Fact]
    public async Task FacetOnUndeclaredField_ReturnsFieldNotFacetable_WithNoIoAttempted()
    {
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with { Facets = ["status"] };

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.field_not_facetable");
    }

    [Fact]
    public async Task PageTimesPageSizeExceedingMaxTotalHits_ReturnsPaginationLimitExceeded_EvenThoughEsWouldAllowMore()
    {
        // Definition().MaxTotalHits is 100 — far below ElasticSearch's own native 10,000
        // index.max_result_window default. This request would succeed against a real, unconfigured ES
        // index; it is rejected here anyway, by design (D-24 / the "Why MaxTotalHits defaults to 1000
        // on both providers" note in 09.Search/CLAUDE.md).
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with { Page = 6, PageSize = 20 }; // 120 > MaxTotalHits(100)

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.pagination_limit_exceeded");
    }

    [Fact]
    public async Task FacetCountOverCap_ReturnsFacetLimitExceeded_WithNoIoAttempted()
    {
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with
        {
            Facets = ["category"],
            NumericFacetStats = ["f1", "f2", "f3", "f4", "f5"],
        };

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.facet_limit_exceeded");
    }

    [Fact]
    public async Task TenantedIndex_WithTenantScopeNone_ReturnsTenantScopeMissing_WithNoIoAttempted()
    {
        var index = CreateIndexWithNoIoCapableClient(TenantedDefinition());
        var request = SearchRequest.Default;

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.tenant_scope_missing");
    }

    [Fact]
    public async Task TenantedIndex_WithTenantScopeSupplied_PassesTheTenantGuard()
    {
        var index = CreateIndexWithNoIoCapableClient(TenantedDefinition());
        var request = SearchRequest.Default;

        var act = async () => await index.SearchAsync(request, TenantScope.Of("tenant-a"));

        await act.Should().ThrowAsync<NullReferenceException>(
            "once the tenant guard passes, the executor proceeds to call the (null) client — proving " +
            "the guard, not an unrelated short-circuit, is what stopped I/O in the None case");
    }

    [Fact]
    public void ProviderDescriptor_Validate_IsPureAndZeroIo_ByConstruction()
    {
        var descriptorType = typeof(SharedKernel.Search.ElasticSearch.Diagnostics.ElasticSearchProviderDescriptor);
        var constructorParameterTypes = descriptorType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Name);

        constructorParameterTypes.Should().NotContain(
            name => name.Contains("Client", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HttpClient", StringComparison.OrdinalIgnoreCase));
    }
}
