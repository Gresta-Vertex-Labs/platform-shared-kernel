#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.Abstractions.Faults;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// R-07: Circuit breaker TestHarness tests.
/// R-08: Fault consumer TestHarness tests.
/// </summary>
public sealed class ResilienceTests
{
    // -------------------------------------------------------------------------
    // R-07: Circuit breaker — consumer always throws; verify faults published
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CircuitBreaker_ConsumerAlwaysThrows_FaultsArePublished()
    {
        // Arrange: configure with low thresholds for fast tripping.
        // TripThreshold=2 means after 2 consecutive failures the breaker trips.
        // ActiveThreshold=1 means evaluation starts after 1 active message.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CircuitBreakerAlwaysFailConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    // Retry inner (0 retries = fail immediately), then circuit breaker outer.
                    busCfg.UseMessageRetry(r => r.Immediate(0));
                    busCfg.UseCircuitBreaker(cb =>
                    {
                        cb.TripThreshold = 2;
                        cb.ActiveThreshold = 1;
                        cb.ResetInterval = TimeSpan.FromSeconds(60);
                        cb.TrackingPeriod = TimeSpan.FromSeconds(60);
                    });
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish messages that all fail.
        await harness.Bus.Publish(new CircuitBreakerMessage("msg-1"));
        await harness.Bus.Publish(new CircuitBreakerMessage("msg-2"));
        await harness.Bus.Publish(new CircuitBreakerMessage("msg-3"));

        // Assert: faults are published because consumer always throws.
        (await harness.Published.Any<Fault<CircuitBreakerMessage>>()).Should().BeTrue(
            "consumer exceptions must produce Fault<TMessage> messages");

        await harness.Stop();
    }

    [Fact]
    public async Task CircuitBreaker_WithMessagingBusBuilder_WithCircuitBreakerConfigured_BuildsSuccessfully()
    {
        // Verify that WithCircuitBreaker() method on the builder compiles and executes without error.
        // This is a smoke test for the builder integration (R-05).
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithRetry()
            .WithCircuitBreaker(o =>
            {
                o.TripThreshold = 3;
                o.ActiveThreshold = 5;
                o.ResetInterval = TimeSpan.FromSeconds(30);
                o.TrackingPeriod = TimeSpan.FromSeconds(30);
            })
            .Build();

        act.Should().NotThrow("WithCircuitBreaker with valid options should not throw during Build()");
    }

    [Fact]
    public async Task CircuitBreaker_WithDefaultOptions_BuildsSuccessfully()
    {
        // Verify WithCircuitBreaker() with null configure (defaults) works.
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithCircuitBreaker()
            .Build();

        act.Should().NotThrow("WithCircuitBreaker with null configure should use defaults and not throw");
    }

    // -------------------------------------------------------------------------
    // R-08: Fault consumer — wire AddFaultConsumer; publish message causing fault;
    //        assert fault consumer HandleAsync invoked.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FaultConsumer_WhenMessageCausesFault_HandleAsyncIsInvoked()
    {
        // Reset tracking state.
        FaultTracker.Reset();

        // Arrange: configure with an always-failing consumer and a fault consumer adapter.
        // The bus is configured with zero retries so fault is generated immediately.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<FaultableMessageConsumer>();
                // Register the fault consumer adapter for Fault<FaultableMessage>.
                cfg.AddConsumer<FaultConsumerAdapter<FaultableMessage, TrackingFaultConsumer>>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(0));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            // Register the IFaultConsumer<FaultableMessage> implementation.
            .AddScoped<TrackingFaultConsumer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish a message that will cause an unhandled exception.
        var testMessage = new FaultableMessage("cause-fault");
        await harness.Bus.Publish(testMessage);

        // Wait for the fault to be consumed by FaultConsumerAdapter.
        (await harness.Consumed.Any<Fault<FaultableMessage>>()).Should().BeTrue(
            "FaultConsumerAdapter must consume the Fault<TMessage> envelope");

        // Assert: the IFaultConsumer<T>.HandleAsync was called.
        FaultTracker.WasCalled.Should().BeTrue(
            "IFaultConsumer<TMessage>.HandleAsync must be invoked by FaultConsumerAdapter");

        FaultTracker.ReceivedMessage.Should().NotBeNull(
            "HandleAsync must receive the faulted message");
        FaultTracker.ReceivedMessage!.Text.Should().Be("cause-fault",
            "HandleAsync must receive the original faulted message payload");

