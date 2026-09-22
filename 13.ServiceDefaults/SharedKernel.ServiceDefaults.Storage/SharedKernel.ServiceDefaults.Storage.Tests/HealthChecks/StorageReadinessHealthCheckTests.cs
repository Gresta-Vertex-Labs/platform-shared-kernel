using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage;

namespace SharedKernel.ServiceDefaults.Storage.Tests.HealthChecks;

public sealed class StorageReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeSucceeds_ReportsHealthy()
    {
        var probe = Substitute.For<IFileStorageHealthProbe>();
        probe.ProbeAsync("invoices", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var healthCheck = new StorageReadinessHealthCheck(probe, "invoices");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        await probe.Received(1).ProbeAsync("invoices", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeFails_ReportsUnhealthy_NeverDegraded()
    {
        var probe = Substitute.For<IFileStorageHealthProbe>();
        var error = StorageErrors.Unavailable("invoices", "health probe");
        probe.ProbeAsync("invoices", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(error)));

        var healthCheck = new StorageReadinessHealthCheck(probe, "invoices");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
        result.Description.Should().Be(error.Message);
    }

    [Fact]
    public async Task AddStorageReadinessCheck_ProbesTheNamedStoreThroughTheRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var probed = new List<string>();
        services.AddSharedKernelStorage()
            .AddStore(ProbeOnlyStore("invoices", probed, Result.Success()))
            .AddStore(ProbeOnlyStore("archive", probed, Result.Failure(Error.Unexpected(StorageErrorCodes.Unavailable, "down"))));
        services.AddHealthChecks()
            .AddStorageReadinessCheck("invoices", "storage-invoices")
            .AddStorageReadinessCheck("archive", "storage-archive");

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["storage-invoices"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["storage-archive"].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["storage-archive"].Description.Should().Be("down");
        probed.Should().BeEquivalentTo(["invoices", "archive"]);
    }

    [Fact]
    public async Task AddStorageReadinessCheck_UnregisteredStore_ReportsUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelStorage();
        services.AddHealthChecks().AddStorageReadinessCheck("missing");

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries[HealthCheckNames.Storage].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries[HealthCheckNames.Storage].Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad name")]
    public void AddStorageReadinessCheck_InvalidStoreName_Throws(string storeName)
    {
        var builder = new ServiceCollection().AddHealthChecks();

        var act = () => builder.AddStorageReadinessCheck(storeName);

        act.Should().Throw<ArgumentException>();
    }

    private static FileStoreRegistration ProbeOnlyStore(string name, List<string> probed, Result outcome) =>
        new(
            name,
            tenantScoped: false,
            _ => throw new InvalidOperationException("The readiness check must never create the store."),
            (_, _) =>
            {
                lock (probed)
                {
                    probed.Add(name);
                }

                return Task.FromResult(outcome);
            });
}
