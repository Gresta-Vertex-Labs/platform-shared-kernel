#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Testing.Idempotency;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// Behaviour of <see cref="IdempotentConsumerBehavior{TMessage}"/> against the unified
/// <see cref="IIdempotencyStore"/> reservation contract, resolved for <see cref="IdempotencyPurpose.Message"/>.
/// </summary>
/// <remarks>
/// The <see cref="IdempotencyReservationStatus.InProgress"/> case is the one a single "seen" flag cannot
/// represent, and the reason a redelivery following a failed attempt used to be acknowledged and dropped.
/// </remarks>
public sealed class IdempotencyTests
{
    private const IdempotencyPurpose Message = IdempotencyPurpose.Message;

    private static ServiceProvider BuildHarness<TConsumer>(IIdempotencyStore store)
        where TConsumer : class, IConsumer =>
        new ServiceCollection()
            .AddKeyedSingleton(Message, store)
            .AddSingleton(MsOptions.Create(new IdempotencyOptions()))
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<TConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseIdempotentConsumers(ctx);
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

    // The key the filter reserves for a message delivered to a consumer on its default in-memory receive endpoint.
    private static string KeyFor<TConsumer>(Guid messageId, string queue) =>
        IdempotentConsumerBehavior<IdempotencyTestMessage>.CreateKey(
            messageId, new Uri($"loopback://localhost/{queue}"), typeof(TConsumer).FullName);

    private static IdempotentConsumerBehavior<IdempotencyFilterTestMessage> Filter(IIdempotencyStore store) =>
        new(store, MsOptions.Create(new IdempotencyOptions()));

    private static void ReturnsOnBegin(IIdempotencyStore store, IdempotencyReservation reservation) =>
        store.TryBeginAsync(Arg.Any<IdempotencyPurpose>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(reservation));

