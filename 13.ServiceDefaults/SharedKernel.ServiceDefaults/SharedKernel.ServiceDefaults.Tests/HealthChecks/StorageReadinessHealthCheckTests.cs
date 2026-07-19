using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class StorageReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeSucceeds_ReportsHealthy()
    {
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.CheckHealthAsync("my-bucket", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var healthCheck = new StorageReadinessHealthCheck(fileStorage, "my-bucket");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeFails_ReportsUnhealthy_NeverDegraded()
    {
        var fileStorage = Substitute.For<IFileStorage>();
        var error = Error.Unexpected("storage.connectivity_failure", "Storage connectivity check failed for bucket 'my-bucket'.");
        fileStorage.CheckHealthAsync("my-bucket", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(error)));

        var healthCheck = new StorageReadinessHealthCheck(fileStorage, "my-bucket");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
        result.Description.Should().Be(error.Message);
    }
}