        FaultTracker.ReceivedFaultId.Should().NotBe(Guid.Empty,
            "HandleAsync must receive a non-empty FaultId");

        FaultTracker.ReceivedExceptions.Should().NotBeEmpty(
            "HandleAsync must receive the exception array from Fault<T>.Exceptions");

        await harness.Stop();
    }

    [Fact]
    public async Task FaultConsumer_ExceptionInfoMappedCorrectly_ExceptionTypeAndMessageSet()
    {
        // Reset tracking state.
        FaultTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<FaultableMessageConsumer>();
                cfg.AddConsumer<FaultConsumerAdapter<FaultableMessage, TrackingFaultConsumer>>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(0));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .AddScoped<TrackingFaultConsumer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new FaultableMessage("exception-info-check"));
        await harness.Consumed.Any<Fault<FaultableMessage>>();

        // Assert exception mapping.
        FaultTracker.ReceivedExceptions.Should().NotBeEmpty();
        FaultTracker.ReceivedExceptions![0].ExceptionType.Should().Contain("InvalidOperationException",
            "ExceptionType must contain the CLR type name from Fault<T>.Exceptions");
        FaultTracker.ReceivedExceptions[0].Message.Should().Contain("faultable consumer failure",
            "Exception Message must be mapped from Fault<T>.Exceptions[].Message");

        await harness.Stop();
    }

    [Fact]
    public async Task AddFaultConsumer_ViaMessagingBusBuilder_BuildsWithoutError()
    {
        // Smoke test: AddFaultConsumer<TMessage, TFaultConsumer>() via the builder (R-06).
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithRetry()
            .AddConsumer<FaultableMessageConsumer>()
            .AddFaultConsumer<FaultableMessage, TrackingFaultConsumer>()
            .Build();

        act.Should().NotThrow("AddFaultConsumer with valid types should not throw during Build()");
    }
}

// ---------------------------------------------------------------------------
// Message types — internal to avoid 'file' modifier issues with MassTransit
// ---------------------------------------------------------------------------

internal sealed record CircuitBreakerMessage(string Text);
internal sealed record FaultableMessage(string Text);

// ---------------------------------------------------------------------------
// Static tracker for cross-scope test assertions
// ---------------------------------------------------------------------------

internal static class FaultTracker
{
    public static bool WasCalled { get; private set; }
    public static FaultableMessage? ReceivedMessage { get; private set; }
    public static Guid ReceivedFaultId { get; private set; }
    public static FaultExceptionInfo[]? ReceivedExceptions { get; private set; }

    public static void Record(Guid faultId, FaultableMessage message, FaultExceptionInfo[] exceptions)
    {
        WasCalled = true;
        ReceivedFaultId = faultId;
        ReceivedMessage = message;
        ReceivedExceptions = exceptions;
    }

    public static void Reset()
    {
        WasCalled = false;
        ReceivedMessage = null;
        ReceivedFaultId = Guid.Empty;
        ReceivedExceptions = null;
    }
}

// ---------------------------------------------------------------------------
// Consumer that always throws — triggers fault pipeline
// ---------------------------------------------------------------------------

internal sealed class CircuitBreakerAlwaysFailConsumer : ConsumerBase<CircuitBreakerMessage>
{
    public CircuitBreakerAlwaysFailConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(CircuitBreakerMessage message, CancellationToken ct)
    {
        throw new InvalidOperationException("circuit breaker test failure");
    }
}

internal sealed class FaultableMessageConsumer : ConsumerBase<FaultableMessage>
{
    public FaultableMessageConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(FaultableMessage message, CancellationToken ct)
    {
        throw new InvalidOperationException("faultable consumer failure");
    }
}

// ---------------------------------------------------------------------------
// IFaultConsumer implementation that records invocation for assertions
// ---------------------------------------------------------------------------

internal sealed class TrackingFaultConsumer : IFaultConsumer<FaultableMessage>
{
    public Task HandleAsync(
        Guid faultId,
        DateTimeOffset faultTimestamp,
        FaultableMessage faultedMessage,
        FaultExceptionInfo[] exceptions,
        CancellationToken ct)
    {
        FaultTracker.Record(faultId, faultedMessage, exceptions);
        return Task.CompletedTask;
    }
}
