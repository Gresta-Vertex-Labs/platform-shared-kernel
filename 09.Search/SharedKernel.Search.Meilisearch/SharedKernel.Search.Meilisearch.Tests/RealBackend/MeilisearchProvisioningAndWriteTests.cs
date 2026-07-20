using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-13: real-backend provisioning and write round-trip against a real <see cref="MeilisearchContainerFixture"/> —
/// index creation and settings application in declared order, <see cref="SearchWriteConsistency"/> semantics,
/// upsert, delete paths, and the invalid-<c>DocumentId</c> guard against a live client.
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchProvisioningAndWriteTests : IAsyncLifetime
{
    private const string IndexName = "products-provisioning-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchProvisioningAndWriteTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task EnsureIndexAsync_AppliesSearchableFilterableSortableSettings_InDeclaredOrder()
    {
        var client = MeilisearchProviderFactory.CreateClient(_fixture);

        var settings = await client.Index(IndexName).GetSettingsAsync();

        // SearchableAttributes order is the relevance-priority order — Name declared before Description.
        settings.SearchableAttributes.Should().ContainInOrder(TestProductFields.Name, TestProductFields.Description);
        settings.FilterableAttributes.Select(a => a.Attribute).Should().Contain(
        [
            TestProductFields.TenantId, TestProductFields.Status, TestProductFields.Category,
            TestProductFields.Price, TestProductFields.Stock, TestProductFields.InStock, TestProductFields.CreatedAt,
        ]);
        settings.SortableAttributes.Should().Contain([TestProductFields.Category, TestProductFields.Price, TestProductFields.Stock, TestProductFields.CreatedAt]);
    }

    [Fact]
    public async Task IndexAsync_WithSearchableConsistency_IsImmediatelyFindable()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var doc = TestProductCorpus.All[0] with { DocumentId = $"searchable-{Guid.NewGuid():N}" };

        var writeResult = await index.IndexAsync(doc, SearchWriteConsistency.Searchable);

        writeResult.IsSuccess.Should().BeTrue();
        var getResult = await index.GetAsync(doc.DocumentId, TenantScope.Of(doc.TenantId));
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Name.Should().Be(doc.Name);
    }

    [Fact]
    public async Task IndexAsync_WithAcceptedConsistency_BecomesSearchableOnlyAfterWaitUntilSearchableAsync()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var doc = TestProductCorpus.All[1] with { DocumentId = $"accepted-{Guid.NewGuid():N}" };

        var writeResult = await index.IndexAsync(doc, SearchWriteConsistency.Accepted);
        writeResult.IsSuccess.Should().BeTrue();
        writeResult.Value.ProviderToken.Should().NotBeNullOrEmpty();

        var waitResult = await index.WaitUntilSearchableAsync(writeResult.Value, TimeSpan.FromSeconds(30));
        waitResult.IsSuccess.Should().BeTrue();

        var getResult = await index.GetAsync(doc.DocumentId, TenantScope.Of(doc.TenantId));
        getResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task IndexAsync_SameDocumentIdTwice_UpsertsRatherThanDuplicates()
    {
        // "documentId" is the PrimaryKeyField, but is deliberately not declared as a filterable Field
        // in ConfigureSharedFields() — so a duplicate-detection check must not filter on it directly
        // (Meilisearch itself rejects a filter on any attribute absent from filterableAttributes).
        // Instead, prove upsert-not-duplicate via a before/after total-count DELTA for the tenant: an
        // upsert must move the count by exactly +1 across two writes to the SAME DocumentId, never +2.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var id = $"upsert-{Guid.NewGuid():N}";
        var first = TestProductCorpus.All[2] with { DocumentId = id, Name = "First Version" };
        var second = first with { Name = "Second Version" };
        var beforeCount = (await index.CountAsync(null, TenantScope.Of(first.TenantId))).Value;

        await index.IndexAsync(first, SearchWriteConsistency.Searchable);
        await index.IndexAsync(second, SearchWriteConsistency.Searchable);

        var getResult = await index.GetAsync(id, TenantScope.Of(first.TenantId));
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Name.Should().Be("Second Version");

        var afterCount = (await index.CountAsync(null, TenantScope.Of(first.TenantId))).Value;
        afterCount.Should().Be(beforeCount + 1);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocument()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var doc = TestProductCorpus.All[3] with { DocumentId = $"delete-one-{Guid.NewGuid():N}" };
        await index.IndexAsync(doc, SearchWriteConsistency.Searchable);

        var deleteResult = await index.DeleteAsync(doc.DocumentId, SearchWriteConsistency.Searchable);

        deleteResult.IsSuccess.Should().BeTrue();
        (await index.GetAsync(doc.DocumentId, TenantScope.Of(doc.TenantId))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesAllSpecifiedDocuments()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var suffix = Guid.NewGuid().ToString("N");
        var docs = new[]
        {
            TestProductCorpus.All[4] with { DocumentId = $"delete-many-a-{suffix}" },
            TestProductCorpus.All[5] with { DocumentId = $"delete-many-b-{suffix}" },
        };
        await index.IndexManyAsync(docs, SearchWriteConsistency.Searchable);

        var deleteResult = await index.DeleteManyAsync(docs.Select(d => d.DocumentId).ToArray(), SearchWriteConsistency.Searchable);

        deleteResult.IsSuccess.Should().BeTrue();
        foreach (var doc in docs)
        {
            (await index.GetAsync(doc.DocumentId, TenantScope.Of(doc.TenantId))).IsFailure.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DeleteByFilterAsync_RemovesOnlyMatchingDocuments()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var suffix = Guid.NewGuid().ToString("N");
        var matching = TestProductCorpus.All[6] with { DocumentId = $"filter-del-match-{suffix}", Status = "to-be-deleted" };
        var nonMatching = TestProductCorpus.All[7] with { DocumentId = $"filter-del-keep-{suffix}", Status = "active", TenantId = matching.TenantId };
        await index.IndexManyAsync([matching, nonMatching], SearchWriteConsistency.Searchable);

        var deleteResult = await index.DeleteByFilterAsync(
            SearchFilter.Eq(TestProductFields.Status, SearchValue.From("to-be-deleted")),
            TenantScope.Of(matching.TenantId),
            SearchWriteConsistency.Searchable);

        deleteResult.IsSuccess.Should().BeTrue();
        (await index.GetAsync(matching.DocumentId, TenantScope.Of(matching.TenantId))).IsFailure.Should().BeTrue();
        (await index.GetAsync(nonMatching.DocumentId, TenantScope.Of(nonMatching.TenantId))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ClearAsync_RemovesEveryDocument()
    {
        var clearIndexName = $"products-clear-{Guid.NewGuid():N}";
        var clearDefinition = new SearchIndexDefinitionBuilder(clearIndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.EnsureIndexAsync(clearDefinition);
        try
        {
            var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, clearDefinition);
            await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable);

            var clearResult = await index.ClearAsync(SearchWriteConsistency.Searchable);

            clearResult.IsSuccess.Should().BeTrue();
            var countResult = await index.CountAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantA));
            countResult.Value.Should().Be(0);
        }
        finally
        {
            await provisioner.DeleteIndexAsync(clearIndexName);
        }
    }

    [Fact]
    public async Task IndexAsync_WithInvalidDocumentId_ReturnsInvalidDocumentId_AndCorpusUnaffected()
    {
        // The charset guard runs client-side before any request is issued (already proven zero-I/O
        // against a null client in the container-free MeilisearchPreflightValidationTests). This
        // confirms the SAME guard still fires end-to-end against a live client.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var beforeCount = (await index.CountAsync(null, TenantScope.Of(TestProductCorpus.TenantA))).Value;
        var invalidDoc = TestProductCorpus.All[8] with { DocumentId = "invalid id with spaces" };

        var result = await index.IndexAsync(invalidDoc, SearchWriteConsistency.Searchable);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_document_id");
        var afterCount = (await index.CountAsync(null, TenantScope.Of(TestProductCorpus.TenantA))).Value;
        afterCount.Should().Be(beforeCount);
    }
}
