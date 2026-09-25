using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// T-21: real-backend provisioning and write round-trip against a live ElasticSearch 9.4.2 container.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchProvisioningAndWriteTests : IAsyncLifetime
{
    private const string IndexName = "products-provisioning-tests";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;
    private readonly ElasticSearchIndex<TestProduct> _index;

    public ElasticSearchProvisioningAndWriteTests(ElasticsearchContainerFixture fixture)
    {
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        _definition = TestProductIndexDefinitions.Standard(IndexName);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        _index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);
    }

    public async Task InitializeAsync()
    {
        var ensureResult = await _provisioner.EnsureIndexAsync(_definition);
        ensureResult.IsSuccess.Should().BeTrue();

        var seedResult = await _index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable);
        seedResult.IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync() => await _provisioner.DeleteIndexAsync(IndexName);

    [Fact]
    public async Task EnsureIndexAsync_ProducesExpectedTypeMapping_PerSearchFieldKind()
    {
        var mappingResponse = await _client.Indices.GetMappingAsync(IndexName);
        mappingResponse.IsValidResponse.Should().BeTrue();

        var properties = mappingResponse.Mappings[IndexName]!.Mappings.Properties!;

        properties[TestProductFields.Name]!.Type.Should().Be("text");
        properties[TestProductFields.Status]!.Type.Should().Be("keyword");
        properties[TestProductFields.Stock]!.Type.Should().Be("integer");
        properties[TestProductFields.Price]!.Type.Should().Be("double");
        properties[TestProductFields.InStock]!.Type.Should().Be("boolean");
        properties[TestProductFields.CreatedAt]!.Type.Should().Be("date");
    }

    [Fact]
    public async Task EnsureIndexAsync_SetsMaxResultWindow_ToDefinitionsMaxTotalHits()
    {
        var settingsResponse = await _client.Indices.GetSettingsAsync((Elastic.Clients.Elasticsearch.Indices)IndexName);
        settingsResponse.IsValidResponse.Should().BeTrue();

        var indexState = settingsResponse.Settings[IndexName]!;
        // ElasticSearch's GET _settings response always nests effective settings under an "index" key,
        // even though CreateIndexRequest.Settings is written with flat property names — the SDK models
        // both shapes on the same IndexSettings type. Check the nested form first, falling back to the
        // flat form so this assertion is correct regardless of which shape the client actually returns.
        var settings = indexState.Settings!;
        var maxResultWindow = settings.Index?.MaxResultWindow ?? settings.MaxResultWindow;
        maxResultWindow.Should().Be(_definition.MaxTotalHits);
    }

    [Fact]
    public async Task IndexAsync_WithSearchableConsistency_IsImmediatelyFindable()
    {
        var document = NewProduct("prod-write-searchable-001");

        var writeResult = await _index.IndexAsync(document, SearchWriteConsistency.Searchable);
        writeResult.IsSuccess.Should().BeTrue();

        var getResult = await _index.GetAsync(document.DocumentId, TenantScope.For(TestProductCorpus.TenantA));
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.DocumentId.Should().Be(document.DocumentId);
    }

    [Fact]
    public async Task IndexAsync_WithAcceptedConsistency_BecomesFindable_AfterWaitUntilSearchableAsync()
    {
        // The "not necessarily immediately visible" half of Accepted's contract is deliberately not
        // asserted as a hard negative here — whether a fresh single-shard test index refreshes before
        // the very next statement runs is a timing race, not a portable behavioral guarantee. The
        // positive, deterministic half — WaitUntilSearchableAsync unconditionally makes it findable — is
        // what this test proves.
        var document = NewProduct("prod-write-accepted-001");

        var writeResult = await _index.IndexAsync(document, SearchWriteConsistency.Accepted);
        writeResult.IsSuccess.Should().BeTrue();

        var waitResult = await _index.WaitUntilSearchableAsync(writeResult.Value, TimeSpan.FromSeconds(10));
        waitResult.IsSuccess.Should().BeTrue();

        var getResult = await _index.GetAsync(document.DocumentId, TenantScope.For(TestProductCorpus.TenantA));
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.DocumentId.Should().Be(document.DocumentId);
    }

    [Fact]
    public void SearchWriteConsistency_HasExactlyTwoMembers_StructurallyProving_RefreshTrueIsUnreachable()
    {
        // ElasticSearchIndex<TDocument>.ToRefresh is a private static method mapping ONLY
        // SearchWriteConsistency.Searchable -> Refresh.WaitFor and everything else -> Refresh.False
        // (confirmed by reading Index/ElasticSearchIndex.cs directly). Refresh.True can only ever be
        // reached by a SearchWriteConsistency value neither ToRefresh nor any caller can construct, since
        // the enum itself has exactly these two members — this is the closest positive structural proof
        // achievable without reflecting into a private method or capturing outbound HTTP request bodies.
        var members = Enum.GetValues<SearchWriteConsistency>();

        members.Should().BeEquivalentTo(
            [SearchWriteConsistency.Accepted, SearchWriteConsistency.Searchable],
            "a third value would be required to ever reach Refresh.True, and no such value exists");
    }

    [Fact]
    public async Task IndexManyAsync_AllValidDocuments_ReturnsSuccessWithEmptyFailures()
    {
        // A genuine ENGINE-SIDE per-item bulk failure could not be engineered against the strongly-typed
        // TestProduct contract (every field is a plain C# primitive matching its mapped type exactly, and
        // IndexManyAsync exposes no per-item version/concurrency token a caller could deliberately
        // conflict) — this test instead proves the all-valid success shape: a success Result whose
        // SucceededCount matches the batch and whose Failures list is empty, exactly as
        // SearchBulkReceipt's own contract describes for the common case.
        var batch = new[]
        {
            NewProduct("prod-bulk-valid-001"),
            NewProduct("prod-bulk-valid-002"),
            NewProduct("prod-bulk-valid-003"),
        };

        var result = await _index.IndexManyAsync(batch, SearchWriteConsistency.Searchable);

        result.IsSuccess.Should().BeTrue();
        result.Value.SucceededCount.Should().Be(batch.Length);
        result.Value.Failures.Should().BeEmpty();
        result.Value.HasFailures.Should().BeFalse();
    }

    [Fact]
    public async Task IndexManyAsync_WithOneInvalidDocumentId_RejectsWholeBatch_BeforeAnyIO()
    {
        var batch = new[]
        {
            NewProduct("prod-bulk-mixed-001"),
            NewProduct("prod bulk mixed 002 !!"), // violates the A-Z a-z 0-9 - _ charset
            NewProduct("prod-bulk-mixed-003"),
        };

        var result = await _index.IndexManyAsync(batch, SearchWriteConsistency.Searchable);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_document_id");

        // No-I/O proof: the charset check runs over the WHOLE collection before any request is issued,
        // so even the VALID sibling documents in the same batch must be unaffected.
        var siblingResult = await _index.GetAsync("prod-bulk-mixed-001", TenantScope.For(TestProductCorpus.TenantA));
        siblingResult.IsFailure.Should().BeTrue();
        siblingResult.Error.Code.Should().Be("search.document_not_found");
    }

    [Fact]
    public async Task IndexAsync_WithInvalidDocumentId_RejectedBeforeIO_CorpusCountUnaffected()
    {
        var invalidDocument = NewProduct("invalid id with spaces!");

        var writeResult = await _index.IndexAsync(invalidDocument, SearchWriteConsistency.Searchable);

        writeResult.IsFailure.Should().BeTrue();
        writeResult.Error.Code.Should().Be("search.invalid_document_id");

        var countResult = await _index.CountAsync(filter: null, TenantScope.For(TestProductCorpus.TenantA));
        countResult.IsSuccess.Should().BeTrue();
        countResult.Value.IsExact.Should().BeTrue();
        countResult.Value.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
    }

    private static TestProduct NewProduct(string documentId) => new()
    {
        DocumentId = documentId,
        TenantId = TestProductCorpus.TenantA.ToString(),
        Name = "Write Test Product",
        Description = "A product created directly by a T-21 write test.",
        Status = "active",
        Category = "electronics",
        Price = 19.99,
        Stock = 5,
        InStock = true,
        CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
