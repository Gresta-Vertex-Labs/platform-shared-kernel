using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Tests.Abstractions;

/// <summary>P-569: one collection's measured health, turned into a readiness report.</summary>
public sealed class VectorCollectionReadinessProbeTests
{
    [Fact]
    public void Name_CombinesProviderAndCollection()
    {
        var probe = new VectorCollectionReadinessProbe("qdrant", "chunks", (_, _) => throw new InvalidOperationException());

        probe.Name.Should().Be("vector-store-qdrant-chunks");
    }

    [Fact]
    public async Task QueryableCollection_IsHealthy_WithTheMeasuredDetailInData()
    {
        var report = await Probe(queryable: true).ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Healthy);
        report.Data[VectorCollectionReadinessProbe.VectorCountKey].Should().Be(5L);
        report.Data.Should().NotContainKey(VectorCollectionReadinessProbe.PendingWriteCountKey);
        report.Latency.Should().Be(TimeSpan.FromMilliseconds(3));
    }

    [Fact]
    public async Task UnqueryableCollection_IsUnhealthy()
    {
        (await Probe(queryable: false).ProbeAsync()).Status.Should().Be(ReadinessStatus.Unhealthy);
    }

    [Fact]
    public async Task FailedMeasurement_IsUnhealthy_WithTheErrorCode()
    {
        var probe = new VectorCollectionReadinessProbe(
            "qdrant",
            "chunks",
            (name, _) => Task.FromResult(Result<VectorCollectionHealth>.Failure(IntelligenceErrors.CollectionNotFound(name))));

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Data[VectorCollectionReadinessProbe.ErrorCodeKey].Should().Be("intelligence.collection_not_found");
    }

    private static VectorCollectionReadinessProbe Probe(bool queryable) =>
        new("qdrant", "chunks", (_, _) => Task.FromResult(Result<VectorCollectionHealth>.Success(new VectorCollectionHealth
        {
            Reachable = true,
            CollectionAddressable = true,
            Queryable = queryable,
            VectorCount = 5,
            EngineVersion = "1.16",
            Latency = TimeSpan.FromMilliseconds(3),
        })));
}
