using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Workflows.Temporal.Health;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class WorkflowReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_ReportsHealthy()
    {
        var probe = Substitute.For<IWorkflowServiceProbe>();
        var health = new WorkflowServiceHealth
        {
            Reachable = true,
            NamespaceAddressable = true,
            WorkerPollersActive = true,
            TaskQueueBacklog = null,
            Latency = TimeSpan.FromMilliseconds(12),
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<WorkflowServiceHealth>.Success(health)));

        var healthCheck = new WorkflowReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task CheckHealthAsync_AnyBooleanFalse_ReportsUnhealthy_NeverDegraded(
        bool reachable, bool namespaceAddressable, bool workerPollersActive)
    {
        var probe = Substitute.For<IWorkflowServiceProbe>();
        var health = new WorkflowServiceHealth
        {
            Reachable = reachable,
            NamespaceAddressable = namespaceAddressable,
            WorkerPollersActive = workerPollersActive,
            TaskQueueBacklog = null,
            Latency = TimeSpan.FromMilliseconds(5),
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<WorkflowServiceHealth>.Success(health)));

        var healthCheck = new WorkflowReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeResultFails_ReportsUnhealthy()
    {
        var probe = Substitute.For<IWorkflowServiceProbe>();
        var error = Error.Unexpected("workflows.probe_failed", "Probe failed for the workflow service.");
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<WorkflowServiceHealth>.Failure(error)));

        var healthCheck = new WorkflowReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(error.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_AllThreeBooleansTrue_LargeTaskQueueBacklog_StillReportsHealthy()
    {
        // Dedicated regression: TaskQueueBacklog must never factor into the Healthy/Unhealthy
        // decision, even when it is large — a deep backlog means work is slow, not that the
        // service is unavailable, per 17.Workflows/CLAUDE.md's own explicit rule.
        var probe = Substitute.For<IWorkflowServiceProbe>();
        var health = new WorkflowServiceHealth
        {
            Reachable = true,
            NamespaceAddressable = true,
            WorkerPollersActive = true,
            TaskQueueBacklog = 500_000,
            Latency = TimeSpan.FromMilliseconds(9),
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<WorkflowServiceHealth>.Success(health)));

        var healthCheck = new WorkflowReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("taskQueueBacklog");
        result.Data["taskQueueBacklog"].Should().Be(500_000L);
    }

    [Fact]
    public async Task CheckHealthAsync_ClientOnlyRegistration_WorkerPollersActiveAlwaysTrue_ReportsHealthy()
    {
        // WorkflowServiceHealth's own contract guarantees WorkerPollersActive is always true on a
        // client-only (.AsClientOnly()) registration — "there are no pollers to fail." No separate
        // isWorkerHost parameter is needed; a single unconditional conjunct serves both shapes.
        var probe = Substitute.For<IWorkflowServiceProbe>();
        var health = new WorkflowServiceHealth
        {
            Reachable = true,
            NamespaceAddressable = true,
            WorkerPollersActive = true,
            TaskQueueBacklog = null,
            Latency = TimeSpan.FromMilliseconds(3),
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<WorkflowServiceHealth>.Success(health)));

        var healthCheck = new WorkflowReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
