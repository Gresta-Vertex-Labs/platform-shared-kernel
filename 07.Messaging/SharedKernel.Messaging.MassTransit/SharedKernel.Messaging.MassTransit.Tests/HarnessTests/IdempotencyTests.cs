#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// Behaviour of <see cref="IdempotentConsumerBehavior{TMessage}"/> against the atomic
/// reserve/complete/release contract introduced by P-560.
/// </summary>
/// <remarks>
/// The <see cref="IdempotencyReservationStatus.InProgress"/> case is the one the previous
/// two-method contract could not represent, and the reason a redelivery following a failed attempt
/// used to be acknowledged and dropped.
/// </remarks>
public sealed class IdempotencyTests
{
    private static ServiceProvider BuildHarness<TConsumer>(IIdempotencyStore store)
        where TConsumer : class, IConsumer =>
        new ServiceCollection()
            .AddSingleton(store)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<TConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

    // -------------------------------------------------------------------------
    // ID-06: AlreadyProcessed -> consumer NOT invoked, nothing completed or released
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AlreadyProcessedMessage_ConsumerBodyNotInvoked()
    {
        IdempotencyTracker.Reset();
        var store = Substitute.For<IIdempotencyStore>();
        store.TryBeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(IdempotencyReservation.AlreadyProcessed()));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyTestMessage("duplicate"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        await store.Received(1).TryBeginAsync(messageId, Arg.Any<CancellationToken>());

        IdempotencyTracker.ConsumeCount.Should().Be(0,
            "a message already consumed to completion must not run the consumer again");

        await store.DidNotReceive().CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // ID-07: Started -> consumer invoked, then completed with the issued token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartedReservation_ConsumerInvoked_ThenCompletedWithSameToken()
    {
        IdempotencyTracker.Reset();
        const string Token = "reservation-token-1";

        var store = Substitute.For<IIdempotencyStore>();
        store.TryBeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(IdempotencyReservation.Started(Token)));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyTestMessage("novel"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        IdempotencyTracker.ConsumeCount.Should().Be(1, "a started reservation must run the consumer exactly once");

        // The token must round-trip: a store uses it to reject a stale holder.
        await store.Received(1).CompleteAsync(messageId, Token, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // P-560: completion must not be cancellable - the consumer's effects are already durable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completion_IsCalledWithUncancellableToken()
    {
        IdempotencyTracker.Reset();
        var store = Substitute.For<IIdempotencyStore>();
        store.TryBeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(IdempotencyReservation.Started("t")));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new IdempotencyTestMessage("novel"), p => p.MessageId = Guid.NewGuid());
        await harness.InactivityTask;

        // A cancelled completion would leave finished work recorded as unprocessed, so it would run
        // again on the next delivery - the defect this assertion pins.
        await store.Received(1).CompleteAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<CancellationToken>(t => t == CancellationToken.None));

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // ID-09: consumer throws -> reservation released, never completed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumerThrows_ReservationReleased_NotCompleted()
    {
        IdempotencyTracker.Reset();
        const string Token = "reservation-token-2";

        var store = Substitute.For<IIdempotencyStore>();
        store.TryBeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(IdempotencyReservation.Started(Token)));

        await using var provider = BuildHarness<IdempotencyThrowingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyThrowingMessage("boom"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        // Without the release the id would stay reserved for the whole lease, and every redelivery
        // inside that window would be discarded as a duplicate - losing the message.
        await store.Received().ReleaseAsync(messageId, Token, Arg.Is<CancellationToken>(t => t == CancellationToken.None));
        await store.DidNotReceive().CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // P-560: InProgress -> filter throws so the message is never acknowledged
    // -------------------------------------------------------------------------
    //
    // Driven directly rather than through the harness: what matters is that the filter *throws*
    // instead of returning, and a harness assertion on a published Fault<T> would be testing
    // MassTransit's error pipeline rather than this filter's decision.

    [Fact]
    public async Task InProgressReservation_FilterThrows_AndConsumerPipeNeverRuns()
    {
        var messageId = Guid.NewGuid();
        var store = Substitute.For<IIdempotencyStore>();
        store.TryBeginAsync(messageId, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(IdempotencyReservation.InProgress()));

        var context = Substitute.For<ConsumeContext<IdempotencyFilterTestMessage>>();
        context.MessageId.Returns(messageId);
        context.CancellationToken.Returns(CancellationToken.None);

        var next = Substitute.For<IPipe<ConsumeContext<IdempotencyFilterTestMessage>>>();
        var filter = new IdempotentConsumerBehavior<IdempotencyFilterTestMessage>(store);

        var act = async () => await filter.Send(context, next);

        // Throwing leaves the message unacknowledged so the broker redelivers it. Returning instead
        // would acknowledge a message whose only in-flight attempt might still fail.
        await act.Should().ThrowAsync<ConcurrentMessageDeliveryException>();

        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<IdempotencyFilterTestMessage>>());
        await store.DidNotReceive().CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // ID-10: no MessageId -> filter passes straight through, store never consulted
    // -------------------------------------------------------------------------
    //
    // Also driven directly: MassTransit assigns a MessageId to everything published through the
    // bus, so this branch is unreachable from the harness. The test this replaced acknowledged as
    // much in a comment and then set a MessageId anyway, so it never covered the branch at all.

    [Fact]
    public async Task NullMessageId_PassesThrough_WithoutConsultingStore()
    {
        var store = Substitute.For<IIdempotencyStore>();

        var context = Substitute.For<ConsumeContext<IdempotencyFilterTestMessage>>();
        context.MessageId.Returns((Guid?)null);
        context.CancellationToken.Returns(CancellationToken.None);

        var next = Substitute.For<IPipe<ConsumeContext<IdempotencyFilterTestMessage>>>();
        var filter = new IdempotentConsumerBehavior<IdempotencyFilterTestMessage>(store);

        await filter.Send(context, next);

        // Nothing to deduplicate on, so the consumer still runs and the store is never touched.
        await next.Received(1).Send(context);
        await store.DidNotReceive().TryBeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // ID-08: WithIdempotency() without IIdempotencyStore — Build() throws
    // -------------------------------------------------------------------------

    [Fact]
    public void WithIdempotency_WithoutStoreRegistered_BuildThrowsInvalidOperationException()
    {
        // Arrange: no IIdempotencyStore registered in DI.
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency();

        // Act & Assert.
        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IIdempotencyStore is not registered*",
                "Build() must throw with diagnostic message about missing IIdempotencyStore");
    }

    [Fact]
    public void WithIdempotency_WithStoreRegistered_BuildSucceeds()
    {
        // Arrange: IIdempotencyStore IS registered.
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyStore, NoOpIdempotencyStore>();

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency()
            .Build();

        act.Should().NotThrow("Build() must succeed when IIdempotencyStore is registered");
    }

    [Fact]
    public void WithIdempotencyOptions_WithStoreRegistered_BuildSucceeds()
    {
        // Arrange: WithIdempotency(Action<>) overload with store registered.
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyStore, NoOpIdempotencyStore>();

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency(o => o.ExpiryWindow = TimeSpan.FromHours(48))
            .Build();

        act.Should().NotThrow("Build() must succeed with action overload and store registered");
    }

    [Fact]
    public void WithIdempotency_DiagnosticMessage_ContainsMissingRegistrationGuidance()
    {
        // Assert the diagnostic message contains actionable guidance.
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("AddScoped") || e.Message.Contains("YourImplementation"),
                "diagnostic message must guide the developer toward registering an implementation");
    }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record IdempotencyTestMessage(string Text);

/// <summary>
/// Message type for the tests that drive <c>IdempotentConsumerBehavior</c> directly.
/// </summary>
/// <remarks>
/// Public on purpose: Castle DynamicProxy, which NSubstitute uses, refuses to proxy
/// <c>ConsumeContext&lt;T&gt;</c> when <c>T</c> is internal and the strong-named
/// MassTransit.Abstractions assembly is involved. The harness-driven tests keep their internal
/// message types, which MassTransit's own type matching requires.
/// </remarks>
public sealed record IdempotencyFilterTestMessage(string Text);
internal sealed record IdempotencyThrowingMessage(string Text);

// ---------------------------------------------------------------------------
// Static tracker for cross-scope consumer invocation count
// ---------------------------------------------------------------------------

internal static class IdempotencyTracker
{
    private static int _consumeCount;

    public static int ConsumeCount => _consumeCount;

    public static void Increment() => Interlocked.Increment(ref _consumeCount);

    public static void Reset() => Interlocked.Exchange(ref _consumeCount, 0);
}

// ---------------------------------------------------------------------------
// Consumers — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class IdempotencyTrackingConsumer : ConsumerBase<IdempotencyTestMessage>
{
    public IdempotencyTrackingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(IdempotencyTestMessage message, CancellationToken ct)
    {
        IdempotencyTracker.Increment();
        return Task.CompletedTask;
    }
}

internal sealed class IdempotencyThrowingConsumer : ConsumerBase<IdempotencyThrowingMessage>
{
    public IdempotencyThrowingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(IdempotencyThrowingMessage message, CancellationToken ct)
        => throw new InvalidOperationException("intentional idempotency test failure");
}

// ---------------------------------------------------------------------------
// No-op IIdempotencyStore for build guard tests
// ---------------------------------------------------------------------------

internal sealed class NoOpIdempotencyStore : IIdempotencyStore
{
    public Task<IdempotencyReservation> TryBeginAsync(Guid messageId, CancellationToken ct)
        => Task.FromResult(IdempotencyReservation.Started(Guid.NewGuid().ToString("N")));

    public Task CompleteAsync(Guid messageId, string reservationToken, CancellationToken ct)
        => Task.CompletedTask;

    public Task ReleaseAsync(Guid messageId, string reservationToken, CancellationToken ct)
        => Task.CompletedTask;
}
