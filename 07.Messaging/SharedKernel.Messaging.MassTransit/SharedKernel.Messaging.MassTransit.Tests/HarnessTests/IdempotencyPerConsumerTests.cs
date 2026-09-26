using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Testing.Idempotency;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// A message consumed in more than one place in one service is deduplicated per consumer, never across consumers.
/// </summary>
/// <remarks>
/// Before the fix the reservation key was the message id alone. MassTransit runs the idempotency filter once per
/// consumer, so the first consumer completed the id and every other consumer of the same message in the service — on
/// another receive endpoint or on the same one — saw <see cref="IdempotencyReservationStatus.Completed"/> and was skipped
/// as a duplicate: its consumer never ran.
/// </remarks>
public sealed class IdempotencyPerConsumerTests
{
    private const string SharedEndpoint = "fan-out-shared";

    private static ServiceProvider BuildHarness(FakeIdempotencyStore store, bool sameEndpoint) =>
        new ServiceCollection()
            .AddKeyedSingleton<IIdempotencyStore>(IdempotencyPurpose.Message, store)
            .AddSingleton(MsOptions.Create(new IdempotencyOptions()))
            .AddMassTransitTestHarness(cfg =>
            {
                if (sameEndpoint)
                {
                    cfg.AddConsumer<FirstFanOutConsumer>().Endpoint(e => e.Name = SharedEndpoint);
                    cfg.AddConsumer<SecondFanOutConsumer>().Endpoint(e => e.Name = SharedEndpoint);
                }
                else
                {
                    // ConfigureEndpoints gives each consumer its own receive endpoint (queue).
                    cfg.AddConsumer<FirstFanOutConsumer>();
                    cfg.AddConsumer<SecondFanOutConsumer>();
                }

                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseIdempotentConsumers(ctx);
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoConsumersOfOneMessage_BothRunOnce_AndEachSkipsTheDuplicate(bool sameEndpoint)
    {
        FanOutTracker.Reset();
        var store = new FakeIdempotencyStore();

        await using var provider = BuildHarness(store, sameEndpoint);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();

        // First delivery: each consumer reserves and completes its own key (2 x begin + complete).
        await harness.Bus.Publish(new FanOutMessage("order-1"), p => p.MessageId = messageId);
        await WaitForCallsAsync(store, 4);

        FanOutTracker.First.Should().Be(1, "the first consumer must run for a new message");
        FanOutTracker.Second.Should().Be(1,
            "the second consumer of the same message must run too, not be skipped as the first one's duplicate");

        // Duplicate delivery: each consumer finds its own key completed (2 x begin) and is skipped.
        await harness.Bus.Publish(new FanOutMessage("order-1"), p => p.MessageId = messageId);
        await WaitForCallsAsync(store, 6);
        await harness.InactivityTask;

        FanOutTracker.First.Should().Be(1, "a duplicate must still be skipped by the first consumer");
        FanOutTracker.Second.Should().Be(1, "a duplicate must still be skipped by the second consumer");
        store.Calls.Should().HaveCount(6);

        Uri Endpoint(string name) => new($"loopback://localhost/{(sameEndpoint ? SharedEndpoint : name)}");
        store.Calls.Select(c => c.Key).Distinct().Should().BeEquivalentTo(
        [
            IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, Endpoint("FirstFanOut"), typeof(FirstFanOutConsumer).FullName),
            IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, Endpoint("SecondFanOut"), typeof(SecondFanOutConsumer).FullName),
        ]);

        await harness.Stop();
    }

    [Fact]
    public void Key_IsTheMessageIdPlusAFixedLengthHashOfEndpointAndConsumer()
    {
        const string Consumer = "Billing.OrderPlacedConsumer";
        var messageId = Guid.NewGuid();
        var queue = new Uri("rabbitmq://broker-a/vhost/orders-billing");

        var key = IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, queue, Consumer);

        key.Should().StartWith(messageId.ToString("D") + ":").And.HaveLength(36 + 1 + 64);
        key.Should().Be(IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, queue, Consumer),
            "the key must be deterministic so a redelivery finds the reservation");
        key.Should().Be(
            IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, new Uri("rabbitmq://broker-b/vhost/orders-billing"), Consumer),
            "only the endpoint's path counts, so moving the broker to another host keeps the keys");

        key.Should().NotBe(IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, queue, "Billing.OtherConsumer"));
        key.Should().NotBe(IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, new Uri("rabbitmq://broker-a/vhost/orders-audit"), Consumer));
        key.Should().NotBe(IdempotentConsumerBehavior<FanOutMessage>.CreateKey(Guid.NewGuid(), queue, Consumer));
        IdempotentConsumerBehavior<FanOutMessage>.CreateKey(messageId, null, null).Should().HaveLength(101);
    }

    private static async Task WaitForCallsAsync(FakeIdempotencyStore store, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (store.Calls.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        store.Calls.Count.Should().BeGreaterThanOrEqualTo(expected, "every delivery must settle before the next is sent");
    }
}

internal sealed record FanOutMessage(string Text);

internal static class FanOutTracker
{
    private static int _first;
    private static int _second;

    public static int First => _first;

    public static int Second => _second;

    public static void IncrementFirst() => Interlocked.Increment(ref _first);

    public static void IncrementSecond() => Interlocked.Increment(ref _second);

    public static void Reset()
    {
        Interlocked.Exchange(ref _first, 0);
        Interlocked.Exchange(ref _second, 0);
    }
}

internal sealed class FirstFanOutConsumer : ConsumerBase<FanOutMessage>
{
    public FirstFanOutConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(FanOutMessage message, CancellationToken ct)
    {
        FanOutTracker.IncrementFirst();
        return Task.CompletedTask;
    }
}

internal sealed class SecondFanOutConsumer : ConsumerBase<FanOutMessage>
{
    public SecondFanOutConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(FanOutMessage message, CancellationToken ct)
    {
        FanOutTracker.IncrementSecond();
        return Task.CompletedTask;
    }
}
