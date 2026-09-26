#pragma warning disable CS8601 // NSubstitute's "out Arg.Any<T>()" idiom for mocking TryGetPayload<T>
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

// Alias to avoid ambiguity with MassTransit.PublishContext in test types.
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// OD-04: <see cref="MessagingPublishContext.PartitionKey"/>/<c>WithPartitionKey</c> is covered in
/// <c>SharedKernel.Messaging.Abstractions.Tests.PublishContextTests</c>.
/// OD-08: RabbitMQ routing-key assignment — verified via a substituted <see cref="RoutingKeySendContext"/>
/// payload (<see cref="RoutingKeySendContext.RoutingKey"/> is setter-only), exercising the real, shipped
/// <see cref="PartitionKeySendContextExtensions.ApplyPartitionKey"/>. The Azure Service Bus session-identifier
/// half moved with that transport to <c>SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests</c> (P-570).
/// OD-09: Regression — omitting <c>PartitionKey</c> leaves behavior unchanged (no setter calls at the
/// unit level; unchanged end-to-end delivery at the harness level).
/// </summary>
public sealed class OrderedDeliveryTests
{
    // -------------------------------------------------------------------------
    // OD-08: PartitionKey set — routing key
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyPartitionKey_WithKey_SetsRabbitMqRoutingKey()
    {
        var sendContext = Substitute.For<SendContext>();
        var routingKeyContext = Substitute.For<RoutingKeySendContext>();
        sendContext.TryGetPayload(out Arg.Any<RoutingKeySendContext>())
            .Returns(callInfo =>
            {
                callInfo[0] = routingKeyContext;
                return true;
            });

        sendContext.ApplyPartitionKey("order-123");

        routingKeyContext.Received(1).RoutingKey = "order-123";
    }

    // -------------------------------------------------------------------------
    // OD-09: Regression — omitting PartitionKey leaves behavior unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyPartitionKey_NullKey_DoesNotSetRoutingKey()
    {
        var sendContext = Substitute.For<SendContext>();
        var routingKeyContext = Substitute.For<RoutingKeySendContext>();
        sendContext.TryGetPayload(out Arg.Any<RoutingKeySendContext>())
            .Returns(callInfo =>
            {
                callInfo[0] = routingKeyContext;
                return true;
            });

        sendContext.ApplyPartitionKey(null);

        // The pre-phase behavior (no routing-key assignment) is provably unchanged.
        routingKeyContext.Received(0).RoutingKey = Arg.Any<string>();
    }

    [Fact]
    public async Task PublishAsync_NoPartitionKey_DeliveryUnchanged()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderedDeliveryConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var bus = harness.Bus;

        // Act: publish with an explicit configure callback that never calls WithPartitionKey —
        // the pre-phase code path.
        await bus.Publish(
            new OrderedDeliveryMessage("no-partition-key", 0),
            _ => { });

        (await harness.Consumed.Any<OrderedDeliveryMessage>()).Should().BeTrue(
            "omitting PartitionKey must not affect ordinary delivery");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // End-to-end: PublishAsync with PartitionKey set delivers successfully (no exception,
    // no altered CorrelationId/header behavior) via the real MassTransitMessageBus implementation.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MassTransitMessageBus_PublishAsync_WithPartitionKey_DeliversSuccessfully()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderedDeliveryConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .AddSingleton<IReadOnlyDictionary<Type, string>>(
                new System.Collections.ObjectModel.ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()))
            .AddScoped<ConventionSendEndpointResolver>()
            .AddScoped<Abstractions.MessageBus.IMessageBus, MassTransitMessageBus>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Abstractions.MessageBus.IMessageBus>();

        var act = async () => await bus.PublishAsync(
            new OrderedDeliveryMessage("order-abc", 0),
            (MessagingPublishContext ctx) => ctx.WithPartitionKey("order-abc"),
            CancellationToken.None);

        await act.Should().NotThrowAsync("setting PartitionKey must never break delivery on any transport");

        (await harness.Consumed.Any<OrderedDeliveryMessage>()).Should().BeTrue();

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message type and consumer
// ---------------------------------------------------------------------------

internal sealed record OrderedDeliveryMessage(string Key, int Sequence);

internal sealed class OrderedDeliveryConsumer : IConsumer<OrderedDeliveryMessage>
{
    public Task Consume(ConsumeContext<OrderedDeliveryMessage> context) => Task.CompletedTask;
}
