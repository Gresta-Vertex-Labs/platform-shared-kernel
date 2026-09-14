using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Messaging.Tests.HealthChecks;

/// <summary>
/// Covers the P-351 gating acceptance criterion: <see cref="MessagingReadinessHealthCheck"/>'s
/// constructor accepts only <see cref="IMessageBusProbe"/> — there is no
/// <c>RabbitMQ.Client</c>/<c>Azure.Messaging.ServiceBus</c>/AMQP-URI/connection-string surface
/// anywhere on this type or in this test class's dependency graph. A passing test here is only
/// possible because the check reflects the real configured bus via the probe, never an
/// independently constructed connection.
/// </summary>
public sealed class MessagingReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeReportsHealthy_ReportsHealthy()
    {
        var probe = Substitute.For<IMessageBusProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MessageBusHealth(true, null)));

        var healthCheck = new MessagingReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeReportsUnhealthy_ReportsUnhealthy_NeverDegraded_SurfacesDescription()
    {
        var probe = Substitute.For<IMessageBusProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MessageBusHealth(false, "Bus not started.")));

        var healthCheck = new MessagingReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
        result.Description.Should().Be("Bus not started.");
    }

    [Fact]
    public async Task CheckHealthAsync_PropagatesCancellationTokenToProbe()
    {
        var probe = Substitute.For<IMessageBusProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MessageBusHealth(true, null)));
        using var cts = new CancellationTokenSource();

        var healthCheck = new MessagingReadinessHealthCheck(probe);
        await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        await probe.Received(1).ProbeAsync(cts.Token);
    }
}
