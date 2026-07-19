using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Index;

/// <summary>
/// T-11: pre-flight validation tests (container-free) — a filter on an undeclared field returns
/// <c>FieldNotFilterable</c>; a sort returns <c>FieldNotSortable</c>; a facet returns
/// <c>FieldNotFacetable</c>; <c>Page * PageSize &gt; MaxTotalHits</c> returns
/// <c>PaginationLimitExceeded</c> naming <c>ICursorSearch</c>/<c>EnumerateAsync</c> as alternatives; a
/// facet count over the cap returns <c>FacetLimitExceeded</c>; and a <c>TenantField</c>-declaring index
/// with <c>TenantScope.None</c> returns <c>TenantScopeMissing</c>. Every case additionally asserts that
/// no I/O was attempted.
/// </summary>
/// <remarks>
/// <see cref="MeilisearchClient"/> ships no interface, so it cannot be substituted via NSubstitute for
/// a "was it called" assertion. Instead, <see cref="MeilisearchIndex{TDocument}"/> is constructed with
/// a <see langword="null"/> client reference: <c>SearchAsync</c> validates the request via
/// <c>MeilisearchRequestValidator</c> before ever dereferencing <c>_client</c>, so a rejection reached
/// without a <see cref="NullReferenceException"/> is structural proof that no I/O was attempted — the
/// mechanical equivalent of "the substituted client is never called" for a non-mockable SDK type.
/// </remarks>
public sealed class MeilisearchPreflightValidationTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static MeilisearchIndex<TestDocument> CreateIndexWithNoIoCapableClient(SearchIndexDefinition definition) =>
        new(
            client: null!,
            definition,
            new MeilisearchOptions(),
            new FakeClock(),
            NullLogger<MeilisearchIndex<TestDocument>>.Instance);

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
    public async Task FilterOnUndeclaredField_ReturnsFieldNotFilterable_WithNoIoAttempted()
    {
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
        var request = SearchRequest.Default with { Facets = ["status"] }; // status is Filterable, not Facetable

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.field_not_facetable");
    }

    [Fact]
    public async Task PageTimesPageSizeExceedingMaxTotalHits_ReturnsPaginationLimitExceeded_NamingAlternatives()
    {
        var index = CreateIndexWithNoIoCapableClient(Definition());
        var request = SearchRequest.Default with { Page = 6, PageSize = 20 }; // 120 > MaxTotalHits(100)

        var act = async () => await index.SearchAsync(request, TenantScope.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.pagination_limit_exceeded");
        result.Subject.Error.Message.Should().Contain("ICursorSearch").And.Contain("EnumerateAsync");
    }

    [Fact]
    public async Task FacetCountOverCap_ReturnsFacetLimitExceeded_WithNoIoAttempted()
    {
        var index = CreateIndexWithNoIoCapableClient(Definition());
        // MaxFacetValues is 5 on Definition(); 6 distinct facet fields requested exceeds it.
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
        // Sanity check that the guard is genuinely conditional on TenantScope.None rather than always
        // failing — reaching the null client here would throw, proving the guard is the only thing
        // stopping I/O in the prior test.
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
        // MeilisearchProviderDescriptor's constructor takes no I/O-capable dependency at all — no
        // client, no HttpClient — so Validate() cannot reach the network by construction, not merely
        // by convention.
        var descriptorType = typeof(SharedKernel.Search.Meilisearch.Diagnostics.MeilisearchProviderDescriptor);
        var constructorParameterTypes = descriptorType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Name);

        constructorParameterTypes.Should().NotContain(
            name => name.Contains("Client", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HttpClient", StringComparison.OrdinalIgnoreCase));
    }
}
