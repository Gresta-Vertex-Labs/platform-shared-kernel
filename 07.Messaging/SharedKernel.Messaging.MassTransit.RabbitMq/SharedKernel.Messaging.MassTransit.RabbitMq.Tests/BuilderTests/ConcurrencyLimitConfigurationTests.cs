using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.RabbitMq.Transport;

namespace SharedKernel.Messaging.MassTransit.RabbitMq.Tests.BuilderTests;

/// <summary>
/// CC-08: <see cref="RabbitMqBusOptions.ConcurrentMessageLimit"/> must be applied to the
///        bus-level configurator when set; unset (<see langword="null"/>) must leave today's
///        behavior (no concurrency limit configured) provably unchanged (P-342).
/// </summary>
/// <remarks>
/// <see cref="RabbitMqMessagingTransport.ConfigureRabbitMq"/> is <c>internal</c> so these tests can inspect
/// the configurator state it produces directly, via a substituted MassTransit configurator.
/// <c>IBusFactoryConfigurator.ConcurrentMessageLimit</c>/<c>.PrefetchCount</c> are write-only (setter only, no
/// getter), so these tests verify via NSubstitute's <c>Received().Property = value</c> setter-call assertion.
/// </remarks>
public sealed class ConcurrencyLimitConfigurationTests
{
    // -------------------------------------------------------------------------
    // CC-08: RabbitMQ — global ConcurrentMessageLimit default
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureRabbitMq_WithConcurrentMessageLimitSet_AppliesToBusLevelConfigurator()
    {
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var opts = new RabbitMqBusOptions { ConcurrentMessageLimit = 8 };

        RabbitMqMessagingTransport.ConfigureRabbitMq(cfg, opts);

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
        RabbitMqMessagingTransport.ConfigureRabbitMq(cfg, opts);

        // Assert: the setter must never even be invoked when the option is unset — today's
        // behavior (no platform-imposed concurrency limit) is provably unchanged. Prefetch is
        // still applied, proving the ConcurrentMessageLimit guard did not short-circuit the
        // rest of the configuration.
        cfg.Received(0).ConcurrentMessageLimit = Arg.Any<int?>();
        cfg.Received(1).PrefetchCount = opts.Prefetch;
    }
}
