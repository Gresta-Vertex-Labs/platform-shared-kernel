using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-15: real-backend <c>EnumerateAsync</c> corpus walk — full-corpus yield with no intermediate list,
/// cancellation mid-enumeration, and proof the walk bypasses <c>maxTotalHits</c> (goes through
/// <c>/documents</c>, not the search endpoint).
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchEnumerateAsyncTests : IAsyncLifetime
{
    private const string IndexName = "products-enumerate-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchEnumerateAsyncTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // MaxTotalHits deliberately far below the corpus size (15) — EnumerateAsync must still yield
        // every document, proving it walks GET /indexes/{uid}/documents (offset+limit), not the search
        // endpoint (which the maxTotalHits ceiling WOULD constrain).
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().MaxTotalHits(5).Build().Value;
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

    [Fact]
    public async Task EnumerateAsync_YieldsExactlyTheFullCorpus_WithNoIntermediateList_AndExceedsMaxTotalHitsCeiling()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var seenIds = new List<string>();

        // Deliberately NOT materializing an intermediate list from a LINQ/ToListAsync call — accumulate
        // via a plain running counter/list inside the loop body, one yield at a time.
        await foreach (var document in index.EnumerateAsync(filter: null, TenantScope.For(TestProductCorpus.TenantA), batchSize: 4))
        {
            seenIds.Add(document.DocumentId);
        }

        var expectedIds = TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.DocumentId).ToArray();
        seenIds.Should().HaveCount(expectedIds.Length);
        seenIds.Should().BeEquivalentTo(expectedIds);

        // MaxTotalHits was set to 5 on this index — the corpus for tenant-a alone (10 docs) already
        // exceeds it, and EnumerateAsync still yielded every one. This is the direct proof it bypasses
        // the search-endpoint ceiling.
        seenIds.Count.Should().BeGreaterThan(_definition.MaxTotalHits);

        // EVIDENCE FOR T-26 (do NOT assert an ordering guarantee here — the contract deliberately leaves
        // EnumerateAsync's ordering unspecified). Verified interactively (2026-07-20, real container
        // v1.20.0) with a dedicated out-of-id-order seed (ids inserted as prod-010, prod-003, prod-007,
        // prod-001, prod-009, prod-005): GET /indexes/{uid}/documents returned documents in EXACT
        // INSERTION order, NOT DocumentId-ascending order (the ascending-by-id sequence did not match;
        // the insertion-order sequence did). This corpus happens to be seeded in ascending-id order
        // (IndexManyAsync(TestProductCorpus.All, ...) in InitializeAsync), which is why seenIds above
        // reads as ascending — that is a property of THIS corpus's insertion order, not a Meilisearch
        // ordering guarantee. T-26's cross-provider parity suite records this finding formally in
        // src/Infrastructure/Search/CLAUDE.md.
    }

    [Fact]
    public async Task EnumerateAsync_CancellationMidEnumeration_StopsFurtherPaging()
    {
        // Cancellation is checked once per page fetch (at the top of EnumerateAsync's internal paging
        // loop), not once per individually-yielded item — an already-fetched page finishes yielding its
        // buffered items before the NEXT page fetch observes cancellation. Cancelling exactly at a batch
        // boundary (seenCount == batchSize) is therefore the deterministic way to prove "no further
        // paging": the second page must never be fetched at all, so seenCount must never exceed batchSize.
        const int batchSize = 2;
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        using var cts = new CancellationTokenSource();
        var seenCount = 0;

        var act = async () =>
        {
            await foreach (var _ in index.EnumerateAsync(filter: null, TenantScope.For(TestProductCorpus.TenantA), batchSize, cts.Token))
            {
                seenCount++;
                if (seenCount == batchSize)
                {
                    cts.Cancel();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        seenCount.Should().Be(batchSize, "no further page should be fetched once cancellation is observed at a batch boundary");
    }

    [Fact]
    public async Task EnumerateAsync_WithBatchSizeSmallerThanCorpus_StillYieldsEveryDocument()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var seenIds = new List<string>();

        await foreach (var document in index.EnumerateAsync(filter: null, TenantScope.For(TestProductCorpus.TenantB), batchSize: 2))
        {
            seenIds.Add(document.DocumentId);
        }

        seenIds.Should().BeEquivalentTo(TestProductCorpus.ForTenant(TestProductCorpus.TenantB).Select(p => p.DocumentId));
    }

    [Fact]
    public async Task EnumerateAsync_WithFilter_YieldsOnlyMatchingDocuments()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var seenIds = new List<string>();

        await foreach (var document in index.EnumerateAsync(
            SearchFilter.Eq(TestProductFields.Category, SearchValue.From("stationery")),
            TenantScope.For(TestProductCorpus.TenantA),
            batchSize: 3))
        {
            seenIds.Add(document.DocumentId);
        }

        var expected = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .Where(p => p.Category == "stationery")
            .Select(p => p.DocumentId);
        seenIds.Should().BeEquivalentTo(expected);
    }
}
