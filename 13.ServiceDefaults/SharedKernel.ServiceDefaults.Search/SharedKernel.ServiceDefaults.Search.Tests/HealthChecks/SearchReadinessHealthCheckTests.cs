using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Search.Tests.HealthChecks;

public sealed class SearchReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_ReportsHealthy()
    {
        var provisioner = Substitute.For<ISearchIndexProvisioner>();
        var health = new SearchIndexHealth
        {
            Reachable = true,
            IndexAddressable = true,
            Searchable = true,
            DocumentCount = 42,
            PendingWriteCount = null,
            EngineVersion = "1.20.0",
            Latency = TimeSpan.FromMilliseconds(12),
        };
        provisioner.ProbeAsync("products", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<SearchIndexHealth>.Success(health)));

        var healthCheck = new SearchReadinessHealthCheck(provisioner, "products");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task CheckHealthAsync_AnyBooleanFalse_ReportsUnhealthy_NeverDegraded(
        bool reachable, bool indexAddressable, bool searchable)
    {
        var provisioner = Substitute.For<ISearchIndexProvisioner>();
        var health = new SearchIndexHealth
        {
            Reachable = reachable,
            IndexAddressable = indexAddressable,
            Searchable = searchable,
            DocumentCount = 0,
            PendingWriteCount = null,
            EngineVersion = "1.20.0",
            Latency = TimeSpan.FromMilliseconds(5),
        };
        provisioner.ProbeAsync("products", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<SearchIndexHealth>.Success(health)));

        var healthCheck = new SearchReadinessHealthCheck(provisioner, "products");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeResultFails_ReportsUnhealthy()
    {
        var provisioner = Substitute.For<ISearchIndexProvisioner>();
        var error = Error.Unexpected("search.probe_failed", "Probe failed for index 'products'.");
        provisioner.ProbeAsync("products", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<SearchIndexHealth>.Failure(error)));

        var healthCheck = new SearchReadinessHealthCheck(provisioner, "products");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(error.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_LargePendingWriteCount_StillReportsHealthy()
    {
        // Dedicated regression: PendingWriteCount must never factor into the Healthy/Unhealthy
        // decision, even when it is large — a deep write backlog means results are stale, not
        // unavailable, per 09.Search/CLAUDE.md's own explicit rule.
        var provisioner = Substitute.For<ISearchIndexProvisioner>();
        var health = new SearchIndexHealth
        {
            Reachable = true,
            IndexAddressable = true,
            Searchable = true,
            DocumentCount = 1_000_000,
            PendingWriteCount = 500_000,
            EngineVersion = "1.20.0",
            Latency = TimeSpan.FromMilliseconds(9),
        };
        provisioner.ProbeAsync("products", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<SearchIndexHealth>.Success(health)));

        var healthCheck = new SearchReadinessHealthCheck(provisioner, "products");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("pendingWriteCount");
        result.Data["pendingWriteCount"].Should().Be(500_000L);
    }
}
