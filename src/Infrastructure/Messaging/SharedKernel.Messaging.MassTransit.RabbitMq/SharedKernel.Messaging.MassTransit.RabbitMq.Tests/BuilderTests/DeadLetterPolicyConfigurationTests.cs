using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.RabbitMq.Transport;

namespace SharedKernel.Messaging.MassTransit.RabbitMq.Tests.BuilderTests;

/// <summary>
/// DL-01/DL-04: <see cref="DeadLetterOptions"/> default-value tests.
/// DL-02/DL-05: <see cref="RabbitMqMessagingTransport.ConfigureDeadLetterPolicy"/> — RabbitMQ
///              <c>x-message-ttl</c> wiring on the automatically-derived fault/dead-letter queues.
/// </summary>
/// <remarks>
/// <see cref="RabbitMqMessagingTransport.ConfigureDeadLetterPolicy"/> is <c>internal</c> (not
/// <c>private</c>) specifically so these tests can inspect the configurator state it produces
/// directly, via a substituted MassTransit configurator, mirroring
/// <c>ConcurrencyLimitConfigurationTests</c> (P-342). <c>IRabbitMqSendTopologyConfigurator
/// .ConfigureErrorSettings</c>/<c>.ConfigureDeadLetterSettings</c> are write-only (setter only, no
/// getter) on the real MassTransit interface, so the assigned delegate is captured via NSubstitute's
/// <c>Arg.Do&lt;T&gt;</c> and then invoked against a second substituted
/// <c>IRabbitMqQueueBindingConfigurator</c> to prove what it actually does, rather than merely
/// asserting that some delegate was assigned.
/// </remarks>
public sealed class DeadLetterPolicyConfigurationTests
{
    // -------------------------------------------------------------------------
    // DL-01/DL-04: DeadLetterOptions defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void DeadLetterOptions_Defaults_MatchMassTransitsOwnRabbitMqConvention()
    {
        var opts = new DeadLetterOptions();

        opts.QueueNameSuffix.Should().Be("_error",
            "the platform default must match MassTransit's own RabbitMQ error-queue naming " +
            "convention so nothing changes until explicitly overridden");
        opts.MessageTimeToLive.Should().BeNull("default is unbounded retention");
    }

    [Fact]
    public void DeadLetterOptions_SectionName_IsWellKnownConfigurationPath()
    {
        DeadLetterOptions.SectionName.Should().Be("SharedKernel:Messaging:DeadLetter");
    }

    // -------------------------------------------------------------------------
    // DL-02/DL-05: ConfigureDeadLetterPolicy — MessageTimeToLive wiring
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureDeadLetterPolicy_WithMessageTimeToLiveSet_AppliesXMessageTtlToErrorSettings()
    {
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var sendTopology = Substitute.For<IRabbitMqSendTopologyConfigurator>();
        cfg.SendTopology.Returns(sendTopology);

        // Arg.Do capture must be wired to the property setter BEFORE the exercise call — NSubstitute
        // invokes the callback at the moment the setter is actually invoked, not retroactively when
        // asserted via Received().
        Action<IRabbitMqQueueBindingConfigurator>? captured = null;
        sendTopology.ConfigureErrorSettings =
            Arg.Do<Action<IRabbitMqQueueBindingConfigurator>>(x => captured = x);

        var ttl = TimeSpan.FromMinutes(10);
        var opts = new DeadLetterOptions { MessageTimeToLive = ttl };

        RabbitMqMessagingTransport.ConfigureDeadLetterPolicy(cfg, opts);

        captured.Should().NotBeNull("a callback must be assigned when MessageTimeToLive is set");

        var errorQueueCfg = Substitute.For<IRabbitMqQueueBindingConfigurator>();
        captured!.Invoke(errorQueueCfg);

        errorQueueCfg.Received(1).SetQueueArgument("x-message-ttl", ttl);
    }

    [Fact]
    public void ConfigureDeadLetterPolicy_WithMessageTimeToLiveSet_AppliesXMessageTtlToDeadLetterSettings()
    {
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var sendTopology = Substitute.For<IRabbitMqSendTopologyConfigurator>();
        cfg.SendTopology.Returns(sendTopology);

        // Arg.Do capture must be wired to the property setter BEFORE the exercise call — see the
        // sibling ErrorSettings test above for why.
        Action<IRabbitMqQueueBindingConfigurator>? captured = null;
        sendTopology.ConfigureDeadLetterSettings =
            Arg.Do<Action<IRabbitMqQueueBindingConfigurator>>(x => captured = x);

        var ttl = TimeSpan.FromMinutes(10);
        var opts = new DeadLetterOptions { MessageTimeToLive = ttl };

        RabbitMqMessagingTransport.ConfigureDeadLetterPolicy(cfg, opts);

        captured.Should().NotBeNull("a callback must be assigned when MessageTimeToLive is set");

        var deadLetterQueueCfg = Substitute.For<IRabbitMqQueueBindingConfigurator>();
        captured!.Invoke(deadLetterQueueCfg);

        deadLetterQueueCfg.Received(1).SetQueueArgument("x-message-ttl", ttl);
    }

    [Fact]
    public void ConfigureDeadLetterPolicy_WithMessageTimeToLiveUnset_LeavesSendTopologyUnchanged()
    {
        var cfg = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        var sendTopology = Substitute.For<IRabbitMqSendTopologyConfigurator>();
        cfg.SendTopology.Returns(sendTopology);

        var opts = new DeadLetterOptions(); // MessageTimeToLive defaults to null

        RabbitMqMessagingTransport.ConfigureDeadLetterPolicy(cfg, opts);

        // The setter must never even be invoked when the option is unset — today's behavior
        // (MassTransit's own unmodified fault/dead-letter queue settings) is provably unchanged.
        sendTopology.Received(0).ConfigureErrorSettings =
            Arg.Any<Action<IRabbitMqQueueBindingConfigurator>>();
        sendTopology.Received(0).ConfigureDeadLetterSettings =
            Arg.Any<Action<IRabbitMqQueueBindingConfigurator>>();
    }

    // -------------------------------------------------------------------------
    // WithDeadLetterPolicy() builder guard tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WithDeadLetterPolicy_UnderRabbitMqTransport_BuildDoesNotThrow()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithDeadLetterPolicy(o =>
            {
                o.QueueNameSuffix = "-poison";
                o.MessageTimeToLive = TimeSpan.FromDays(7);
            })
            .Build();

        act.Should().NotThrow("WithDeadLetterPolicy with valid options must not throw during Build()");
    }

    [Fact]
    public void WithDeadLetterPolicy_WithDefaultOptions_BuildDoesNotThrow()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithDeadLetterPolicy()
            .Build();

        act.Should().NotThrow("WithDeadLetterPolicy with null configure must use defaults and not throw");
    }
}
