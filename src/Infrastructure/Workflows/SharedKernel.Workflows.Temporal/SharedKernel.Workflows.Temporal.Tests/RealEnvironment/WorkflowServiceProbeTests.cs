using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Testing.Logging;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Health;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// The readiness probe logs the exception of a faulted check (event 17013, Warning) and still reports the same
/// <see cref="ReadinessReport"/>; a cancelled token is never swallowed.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class WorkflowServiceProbeTests(TemporalTestFixture fixture, LocalDevServerFixture localServer)
    : IClassFixture<LocalDevServerFixture>
{
    private const int ProbeCheckFailedEventId = 17013;

    private static (WorkflowServiceProbe Probe, InMemoryLogger<WorkflowServiceProbe> Logger) CreateProbe(
        ITemporalClient client,
        string @namespace)
    {
        var logger = new InMemoryLogger<WorkflowServiceProbe>();
        var services = new ServiceCollection();
        services.AddSingleton(client);
        services.AddSingleton<IOptions<TemporalOptions>>(
            Options.Create(new TemporalOptions { TargetHost = "unused", Namespace = @namespace }));
        services.AddSingleton<ILogger<WorkflowServiceProbe>>(logger);
        return (new WorkflowServiceProbe(services.BuildServiceProvider()), logger);
    }

    [Fact]
    public async Task Probe_AnUnaddressableNamespace_LogsTheFaultWithItsException_AndReportsUnhealthy()
    {
        (WorkflowServiceProbe probe, InMemoryLogger<WorkflowServiceProbe> logger) =
            CreateProbe(localServer.Environment.Client, $"no-such-namespace-{Guid.NewGuid():N}");

        ReadinessReport report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Data[WorkflowReadiness.ReachableKey].Should().Be(true);
        report.Data[WorkflowReadiness.NamespaceAddressableKey].Should().Be(false);
        LogRecord record = logger.Records
            .ShouldHaveLoggedWithException(new EventId(ProbeCheckFailedEventId), typeof(Temporalio.Exceptions.RpcException));
        record.LogLevel.Should().Be(LogLevel.Warning);
        record.TryGetProperty("Check", out object? check).Should().BeTrue();
        check.Should().Be("namespace");
    }

    [Fact]
    public async Task Probe_AnUnreachableService_ReportsUnhealthy_AndLogsTheConnectFaultWithItsException()
    {
        ITemporalClient client = TemporalClient.CreateLazy(
            new TemporalClientConnectOptions("127.0.0.1:1") { Namespace = "default" });
        (WorkflowServiceProbe probe, InMemoryLogger<WorkflowServiceProbe> logger) = CreateProbe(client, "default");

        ReadinessReport report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Data[WorkflowReadiness.ReachableKey].Should().Be(false);
        report.Data[WorkflowReadiness.NamespaceAddressableKey].Should().Be(false);
        LogRecord fault = logger.Records.ShouldHaveLoggedWithException(
            new EventId(ProbeCheckFailedEventId), typeof(InvalidOperationException));
        fault.LogLevel.Should().Be(LogLevel.Warning);
        fault.TryGetProperty("Check", out object? check).Should().BeTrue();
        check.Should().Be("reachability");
    }

    [Fact]
    public async Task Probe_ACancelledToken_StillThrows_AndLogsNothing()
    {
        (WorkflowServiceProbe probe, InMemoryLogger<WorkflowServiceProbe> logger) =
            CreateProbe(fixture.Environment.Client, "default");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> act = () => probe.ProbeAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Records.Should().BeEmpty();
    }
}
