using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class VectorStoreReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_ReportsHealthy()
    {
        var provisioner = Substitute.For<IVectorCollectionProvisioner>();
        var health = new VectorCollectionHealth
        {
            Reachable = true,
            CollectionAddressable = true,
            Queryable = true,
            VectorCount = 42,
            PendingWriteCount = null,
            EngineVersion = "1.16.0",
            SchemaFingerprint = "abc123",
            Latency = TimeSpan.FromMilliseconds(12),
        };
        provisioner.ProbeAsync("documents", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<VectorCollectionHealth>.Success(health)));

        var healthCheck = new VectorStoreReadinessHealthCheck(provisioner, "documents");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task CheckHealthAsync_AnyBooleanFalse_ReportsUnhealthy_NeverDegraded(
        bool reachable, bool collectionAddressable, bool queryable)
    {
        var provisioner = Substitute.For<IVectorCollectionProvisioner>();
        var health = new VectorCollectionHealth
        {
            Reachable = reachable,
            CollectionAddressable = collectionAddressable,
            Queryable = queryable,
            VectorCount = 0,
            PendingWriteCount = null,
            EngineVersion = "1.16.0",
            SchemaFingerprint = null,
            Latency = TimeSpan.FromMilliseconds(5),
        };
        provisioner.ProbeAsync("documents", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<VectorCollectionHealth>.Success(health)));

        var healthCheck = new VectorStoreReadinessHealthCheck(provisioner, "documents");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeResultFails_ReportsUnhealthy()
    {
        var provisioner = Substitute.For<IVectorCollectionProvisioner>();
        var error = Error.Unexpected("vector_store.probe_failed", "Probe failed for collection 'documents'.");
        provisioner.ProbeAsync("documents", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<VectorCollectionHealth>.Failure(error)));

        var healthCheck = new VectorStoreReadinessHealthCheck(provisioner, "documents");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(error.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_LargePendingWriteCount_StillReportsHealthy()
    {
        // Dedicated regression: PendingWriteCount must never factor into the Healthy/Unhealthy
        // decision, even when it is large — a deep write backlog means results may be stale, not
        // unavailable, per 10.Intelligence/CLAUDE.md's own explicit rule.
        var provisioner = Substitute.For<IVectorCollectionProvisioner>();
        var health = new VectorCollectionHealth
        {
            Reachable = true,
            CollectionAddressable = true,
            Queryable = true,
            VectorCount = 1_000_000,
            PendingWriteCount = 500_000,
            EngineVersion = "1.16.0",
            SchemaFingerprint = "abc123",
            Latency = TimeSpan.FromMilliseconds(9),
        };
        provisioner.ProbeAsync("documents", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<VectorCollectionHealth>.Success(health)));

        var healthCheck = new VectorStoreReadinessHealthCheck(provisioner, "documents");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("pendingWriteCount");
        result.Data["pendingWriteCount"].Should().Be(500_000L);
    }
}