    // -------------------------------------------------------------------------
    // ID-06: Completed -> consumer NOT invoked, nothing completed or released
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompletedMessage_ConsumerBodyNotInvoked()
    {
        IdempotencyTracker.Reset();
        var store = Substitute.For<IIdempotencyStore>();
        ReturnsOnBegin(store, IdempotencyReservation.Completed(null));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyTestMessage("duplicate"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        await store.Received(1).TryBeginAsync(
            Message, KeyFor<IdempotencyTrackingConsumer>(messageId, "IdempotencyTracking"), IdempotentConsumerBehavior<IdempotencyTestMessage>.MessageFingerprint,
            new IdempotencyOptions().LeaseDuration, Arg.Any<CancellationToken>());

        IdempotencyTracker.ConsumeCount.Should().Be(0,
            "a message already consumed to completion must not run the consumer again");

        await store.DidNotReceiveWithAnyArgs().CompleteAsync(default, default!, default!, default, default, default);
        await store.DidNotReceiveWithAnyArgs().ReleaseAsync(default, default!, default!, default);

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
        ReturnsOnBegin(store, IdempotencyReservation.Started(Token));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyTestMessage("novel"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        IdempotencyTracker.ConsumeCount.Should().Be(1, "a started reservation must run the consumer exactly once");

        // The token must round-trip: a store uses it to reject a stale holder. No response is stored for a
        // message, and the retention is the configured expiry window.
        await store.Received(1).CompleteAsync(
            Message, KeyFor<IdempotencyTrackingConsumer>(messageId, "IdempotencyTracking"), Token, null, new IdempotencyOptions().ExpiryWindow, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().ReleaseAsync(default, default!, default!, default);

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
        ReturnsOnBegin(store, IdempotencyReservation.Started("t"));

        await using var provider = BuildHarness<IdempotencyTrackingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new IdempotencyTestMessage("novel"), p => p.MessageId = Guid.NewGuid());
        await harness.InactivityTask;

        // A cancelled completion would leave finished work recorded as unprocessed, so it would run
        // again on the next delivery - the defect this assertion pins.
        await store.Received(1).CompleteAsync(
            Message, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(),
            Arg.Is<CancellationToken>(t => t == CancellationToken.None));

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
        ReturnsOnBegin(store, IdempotencyReservation.Started(Token));

        await using var provider = BuildHarness<IdempotencyThrowingConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(new IdempotencyThrowingMessage("boom"), p => p.MessageId = messageId);
        await harness.InactivityTask;

        // Without the release the id would stay reserved for the whole lease, and every redelivery
        // inside that window would be discarded as a duplicate - losing the message.
        await store.Received().ReleaseAsync(
            Message, KeyFor<IdempotencyThrowingConsumer>(messageId, "IdempotencyThrowing"), Token, Arg.Is<CancellationToken>(t => t == CancellationToken.None));
        await store.DidNotReceiveWithAnyArgs().CompleteAsync(default, default!, default!, default, default, default);

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // P-568: a redelivery after a failed attempt is consumed, a duplicate after success is not
    // -------------------------------------------------------------------------
    //
    // Against the real reservation protocol (FakeIdempotencyStore) rather than a substitute: the
    // same message id is delivered three times — the first attempt fails, the redelivery succeeds,
    // and the third delivery is a true duplicate.

    [Fact]
    public async Task RedeliveryAfterFailedAttempt_IsConsumed_AndLaterDuplicateIsSkipped()
    {
        FlakyIdempotencyConsumer.Reset();
        var store = new FakeIdempotencyStore();

        await using var provider = BuildHarness<FlakyIdempotencyConsumer>(store);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Store calls each delivery settles with: begin + release, begin + complete, begin (duplicate).
        int[] settledCallCounts = [2, 4, 5];
        var messageId = Guid.NewGuid();
        foreach (var expectedCalls in settledCallCounts)
        {
            await harness.Bus.Publish(new FlakyIdempotencyMessage("order-1"), p => p.MessageId = messageId);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (store.Calls.Count < expectedCalls && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            store.Calls.Count.Should().Be(expectedCalls, "each delivery must settle before the next one is sent");
        }

        FlakyIdempotencyConsumer.Attempts.Should().Be(2,
            "the failed first attempt released the id, so the redelivery ran; the third delivery was a duplicate");
        FlakyIdempotencyConsumer.Successes.Should().Be(1);

        var replay = await store.TryBeginAsync(
            Message, KeyFor<FlakyIdempotencyConsumer>(messageId, "FlakyIdempotency"), IdempotentConsumerBehavior<FlakyIdempotencyMessage>.MessageFingerprint,
            TimeSpan.FromSeconds(30), CancellationToken.None);
        replay.Status.Should().Be(IdempotencyReservationStatus.Completed);

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
        ReturnsOnBegin(store, IdempotencyReservation.InProgress());

        var context = Substitute.For<ConsumeContext<IdempotencyFilterTestMessage>>();
        context.MessageId.Returns(messageId);
        context.CancellationToken.Returns(CancellationToken.None);

        var next = Substitute.For<IPipe<ConsumeContext<IdempotencyFilterTestMessage>>>();
        var filter = Filter(store);

        var act = async () => await filter.Send(context, next);

        // Throwing leaves the message unacknowledged so the broker redelivers it. Returning instead
        // would acknowledge a message whose only in-flight attempt might still fail.
        await act.Should().ThrowAsync<ConcurrentMessageDeliveryException>();

        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<IdempotencyFilterTestMessage>>());
        await store.DidNotReceiveWithAnyArgs().CompleteAsync(default, default!, default!, default, default, default);
        await store.DidNotReceiveWithAnyArgs().ReleaseAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task FingerprintMismatch_ForAMessage_IsAStoreDefect_AndTheConsumerNeverRuns()
    {
        var store = Substitute.For<IIdempotencyStore>();
        ReturnsOnBegin(store, IdempotencyReservation.FingerprintMismatch());

        var context = Substitute.For<ConsumeContext<IdempotencyFilterTestMessage>>();
        context.MessageId.Returns(Guid.NewGuid());
        context.CancellationToken.Returns(CancellationToken.None);

        var next = Substitute.For<IPipe<ConsumeContext<IdempotencyFilterTestMessage>>>();

        var act = async () => await Filter(store).Send(context, next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<IdempotencyFilterTestMessage>>());
    }

    // -------------------------------------------------------------------------
    // ID-10: no MessageId -> filter passes straight through, store never consulted
    // -------------------------------------------------------------------------
    //
    // Also driven directly: MassTransit assigns a MessageId to everything published through the
    // bus, so this branch is unreachable from the harness.

    [Fact]
    public async Task NullMessageId_PassesThrough_WithoutConsultingStore()
    {
        var store = Substitute.For<IIdempotencyStore>();

        var context = Substitute.For<ConsumeContext<IdempotencyFilterTestMessage>>();
        context.MessageId.Returns((Guid?)null);
        context.CancellationToken.Returns(CancellationToken.None);

        var next = Substitute.For<IPipe<ConsumeContext<IdempotencyFilterTestMessage>>>();
        var filter = Filter(store);

        await filter.Send(context, next);

        // Nothing to deduplicate on, so the consumer still runs and the store is never touched.
        await next.Received(1).Send(context);
        await store.DidNotReceiveWithAnyArgs().TryBeginAsync(default, default!, default!, default, default);
    }

    // -------------------------------------------------------------------------
    // ID-08: WithIdempotency() without a Message store — Build() throws
    // -------------------------------------------------------------------------

    [Fact]
    public void WithIdempotency_WithoutStoreRegistered_BuildThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No IIdempotencyStore is registered for IdempotencyPurpose.Message*",
                "Build() must throw with diagnostic message about the missing message store");
    }

    [Fact]
    public void WithIdempotency_WithOnlyARequestStore_BuildThrows()
    {
        // A store registered for requests does not guard messages.
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NoOpIdempotencyStore>(IdempotencyPurpose.Request);

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency()
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IdempotencyPurpose.Message*");
    }

    [Fact]
    public void WithIdempotency_WithStoreRegistered_BuildSucceeds()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NoOpIdempotencyStore>(IdempotencyPurpose.Message);

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency()
            .Build();

        act.Should().NotThrow("Build() must succeed when a message store is registered");
    }

    [Fact]
    public void WithIdempotencyOptions_WithStoreRegistered_BuildSucceeds_AndOptionsFlowToTheFilter()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NoOpIdempotencyStore>(IdempotencyPurpose.Message);

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency(o =>
            {
                o.ExpiryWindow = TimeSpan.FromHours(48);
                o.LeaseDuration = TimeSpan.FromMinutes(2);
            })
            .Build();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdempotencyOptions>>().Value;
        options.ExpiryWindow.Should().Be(TimeSpan.FromHours(48));
        options.LeaseDuration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void WithIdempotencyOptions_LeaseNotShorterThanExpiry_BuildThrows()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<NoOpIdempotencyStore>(IdempotencyPurpose.Message);

        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency(o => o.LeaseDuration = TimeSpan.FromDays(2))
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*LeaseDuration*");
    }

