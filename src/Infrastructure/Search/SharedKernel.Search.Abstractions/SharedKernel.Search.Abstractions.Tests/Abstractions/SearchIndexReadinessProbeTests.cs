using FluentAssertions;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Abstractions;

/// <summary>P-569: one index's measured health, turned into a readiness report.</summary>
public sealed class SearchIndexReadinessProbeTests
{
    [Fact]
    public void Name_CombinesProviderAndIndex()
    {
        var probe = new SearchIndexReadinessProbe("meilisearch", "products", (_, _) => throw new InvalidOperationException());

        probe.Name.Should().Be("search-meilisearch-products");
        SearchIndexReadinessProbe.ProbeNameFor("elasticsearch", "products").Should().Be("search-elasticsearch-products");
    }

    [Fact]
    public async Task FullyReadyIndex_IsHealthy_WithTheMeasuredDetailInData()
    {
        var probe = Probe(Health(searchable: true, pending: null, fingerprint: "abc"));

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Healthy);
        report.Latency.Should().Be(TimeSpan.FromMilliseconds(7));
        report.Data[SearchIndexReadinessProbe.IndexKey].Should().Be("products");
        report.Data[SearchIndexReadinessProbe.DocumentCountKey].Should().Be(42L);
        report.Data[SearchIndexReadinessProbe.SchemaFingerprintKey].Should().Be("abc");
        report.Data.Should().NotContainKey(SearchIndexReadinessProbe.PendingWriteCountKey, "an unknown backlog is absent, never a fabricated 0");
    }

    [Fact]
    public async Task UnsearchableIndex_IsUnhealthy_AndABacklogAloneNeverIs()
    {
        (await Probe(Health(searchable: false, pending: 0, fingerprint: null)).ProbeAsync()).Status
            .Should().Be(ReadinessStatus.Unhealthy);
        (await Probe(Health(searchable: true, pending: 1_000_000, fingerprint: null)).ProbeAsync()).Status
            .Should().Be(ReadinessStatus.Healthy);
    }

    [Fact]
    public async Task FailedMeasurement_IsUnhealthy_WithTheErrorCode()
    {
        var probe = new SearchIndexReadinessProbe(
            "meilisearch",
            "products",
            (index, _) => Task.FromResult(Result<SearchIndexHealth>.Failure(SearchErrors.IndexNotFound(index))));

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Data[SearchIndexReadinessProbe.ErrorCodeKey].Should().Be("search.index_not_found");
    }

    [Fact]
    public async Task FailedMeasurement_UnhealthyDescription_IsTheKernelErrorMessage_WithNoEngineText()
    {
        var error = SearchErrors.Unreachable("meilisearch");
        var probe = new SearchIndexReadinessProbe(
            "meilisearch", "products", (_, _) => Task.FromResult(Result<SearchIndexHealth>.Failure(error)));

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Description.Should().Be(error.Message).And.NotContain("http");
        report.Data.Values.OfType<string>().Should().NotContain(value => value.Contains("http", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledToken_PropagatesBeforeTheMeasurement()
    {
        var measured = false;
        var probe = new SearchIndexReadinessProbe(
            "meilisearch",
            "products",
            (_, _) =>
            {
                measured = true;
                return Task.FromResult(Result<SearchIndexHealth>.Success(Health(searchable: true, pending: null, fingerprint: null)));
            });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => probe.ProbeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        measured.Should().BeFalse();
    }

    private static SearchIndexReadinessProbe Probe(SearchIndexHealth health) =>
        new("meilisearch", "products", (_, _) => Task.FromResult(Result<SearchIndexHealth>.Success(health)));

    private static SearchIndexHealth Health(bool searchable, long? pending, string? fingerprint) => new()
    {
        Reachable = true,
        IndexAddressable = true,
        Searchable = searchable,
        DocumentCount = 42,
        PendingWriteCount = pending,
        EngineVersion = "1.0",
        SchemaFingerprint = fingerprint,
        Latency = TimeSpan.FromMilliseconds(7),
    };
}
