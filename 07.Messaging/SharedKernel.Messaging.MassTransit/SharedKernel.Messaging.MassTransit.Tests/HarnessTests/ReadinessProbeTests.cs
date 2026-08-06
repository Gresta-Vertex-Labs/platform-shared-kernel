using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// SK.07.ReadinessProbe / P-347: <c>MassTransitMessageBusProbe</c> exercised against a real
/// <c>MassTransit.Testing.TestHarness</c> bus (in-memory, no broker required) — proves
/// <c>ProbeAsync</c> reports <c>IsHealthy = true</c> once the bus has started, and
/// <c>IsHealthy = false</c> with a non-null diagnostic <c>Description</c> when the bus has never
/// started or has since been stopped. Registered manually here (rather than via the real
/// <c>MessagingBusBuilder.Build()</c>, which configures a real transport incompatible with
/// <c>AddMassTransitTestHarness()</c> in the same registration) — mirrors the established pattern
/// in <c>AmbientPropagationTests.cs</c>.
/// </summary>
public sealed class ReadinessProbeTests
{
    // -------------------------------------------------------------------------
    // RP-08: healthy, started bus
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_WithStartedBus_ReportsHealthy()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "rp-started-service");
        services.AddMassTransitTestHarness();
        services.AddSingleton<IMessageBusProbe, MassTransitMessageBusProbe>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            var probe = provider.GetRequiredService<IMessageBusProbe>();
            var health = await probe.ProbeAsync(CancellationToken.None);

            health.IsHealthy.Should().BeTrue("a started, connected bus must report healthy");
            health.Description.Should().BeNull("no diagnostic detail is expected when healthy");
        }
        finally
        {
            await harness.Stop();
        }
    }

    // -------------------------------------------------------------------------
    // RP-09: unhealthy — bus never started / bus stopped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_WithBusNeverStarted_ReportsUnhealthyWithDescription()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "rp-not-started-service");
        services.AddMassTransitTestHarness();
        services.AddSingleton<IMessageBusProbe, MassTransitMessageBusProbe>();

        await using var provider = services.BuildServiceProvider(true);

        // Deliberately never call harness.Start().
        var probe = provider.GetRequiredService<IMessageBusProbe>();
        var health = await probe.ProbeAsync(CancellationToken.None);

        health.IsHealthy.Should().BeFalse("a bus that was never started is not ready");
        health.Description.Should().NotBeNullOrWhiteSpace(
            "an unhealthy probe result must carry a human-readable diagnostic detail");
    }

    [Fact]
    public async Task ProbeAsync_WithStoppedBus_ReportsUnhealthyWithDescription()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "rp-stopped-service");
        services.AddMassTransitTestHarness();
        services.AddSingleton<IMessageBusProbe, MassTransitMessageBusProbe>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Stop();

        var probe = provider.GetRequiredService<IMessageBusProbe>();
        var health = await probe.ProbeAsync(CancellationToken.None);

        health.IsHealthy.Should().BeFalse("a stopped bus is not ready");
        health.Description.Should().NotBeNullOrWhiteSpace(
            "an unhealthy probe result must carry a human-readable diagnostic detail");
    }
}
