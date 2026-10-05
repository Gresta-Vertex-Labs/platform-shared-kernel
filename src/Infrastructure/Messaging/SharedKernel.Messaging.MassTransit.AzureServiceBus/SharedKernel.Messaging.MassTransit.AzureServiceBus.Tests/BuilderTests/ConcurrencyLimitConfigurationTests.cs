using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.Transport;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests.BuilderTests;

/// <summary>
/// CC-07: <see cref="AzureServiceBusOptions.MaxConcurrentCalls"/> must actually change the
///        configured bus-level receive-endpoint concurrency — previously read into the options
///        type but never consulted anywhere the bus was actually built (P-342).
/// </summary>
/// <remarks>
/// <see cref="AzureServiceBusMessagingTransport.ConfigureAzureServiceBus"/> is <c>internal</c> so these tests
/// can inspect the configurator state it produces directly, via a substituted MassTransit configurator.
/// <c>IBusFactoryConfigurator.ConcurrentMessageLimit</c> is write-only, so the tests verify via NSubstitute's
/// <c>Received().Property = value</c> setter-call assertion.
/// </remarks>
public sealed class ConcurrencyLimitConfigurationTests
{
    // -------------------------------------------------------------------------
    // CC-07: Azure Service Bus — MaxConcurrentCalls wiring fix
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureAzureServiceBus_AppliesMaxConcurrentCalls_ToBusLevelConcurrentMessageLimit()
    {
        // Arrange: a substituted bus factory configurator — no real ASB connection is attempted.
        var cfg = Substitute.For<IServiceBusBusFactoryConfigurator>();
        var opts = new AzureServiceBusOptions
        {
            ConnectionString =
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=",
            MaxConcurrentCalls = 25,
        };

        // Act
        AzureServiceBusMessagingTransport.ConfigureAzureServiceBus(cfg, opts);

        // Assert: proves the setter was actually invoked with this value — not merely that
        // AzureServiceBusOptions.MaxConcurrentCalls itself holds 25.
        // NOTE: IServiceBusEndpointConfigurator.MaxConcurrentCalls is obsolete in MassTransit 9.1.2
        // ("Set ConcurrentMessageLimit instead") — ConcurrentMessageLimit is the correct target,
        // and (like PrefetchCount) it is write-only on IBusFactoryConfigurator.
        cfg.Received(1).ConcurrentMessageLimit = 25;
    }

    [Fact]
    public void ConfigureAzureServiceBus_DefaultMaxConcurrentCalls_AppliesOneToConcurrentMessageLimit()
    {
        // AzureServiceBusOptions.MaxConcurrentCalls defaults to 1 (non-nullable) — the fix must
        // apply it unconditionally, exactly like RabbitMqBusOptions.Prefetch always applies.
        var cfg = Substitute.For<IServiceBusBusFactoryConfigurator>();
        var opts = new AzureServiceBusOptions
        {
            ConnectionString =
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=",
        };

        AzureServiceBusMessagingTransport.ConfigureAzureServiceBus(cfg, opts);

        cfg.Received(1).ConcurrentMessageLimit = 1;
    }
}
