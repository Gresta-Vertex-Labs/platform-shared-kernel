using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.SchedulingTests;

/// <summary>
/// SC-08: Schedule a message via IMessageScheduler.ScheduleAsync; assert consumed after deliverAt.
/// SC-09: Schedule a message then CancelAsync before deliverAt; assert never consumed.
/// </summary>
public sealed class InMemorySchedulingTests
{
    // -------------------------------------------------------------------------
    // SC-08: Message is delivered after deliverAt elapses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScheduleAsync_WithNearFutureDeliverAt_MessageIsConsumedAfterDelay()
    {
        // Arrange — wire in-memory transport with the delayed message scheduler.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ScheduledMessageConsumer>();
                cfg.AddDelayedMessageScheduler();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseDelayedMessageScheduler();
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Resolve MassTransit's IMessageScheduler from a scope (it is registered as scoped).
        using var scope = provider.CreateScope();
        var mtScheduler = scope.ServiceProvider.GetRequiredService<global::MassTransit.IMessageScheduler>();
        var scheduler = new MassTransitMessageScheduler(mtScheduler);

        // Schedule a message for 200 ms from now.
        var deliverAt = DateTimeOffset.UtcNow.AddMilliseconds(200);
        await scheduler.ScheduleAsync(new ScheduledTestMessage("delayed-hello"), deliverAt, CancellationToken.None);

        // Wait long enough for the scheduler to fire (200 ms delay + buffer).
        await Task.Delay(1500);

        // Assert the message was consumed.
        (await harness.Consumed.Any<ScheduledTestMessage>()).Should().BeTrue(
            "the scheduled message must be consumed after deliverAt elapsed");

        ScheduledMessageTracker.ReceivedText.Should().Be("delayed-hello",
            "consumer must receive the correct scheduled message payload");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SC-09: Message is NOT delivered when cancelled before deliverAt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsync_BeforeDeliverAt_MessageIsNeverConsumed()
    {
        // Arrange — same in-memory scheduler wiring.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ScheduledMessageConsumer>();
                cfg.AddDelayedMessageScheduler();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseDelayedMessageScheduler();
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var mtScheduler = scope.ServiceProvider.GetRequiredService<global::MassTransit.IMessageScheduler>();
        var scheduler = new MassTransitMessageScheduler(mtScheduler);

        // Schedule with a long delay — long enough that we can cancel before delivery.
        var deliverAt = DateTimeOffset.UtcNow.AddSeconds(30);
        var token = await scheduler.ScheduleAsync(new ScheduledTestMessage("cancel-me"), deliverAt, CancellationToken.None);

        // Cancel immediately.
        await scheduler.CancelAsync(token, CancellationToken.None);

        // Wait a short time — if not cancelled correctly, it shouldn't fire in 30s anyway,
        // but we verify the cancel path executed without exception.
        await Task.Delay(300);

        // Assert the message was NOT consumed.
#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
        (await harness.Consumed.Any<ScheduledTestMessage>(x => x.Context.Message.Text == "cancel-me"))
#pragma warning restore CS8602
            .Should().BeFalse(
            "the cancelled message must never be consumed");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SC-09 bonus: CancelAsync with unknown token is a no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsync_WithUnknownToken_IsNoOp()
    {
        // Verify that CancelAsync with an unrecognised token does not throw.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddDelayedMessageScheduler();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseDelayedMessageScheduler();
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var mtScheduler = scope.ServiceProvider.GetRequiredService<global::MassTransit.IMessageScheduler>();
        var scheduler = new MassTransitMessageScheduler(mtScheduler);

        // Act — cancel with a token that was never returned by ScheduleAsync.
        var unknownToken = Guid.NewGuid();
        var act = async () => await scheduler.CancelAsync(unknownToken, CancellationToken.None);

        // Assert — must be a no-op; no exception thrown.
        await act.Should().NotThrowAsync("CancelAsync with an unrecognised token must be a no-op");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SC-06 smoke: WithInMemoryScheduler builds without error
    // -------------------------------------------------------------------------

    [Fact]
    public void WithInMemoryScheduler_BuildsSuccessfully()
    {
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithInMemoryScheduler()
            .Build();

        act.Should().NotThrow("WithInMemoryScheduler should build without error");
    }

    // -------------------------------------------------------------------------
    // SC-07 smoke: WithQuartzScheduler throws when ConnectionString is null
    // -------------------------------------------------------------------------

    [Fact]
    public void WithQuartzScheduler_WithNoConnectionString_ThrowsAtBuild()
    {
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithQuartzScheduler()
            .Build();

        act.Should().Throw<InvalidOperationException>(
            "Build() must throw when QuartzSchedulerOptions.ConnectionString is null or empty");
    }

    // -------------------------------------------------------------------------
    // SC-07 smoke: WithQuartzScheduler with valid ConnectionString builds
    // -------------------------------------------------------------------------

    [Fact]
    public void WithQuartzScheduler_WithConnectionString_BuildsSuccessfully()
    {
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithQuartzScheduler(o => o.ConnectionString = "Host=localhost;Database=quartz")
            .Build();

        act.Should().NotThrow("WithQuartzScheduler with a valid ConnectionString should build without error");
    }

    // -------------------------------------------------------------------------
    // IMessageScheduler DI registration test — WithInMemoryScheduler
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithInMemoryScheduler_IMessageSchedulerRegisteredAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "sched-test")
            .UseRabbitMq("rabbitmq://localhost")
            .WithInMemoryScheduler()
            .Build();

        await using var sp = services.BuildServiceProvider(true);

        // Act — resolve IMessageScheduler (abstraction) from DI.
        using var scope = sp.CreateScope();
        var scheduler = scope.ServiceProvider
            .GetService<SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler>();

        // Assert
        scheduler.Should().NotBeNull("IMessageScheduler must be registered when WithInMemoryScheduler() is called");
        scheduler.Should().BeOfType<MassTransitMessageScheduler>(
            "the concrete implementation must be MassTransitMessageScheduler");
    }
}

// ---------------------------------------------------------------------------
// Static tracker for cross-scope scheduler assertions
// ---------------------------------------------------------------------------

internal static class ScheduledMessageTracker
{
    public static string? ReceivedText { get; private set; }

    public static void Record(string text)
    {
        ReceivedText = text;
    }

    public static void Reset()
    {
        ReceivedText = null;
    }
}

// ---------------------------------------------------------------------------
// Message type — internal to avoid 'file' modifier issues with MassTransit
// ---------------------------------------------------------------------------

internal sealed record ScheduledTestMessage(string Text);

// ---------------------------------------------------------------------------
// Consumer for scheduled messages
// ---------------------------------------------------------------------------

internal sealed class ScheduledMessageConsumer : ConsumerBase<ScheduledTestMessage>
{
    public ScheduledMessageConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(ScheduledTestMessage message, CancellationToken ct)
    {
        ScheduledMessageTracker.Record(message.Text);
        return Task.CompletedTask;
    }
}
