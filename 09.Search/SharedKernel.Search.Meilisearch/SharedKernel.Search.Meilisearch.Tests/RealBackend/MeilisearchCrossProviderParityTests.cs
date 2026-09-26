using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-26 (Meilisearch half): the shared cross-provider behavioural parity suite. Runs a fixed table of
/// <see cref="SearchFilter"/> cases against the SAME 15-document/two-tenant corpus
/// (<see cref="TestProductCorpus"/>) used by the ElasticSearch project's byte-identical copy, and
/// asserts each case's result set against an expected <c>DocumentId</c> set computed via LINQ directly
/// over the corpus — never a hand-typed magic number. The ElasticSearch project runs the IDENTICAL case
/// table (by construction — both files were authored from the same design) against its own real
/// container; identical corpus + identical cases + both providers independently matching the
/// LINQ-derived expectation is what proves the two filter compilers agree, without requiring a single
/// test harness that spans both sibling-isolated packages.
/// </summary>
/// <remarks>
/// Also settles this domain's two open real-container questions for the Meilisearch side (the
/// ElasticSearch project settles the same two questions for its own engine): (a) whether
/// <c>EnumerateAsync</c> is genuinely id-ordered — settled here with a DELIBERATELY out-of-id-order seed
/// (never relying on the corpus's own ascending-id declaration order, which would be indistinguishable
/// from coincidence); (b) whether a shared-index tenant-scoped facet count excludes the other tenant's
/// documents. Findings are recorded in <c>09.Search/CLAUDE.md</c>, not asserted as a locked contract
/// guarantee here — the neutral <c>ISearchIndex.EnumerateAsync</c> XML doc deliberately continues to
/// promise nothing about ordering.
/// </remarks>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchCrossProviderParityTests : IAsyncLifetime
{
    private const string IndexName = "products-parity-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchCrossProviderParityTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    private static IReadOnlyList<TestProduct> TenantACorpus => TestProductCorpus.ForTenant(TestProductCorpus.TenantA);

    public static TheoryData<string, SearchFilter, IReadOnlyList<string>> Cases()
    {
        var data = new TheoryData<string, SearchFilter, IReadOnlyList<string>>();

        // --- Inclusive vs exclusive range bounds (boundary VALUES that exactly match real data) ---
        data.Add(
            "Range_InclusiveBounds_IncludesBoundaryMatches",
            SearchFilter.Between(TestProductFields.Price, SearchValue.From(29.99), SearchValue.From(89.99), fromInclusive: true, toInclusive: true),
            Expected(p => p.Price >= 29.99 && p.Price <= 89.99));
        data.Add(
            "Range_ExclusiveBounds_ExcludesBoundaryMatches",
            SearchFilter.Between(TestProductFields.Price, SearchValue.From(29.99), SearchValue.From(89.99), fromInclusive: false, toInclusive: false),
            Expected(p => p.Price > 29.99 && p.Price < 89.99));

        // --- DateTimeOffset bounds, inclusive vs exclusive on exact-match boundary dates ---
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        data.Add(
            "DateTimeOffsetRange_Inclusive",
            SearchFilter.Between(TestProductFields.CreatedAt, SearchValue.From(from), SearchValue.From(to), fromInclusive: true, toInclusive: true),
            Expected(p => p.CreatedAt >= from && p.CreatedAt <= to));
        data.Add(
            "DateTimeOffsetRange_Exclusive",
            SearchFilter.Between(TestProductFields.CreatedAt, SearchValue.From(from), SearchValue.From(to), fromInclusive: false, toInclusive: false),
            Expected(p => p.CreatedAt > from && p.CreatedAt < to));

        // --- Empty-operand And/Or ---
        data.Add("EmptyAnd_MatchesEverything", SearchFilter.All(), Expected(_ => true));

        // --- In with a single value ---
        data.Add(
            "In_SingleValue",
            SearchFilter.In(TestProductFields.Category, SearchValue.From("electronics")),
            Expected(p => p.Category == "electronics"));

        // --- Not nesting and precedence ---
        data.Add(
            "Not_SimpleNegation",
            SearchFilter.Negate(SearchFilter.Eq(TestProductFields.Status, SearchValue.From("discontinued"))),
            Expected(p => p.Status != "discontinued"));
        data.Add(
            "Not_NestedInsideAnd",
            SearchFilter.All(
                SearchFilter.Eq(TestProductFields.Category, SearchValue.From("furniture")),
                SearchFilter.Negate(SearchFilter.Eq(TestProductFields.Status, SearchValue.From("discontinued")))),
            Expected(p => p.Category == "furniture" && p.Status != "discontinued"));

        // --- Quote and backslash escaping (values that match NO document — proves safe escaping, not
        //     injection). Uses Status (Keyword, declared filterable), not Name — Name is declared
        //     searchable only in the shared field shape, so a filter on it would be rejected with
        //     FieldNotFilterable before ever reaching the escaping logic under test. ---
        data.Add(
            "StringEscaping_EmbeddedQuote_NoMatchNoError",
            SearchFilter.Eq(TestProductFields.Status, SearchValue.From("active\"; DROP")),
            Expected(_ => false));
        data.Add(
            "StringEscaping_EmbeddedBackslash_NoMatchNoError",
            SearchFilter.Eq(TestProductFields.Status, SearchValue.From(@"active\backslash")),
            Expected(_ => false));

        return data;
    }

    private static IReadOnlyList<string> Expected(Func<TestProduct, bool> predicate) =>
        TenantACorpus.Where(predicate).Select(p => p.DocumentId).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case_ProducesExpectedResultSet(string caseName, SearchFilter filter, IReadOnlyList<string> expectedDocumentIds)
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var request = SearchRequest.Default with { Filter = filter, PageSize = 100, RequireExactTotalHits = true };

        var result = await index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue($"case '{caseName}' should succeed");
        result.Value.Hits.Select(h => h.Document.DocumentId).Should().BeEquivalentTo(
            expectedDocumentIds, $"case '{caseName}' result set must match the LINQ-derived expectation");
        result.Value.TotalHits.Should().Be(expectedDocumentIds.Count, $"case '{caseName}' TotalHits must be exact");
    }

    /// <remarks>
    /// FINDING (recorded in <c>09.Search/CLAUDE.md</c>) — the empty-operand <c>Or</c> case is
    /// DELIBERATELY NOT in the shared <see cref="Cases"/> table above, precisely because its real
    /// behaviour diverges between the two engines and had to be discovered here, not assumed. Verified
    /// against the real v1.20.0 engine (2026-07-20): <c>MeilisearchFilterCompiler</c> compiles
    /// <c>SearchFilter.Any()</c> (zero operands) to the literal filter-expression string <c>"()"</c>
    /// (confirmed by the container-free T-10 unit test), and Meilisearch's OWN FILTER PARSER REJECTS
    /// THAT STRING AS A SYNTAX ERROR — a genuine <c>400</c> with
    /// <c>{"code":"invalid_search_filter","message":"Was expecting an operation ... at ')'."}</c>,
    /// confirmed via a raw (non-SDK) HTTP call. <c>MeilisearchIndex.SearchAsync</c> maps this cleanly to
    /// <c>SearchErrors.EngineFault</c> (the documented last-resort mapping for an unclassifiable 4xx/5xx)
    /// — a well-formed <see cref="Result"/> failure, never an unhandled exception, so the "fail loud,
    /// never degrade" contract holds even for this genuinely-malformed-filter edge case. This is the
    /// OPPOSITE of ElasticSearch's own behaviour for the identical logical case (see the ElasticSearch
    /// project's own <c>EmptyOr_RealEngineBehaviour_RecordedAsEvidence</c>, where an empty <c>Or</c>
    /// compiles to a clauseless <c>BoolQuery</c> that ElasticSearch itself treats as match-all, per
    /// <c>ElasticSearchFilterCompiler</c>'s own design note) — a genuine, real semantic divergence
    /// between the two providers for this specific AST shape, not a compiler bug on either side.
    /// </remarks>
    [Fact]
    public async Task EmptyOr_RealEngineBehaviour_RecordedAsEvidence()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var request = SearchRequest.Default with { Filter = SearchFilter.Any(), PageSize = 100, RequireExactTotalHits = true };

        var result = await index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsFailure.Should().BeTrue("Meilisearch's own filter parser rejects an empty '()' expression as invalid syntax");
        result.Error.Code.Should().Be("search.engine_fault");
    }

    [Fact]
    public async Task EnumerateAsync_WithOutOfIdOrderSeed_SettlesOrderingQuestion()
    {
        // Deliberately seeded in NON-ascending DocumentId order so "returned in id order" and "returned
        // in insertion order" are DISTINGUISHABLE outcomes — the shared corpus's own ascending-id
        // declaration order (used by every other real-backend test) cannot tell them apart.
        var orderingIndexName = $"products-ordering-{Guid.NewGuid():N}";
        var definition = new SearchIndexDefinitionBuilder(orderingIndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.EnsureIndexAsync(definition);
        try
        {
            var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, definition);
            var seedOrder = new[] { "prod-010", "prod-003", "prod-007", "prod-001", "prod-009", "prod-005" };
            var docsBySeedOrder = seedOrder
                .Select(id => TestProductCorpus.All.Single(p => p.DocumentId == id))
                .ToArray();

            // IndexAsync one at a time (not IndexManyAsync) so Meilisearch's global sequential task
            // queue processes them in this EXACT enqueue order — IndexManyAsync's internal batching
            // does not change per-document ordering within a single batch, but issuing them
            // individually removes any doubt.
            foreach (var doc in docsBySeedOrder)
            {
                (await index.IndexAsync(doc, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
            }

            var observed = new List<string>();
            await foreach (var document in index.EnumerateAsync(filter: null, TenantScope.For(TestProductCorpus.TenantA), batchSize: 2))
            {
                observed.Add(document.DocumentId);
            }

            observed.Should().BeEquivalentTo(seedOrder, "every seeded document must still be yielded regardless of order");

            var isAscendingById = observed.SequenceEqual(observed.OrderBy(id => id, StringComparer.Ordinal));
            var isInsertionOrder = observed.SequenceEqual(seedOrder);

            // FINDING (recorded in 09.Search/CLAUDE.md): Meilisearch's GET /indexes/{uid}/documents
            // returns documents in INSERTION order, NOT DocumentId-ascending order — confirmed by this
            // deliberately out-of-order seed. This assertion is the mechanical proof backing that finding.
            isInsertionOrder.Should().BeTrue(
                $"Meilisearch's /documents endpoint is expected to return insertion order; observed: {string.Join(",", observed)}");
            isAscendingById.Should().BeFalse(
                "this out-of-order seed is specifically designed so id-ascending and insertion order diverge, " +
                "proving the two are genuinely distinguishable outcomes, not a coincidence of a same-order corpus");
        }
        finally
        {
            await provisioner.DeleteIndexAsync(orderingIndexName);
        }
    }

    [Fact]
    public async Task TenantScopedFacetCounts_ExcludeOtherTenantsDocuments()
    {
        // Direct, dedicated proof (beyond T-14's incidental coverage) that the tenant filter is applied
        // BEFORE faceting, not after — a false result here would mean tenant facet counts leak
        // cross-tenant cardinality, the exact security-relevant risk this open question names.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        // PageSize must be >= 1 (SearchWellKnown/InvalidSearchRequest guard) — the facet distribution
        // itself is computed over the full filtered set regardless of PageSize, so any valid page size
        // works; hits are simply not the object of this assertion.
        var request = SearchRequest.Default with { Facets = [TestProductFields.Category], PageSize = 1 };

        var tenantAResult = await index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        tenantAResult.IsSuccess.Should().BeTrue();
        var categoryFacet = tenantAResult.Value.Facets[TestProductFields.Category];
        var totalFacetedCount = categoryFacet.Values.Sum(v => v.Count);

        // tenant-a has 10 documents; the full corpus (both tenants) has 15. A leak would show 15 (or any
        // value including tenant-b's 5 furniture/electronics/stationery documents) here instead of 10.
        totalFacetedCount.Should().Be(TenantACorpus.Count);
        totalFacetedCount.Should().NotBe(TestProductCorpus.All.Count);

        // Cross-check every individual facet bucket against the LINQ-derived per-category tenant-a count.
        foreach (var facetValue in categoryFacet.Values)
        {
            var expectedCount = TenantACorpus.Count(p => p.Category == facetValue.Value);
            facetValue.Count.Should().Be(expectedCount, $"category '{facetValue.Value}' facet count must exclude tenant-b");
        }
    }
}
