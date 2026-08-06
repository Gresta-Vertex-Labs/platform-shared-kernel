using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// CC-07: <see cref="AzureServiceBusOptions.MaxConcurrentCalls"/> must actually change the
///        configured bus-level receive-endpoint concurrency — previously read into the options
///        type but never consulted anywhere the bus was actually built (P-342).
/// CC-08: <see cref="RabbitMqBusOptions.ConcurrentMessageLimit"/> must be applied to the
///        bus-level configurator when set; unset (<see langword="null"/>) must leave today's
///        behavior (no concurrency limit configured) provably unchanged (P-342).
/// </summary>
/// <remarks>
/// Both <see cref="MessagingBusBuilder.ConfigureAzureServiceBus"/> and
/// <see cref="MessagingBusBuilder.ConfigureRabbitMq"/> are <c>internal</c> (not <c>private</c>)
/// specifically so these tests can inspect the configurator state each helper produces directly,
/// via a substituted MassTransit configurator, rather than merely proving the option value
/// round-trips through the options type itself.
/// <c>IBusFactoryConfigurator.ConcurrentMessageLimit</c>/<c>.PrefetchCount</c> are write-only
/// (setter only, no getter) on the bus-level configurator interface, so these tests verify via
/// NSubstitute's <c>Received().Property = value</c> setter-call assertion rather than reading
/// the property back.
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
        MessagingBusBuilder.ConfigureAzureServiceBus(cfg, opts);

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

        MessagingBusBuilder.ConfigureAzureServiceBus(cfg, opts);

        cfg.Received(1).ConcurrentMessageLimit = 1;
    }

    // -------------------------------------------------------------------------
    // CC-08: RabbitMQ — global ConcurrentMessageLimit default
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureRabbitMq_WithConcurrentMessageLimitSet_AppliesToBusLevelConfigurator()
    {
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var opts = new RabbitMqBusOptions { ConcurrentMessageLimit = 8 };

        MessagingBusBuilder.ConfigureRabbitMq(cfg, opts);

        cfg.Received(1).ConcurrentMessageLimit = 8;
        cfg.Received(1).PrefetchCount = opts.Prefetch;
    }

    [Fact]
    public void ConfigureRabbitMq_WithConcurrentMessageLimitUnset_LeavesBusLevelConfiguratorUnchanged()
    {
        // Arrange: default RabbitMqBusOptions — ConcurrentMessageLimit is null.
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var opts = new RabbitMqBusOptions();

        // Act
        MessagingBusBuilder.ConfigureRabbitMq(cfg, opts);

        // Assert: the setter must never even be invoked when the option is unset — today's
        // behavior (no platform-imposed concurrency limit) is provably unchanged. Prefetch is
        // still applied, proving the ConcurrentMessageLimit guard did not short-circuit the
        // rest of the configuration.
        cfg.Received(0).ConcurrentMessageLimit = Arg.Any<int?>();
        cfg.Received(1).PrefetchCount = opts.Prefetch;
    }
}