    [Fact]
    public void WithIdempotency_DiagnosticMessage_ContainsMissingRegistrationGuidance()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithIdempotency();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("AddRedisIdempotency") || e.Message.Contains("AddIdempotencyStore"),
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

internal sealed record FlakyIdempotencyMessage(string Text);

/// <summary>Fails its first attempt and succeeds afterwards, counting both.</summary>
internal sealed class FlakyIdempotencyConsumer : ConsumerBase<FlakyIdempotencyMessage>
{
    private static int _attempts;
    private static int _successes;

    public FlakyIdempotencyConsumer() : base(NullLogger.Instance) { }

    public static int Attempts => _attempts;

    public static int Successes => _successes;

    public static void Reset()
    {
        Interlocked.Exchange(ref _attempts, 0);
        Interlocked.Exchange(ref _successes, 0);
    }

    protected override Task ConsumeAsync(FlakyIdempotencyMessage message, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _attempts) == 1)
            throw new InvalidOperationException("intentional first-attempt failure");

        Interlocked.Increment(ref _successes);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// No-op IIdempotencyStore for build guard tests
// ---------------------------------------------------------------------------

internal sealed class NoOpIdempotencyStore : IIdempotencyStore
{
    public Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
        => Task.FromResult(IdempotencyReservation.Started(Guid.NewGuid().ToString("N")));

    public Task<bool> CompleteAsync(
        IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
