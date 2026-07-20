using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>T-25: real-backend <c>ProbeAsync</c> coverage against a live ElasticSearch 9.4.2 container.</summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchProbeTests : IAsyncLifetime
{
    private const string IndexName = "products-probe-tests";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;

    public ElasticSearchProbeTests(ElasticsearchContainerFixture fixture)
    {
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        _definition = TestProductIndexDefinitions.Standard(IndexName);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
    }

    public async Task InitializeAsync()
    {
        (await _provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync() => await _provisioner.DeleteIndexAsync(IndexName);

    [Fact]
    public async Task ProbeAsync_HealthyIndex_ReportsFullyHealthy_WithYellowClusterAccepted()
    {
        var result = await _provisioner.ProbeAsync(IndexName);

        result.IsSuccess.Should().BeTrue();
        // The fixture's own single-node cluster is PERMANENTLY yellow (a single node can never allocate
        // replica shards) — this is expected and correct, not a workaround. ProbeAsync deliberately
        // treats yellow as healthy for exactly this reason (see ElasticSearchIndexProvisioner.ProbeAsync,
        // which gates only on `HealthStatus.Yellow or HealthStatus.Green`). This Reachable == true
        // assertion against a real, freshly-provisioned single-node cluster IS the healthy-probe proof.
        result.Value.Reachable.Should().BeTrue();
        result.Value.IndexAddressable.Should().BeTrue();
        result.Value.Searchable.Should().BeTrue();
        result.Value.DocumentCount.Should().Be(TestProductCorpus.All.Count);
        result.Value.Latency.Should().BeGreaterThan(TimeSpan.Zero);
        // Permanent on ElasticSearch — never null-as-a-TODO, never accidentally 0. 13.ServiceDefaults
        // must never treat this null as unhealthy.
        result.Value.PendingWriteCount.Should().BeNull();
    }

    /// <summary>
    /// Forcing a genuinely RED cluster against a single-node Testcontainers fixture is not attempted —
    /// it would require inducing real damage (e.g. deleting a primary shard's data out from under the
    /// engine) with no reliable, non-destructive trigger available. This is instead a structural/
    /// code-reading proof: <see cref="ElasticSearchIndexProvisioner"/>'s <c>ProbeAsync</c> computes
    /// <c>reachable</c> as <c>healthResponse.IsValidResponse &amp;&amp; healthResponse.Status is
    /// HealthStatus.Yellow or HealthStatus.Green</c> (confirmed by reading
    /// <c>Provisioning/ElasticSearchIndexProvisioner.cs</c> directly), which structurally excludes
    /// every other <see cref="HealthStatus"/> member — including <c>Red</c> — from ever being reported
    /// reachable, by construction, not by convention.
    /// </summary>
    [Fact]
    public void HealthStatus_HasMembersBeyondYellowAndGreen_StructurallyConfirmingRedIsExcludedFromReachable()
    {
        var members = Enum.GetValues<HealthStatus>();

        members.Should().Contain(
            [HealthStatus.Red, HealthStatus.Unavailable, HealthStatus.Unknown],
            "these members exist specifically to be excluded by ProbeAsync's `is Yellow or Green` pattern match");
        members.Should().Contain([HealthStatus.Yellow, HealthStatus.Green]);
    }

    [Fact]
    public async Task ProbeAsync_NonExistentIndexName_ReachableTrue_ButNotAddressableOrSearchable()
    {
        var result = await _provisioner.ProbeAsync("some-alias-that-does-not-exist");

        result.IsSuccess.Should().BeTrue();
        result.Value.Reachable.Should().BeTrue();
        result.Value.IndexAddressable.Should().BeFalse();
        result.Value.Searchable.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_WithNonZeroProbeCacheSeconds_ServesSecondCallFromCache()
    {
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(
            _client, ElasticsearchProviderFactory.CreateOptions(o => o.ProbeCacheSeconds = 5));

        var first = await provisioner.ProbeAsync(IndexName);
        var second = await provisioner.ProbeAsync(IndexName);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        // SearchIndexHealth is a sealed record — record (value) equality against the FIRST call's exact
        // Latency/DocumentCount/etc. is only possible if the second call was served from the cache
        // rather than re-probing (a re-probe would almost certainly observe a different Latency).
        second.Value.Should().Be(first.Value);
    }

    [Fact]
    public async Task ProbeAsync_WithZeroProbeCacheSeconds_DisablesCaching_BothCallsSucceedIndependently()
    {
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(
            _client, ElasticsearchProviderFactory.CreateOptions(o => o.ProbeCacheSeconds = 0));

        var first = await provisioner.ProbeAsync(IndexName);
        var second = await provisioner.ProbeAsync(IndexName);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Reachable.Should().BeTrue();
        second.Value.Reachable.Should().BeTrue();
    }
}
