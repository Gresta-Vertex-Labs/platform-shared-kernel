#pragma warning disable CS8601 // NSubstitute's "out Arg.Any<T>()" idiom for mocking TryGetPayload<T>
using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.Transport;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests.BuilderTests;

/// <summary>
/// OD-08: Azure Service Bus session-identifier assignment for a publish-time partition key — verified via a
/// substituted <see cref="ServiceBusSendContext"/> payload, since <see cref="ServiceBusSendContext.SessionId"/> is
/// setter-only and MassTransit's in-memory transport never attaches that payload. Moved from the core
/// <c>OrderedDeliveryTests</c> with the transport (P-570).
/// </summary>
public sealed class PartitionKeySessionIdTests
{
    private static readonly AzureServiceBusMessagingTransport Transport = new(new AzureServiceBusOptions
    {
        ConnectionString =
            "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=",
    });

    [Fact]
    public void ApplyPartitionKey_WithKey_SetsAzureServiceBusSessionId()
    {
        // Arrange: a SendContext substitute whose TryGetPayload<ServiceBusSendContext> resolves to
        // a second substitute representing the (ASB-transport-only) session-capable payload —
        // exactly what MassTransit.ServiceBusSendContextExtensions.SetSessionId looks up internally.
        var sendContext = Substitute.For<SendContext>();
        var serviceBusContext = Substitute.For<ServiceBusSendContext>();
        sendContext.TryGetPayload(out Arg.Any<ServiceBusSendContext>())
            .Returns(callInfo =>
            {
                callInfo[0] = serviceBusContext;
                return true;
            });

        // Act
        Transport.ApplyPartitionKey(sendContext, "order-123");

        // Assert: proves the setter was actually invoked with this value.
        serviceBusContext.Received(1).SessionId = "order-123";
    }

    [Fact]
    public void ApplyPartitionKey_MissingAzureServiceBusPayload_DoesNotThrow()
    {
        // Under a non-ASB transport (RabbitMQ, in-memory), TryGetPayload<ServiceBusSendContext>
        // returns false (default substitute behavior — not configured to return true). Applying the
        // partition key must remain a safe, silent no-op for the ASB half of the mapping.
        var sendContext = Substitute.For<SendContext>();

        var act = () => Transport.ApplyPartitionKey(sendContext, "order-123");

        act.Should().NotThrow();
    }
}
