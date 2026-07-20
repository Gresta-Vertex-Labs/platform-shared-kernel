using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-16: real-backend cutover and probe — atomic staging→live swap, <c>DeleteStagingAfterCutover</c>
/// both ways (including the 9113 log on opt-out), a full healthy <c>ProbeAsync</c> reading, the
/// mis-scoped-API-key <c>IndexAddressable</c> failure while <c>/health</c> still passes, and
/// caller-side schema-fingerprint drift detection.
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchCutoverAndProbeTests
{
    private readonly MeilisearchContainerFixture _fixture;

    public MeilisearchCutoverAndProbeTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CutoverAsync_SwapsStagingIntoLive_AndDeletesStagingByDefault()
    {
        var staging = $"cutover-staging-{Guid.NewGuid():N}";
        var live = $"cutover-live-{Guid.NewGuid():N}";
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        var stagingDefinition = new SearchIndexDefinitionBuilder(staging).ConfigureSharedFields().Build().Value;
        await provisioner.EnsureIndexAsync(stagingDefinition);
        var stagingIndex = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, stagingDefinition);
        var stagingOnlyDoc = TestProductCorpus.All[0] with { DocumentId = $"staging-only-{Guid.NewGuid():N}" };
        await stagingIndex.IndexAsync(stagingOnlyDoc, SearchWriteConsistency.Searchable);

        try
        {
            var cutoverResult = await provisioner.CutoverAsync(
                new IndexCutoverRequest { StagingIndexName = staging, LiveIndexName = live, DeleteStagingAfterCutover = true });

            cutoverResult.IsSuccess.Should().BeTrue();

            var liveDefinition = stagingDefinition with { Name = live };
            var liveIndex = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, liveDefinition);
            var getResult = await liveIndex.GetAsync(stagingOnlyDoc.DocumentId, TenantScope.Of(stagingOnlyDoc.TenantId));
            getResult.IsSuccess.Should().BeTrue("the live-named index must now serve the staged data after cutover");

            (await provisioner.IndexExistsAsync(staging)).Value.Should().BeFalse("the staging index is deleted by default after cutover");
        }
        finally
        {
            await provisioner.DeleteIndexAsync(live);
            await provisioner.DeleteIndexAsync(staging);
        }
    }

    [Fact]
    public async Task CutoverAsync_WithDeleteStagingAfterCutoverFalse_RetainsStaging_AndLogsWarning9113()
    {
        var staging = $"cutover-staging-retain-{Guid.NewGuid():N}";
        var live = $"cutover-live-retain-{Guid.NewGuid():N}";
        var logger = new InMemoryLogger<MeilisearchIndexProvisioner>();
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture, logger);
        var stagingDefinition = new SearchIndexDefinitionBuilder(staging).ConfigureSharedFields().Build().Value;
        await provisioner.EnsureIndexAsync(stagingDefinition);

        try
        {
            var cutoverResult = await provisioner.CutoverAsync(
                new IndexCutoverRequest { StagingIndexName = staging, LiveIndexName = live, DeleteStagingAfterCutover = false });

            cutoverResult.IsSuccess.Should().BeTrue();
            (await provisioner.IndexExistsAsync(staging)).Value.Should().BeTrue("opting out of cleanup must leave the staging index in place");
            logger.Records.Should().Contain(r => r.EventId.Id == 9113 && r.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning);
        }
        finally
        {
            await provisioner.DeleteIndexAsync(live);
            await provisioner.DeleteIndexAsync(staging);
        }
    }

    [Fact]
    public async Task ProbeAsync_OnHealthySeededIndex_PopulatesEveryHealthMember()
    {
        var indexName = $"probe-healthy-{Guid.NewGuid():N}";
        var definition = new SearchIndexDefinitionBuilder(indexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.EnsureIndexAsync(definition);
        try
        {
            var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, definition);
            await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable);

            var probeResult = await provisioner.ProbeAsync(indexName);

            probeResult.IsSuccess.Should().BeTrue();
            var health = probeResult.Value;
            health.Reachable.Should().BeTrue();
            health.IndexAddressable.Should().BeTrue();
            health.Searchable.Should().BeTrue();
            health.PendingWriteCount.Should().NotBeNull();
            health.DocumentCount.Should().Be(TestProductCorpus.All.Count);
            health.Latency.Should().BeGreaterThan(TimeSpan.Zero);
            health.EngineVersion.Should().NotBeNullOrEmpty();
            health.SchemaFingerprint.Should().Be(definition.Fingerprint);
        }
        finally
        {
            await provisioner.DeleteIndexAsync(indexName);
        }
    }

    [Fact]
    public async Task ProbeAsync_WithMisScopedApiKey_FailsIndexAddressable_WhileHealthStillPasses()
    {
        var indexName = $"probe-scoped-key-{Guid.NewGuid():N}";
        var otherIndexName = $"probe-scoped-key-other-{Guid.NewGuid():N}";
        var masterProvisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await masterProvisioner.EnsureIndexAsync(new SearchIndexDefinitionBuilder(indexName).ConfigureSharedFields().Build().Value);

        try
        {
            // Mint a key scoped to a DIFFERENT index — test scaffolding only, via a raw HTTP call to
            // Meilisearch's key-management endpoint authorized with the fixture's own master key. This is
            // NOT the production IMeilisearchRawClientAccessor pattern; it exists purely to construct a
            // realistic mis-scoped-key scenario for this one test.
            using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Url) };
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _fixture.ApiKey);
            var createKeyResponse = await httpClient.PostAsJsonAsync("/keys", new
            {
                description = "scoped-key-test",
                actions = new[] { "search", "documents.get" },
                indexes = new[] { otherIndexName },
                expiresAt = (string?)null,
            });
            createKeyResponse.EnsureSuccessStatusCode();
            var keyPayload = await createKeyResponse.Content.ReadFromJsonAsync<JsonElement>();
            var scopedKey = keyPayload.GetProperty("key").GetString()!;

            var scopedProvisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture, scopedKey);

            var probeResult = await scopedProvisioner.ProbeAsync(indexName);

            probeResult.IsSuccess.Should().BeTrue();
            probeResult.Value.Reachable.Should().BeTrue("/health is unauthenticated and unaffected by key scope");
            probeResult.Value.IndexAddressable.Should().BeFalse("the scoped key has no access to this index");
        }
        finally
        {
            await masterProvisioner.DeleteIndexAsync(indexName);
        }
    }

    [Fact]
    public void SchemaFingerprint_Drift_IsDetectableByComparingProbeResultAgainstTheCallersOwnDefinition()
    {
        // ProbeAsync itself never compares SchemaFingerprint against an expected value (it only reports
        // what is currently stored) — SearchErrors.SchemaFingerprintMismatch exists for the CALLER to
        // construct once it detects drift by comparing ProbeAsync's returned health record against its
        // own held SearchIndexDefinition.Fingerprint. This test proves the comparison mechanics: two
        // definitions differing by one field role produce different fingerprints, and the canonical error
        // factory accepts exactly that (expected, actual) pair.
        var definitionA = new SearchIndexDefinitionBuilder("drift-test")
            .Field("status", SearchFieldKind.Keyword, filterable: true)
            .Build().Value;
        var definitionB = new SearchIndexDefinitionBuilder("drift-test")
            .Field("status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Build().Value;

        definitionA.Fingerprint.Should().NotBe(definitionB.Fingerprint);

        var error = SearchErrors.SchemaFingerprintMismatch("drift-test", definitionA.Fingerprint, definitionB.Fingerprint);
        error.Code.Should().Be("search.schema_fingerprint_mismatch");
    }
}
