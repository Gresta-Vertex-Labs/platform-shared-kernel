using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Extensions;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// P-561: <c>AddSharedKernelMessaging(IConfiguration)</c> and the service-name shape rules
/// <c>MessagingOptionsValidator</c> enforces.
/// </summary>
/// <remarks>
/// The shape rules matter because the service name is not a label: it prefixes every queue and
/// exchange the service declares and is the CloudEvents <c>source</c> on every event it publishes.
/// Before P-561 the lowercase-slug rule existed only in documentation.
/// </remarks>
public sealed class ConfigurationBindingTests
{
    private static IConfiguration ConfigurationWith(string? serviceName) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(serviceName is null
                ? []
                : new Dictionary<string, string?>
                {
                    ["SharedKernel:Messaging:ServiceName"] = serviceName,
                })
            .Build();

    private static MessagingOptions Resolve(IConfiguration configuration, Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(configuration, configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    }

    // -------------------------------------------------------------------------
    // Binding
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelMessaging_WithConfiguration_BindsServiceNameFromSection()
    {
        Resolve(ConfigurationWith("order-service"))
            .ServiceName.Should().Be("order-service");
    }

    /// <summary>
    /// The root configuration is passed in, not a section: the section path comes from
    /// <see cref="MessagingOptions.SectionName"/>, so no call site can name the wrong one.
    /// </summary>
    [Fact]
    public void AddSharedKernelMessaging_ReadsSectionNameFromOptionsType()
    {
        MessagingOptions.SectionName.Should().Be("SharedKernel:Messaging");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsewhere:ServiceName"] = "wrong-section",
                [$"{MessagingOptions.SectionName}:ServiceName"] = "right-section",
            })
            .Build();

        Resolve(configuration).ServiceName.Should().Be("right-section");
    }

    /// <summary>The inline action runs after binding, so a host can override what configuration said.</summary>
    [Fact]
    public void AddSharedKernelMessaging_WithConfigurationAndAction_ActionOverridesBoundValue()
    {
        Resolve(ConfigurationWith("from-configuration"), o => o.ServiceName = "from-code")
            .ServiceName.Should().Be("from-code");
    }

    [Fact]
    public void AddSharedKernelMessaging_WithNullConfiguration_Throws()
    {
        var act = () => new ServiceCollection().AddSharedKernelMessaging((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The returned builder is the same fluent surface as the inline-action overload's, so the two
    /// entry points differ only in where the options come from.
    /// </summary>
    [Fact]
    public void AddSharedKernelMessaging_WithConfiguration_ReturnsUsableBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddSharedKernelMessaging(ConfigurationWith("order-service"));

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
    }

    // -------------------------------------------------------------------------
    // Startup validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// A missing section fails at startup, not at the first publish — the whole reason binding goes
    /// through <c>AddValidatedOptions</c> rather than <c>services.Configure</c>.
    /// </summary>
    [Fact]
    public void AddSharedKernelMessaging_WithMissingSection_FailsOnResolve()
    {
        var act = () => Resolve(ConfigurationWith(serviceName: null));

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ServiceName*");
    }

    [Theory]
    [InlineData("Order-Service", "a capital produces a queue name a broker may mangle")]
    [InlineData("order service", "a space is not legal in a queue name")]
    [InlineData("order_service", "underscores are excluded so one name works on every transport")]
    [InlineData("order.service", "a dot is legal on RabbitMQ but not on Azure Service Bus")]
    [InlineData("-order", "a leading hyphen produces an empty leading segment")]
    [InlineData("order-", "a trailing hyphen produces an empty trailing segment")]
    [InlineData("order--service", "a doubled hyphen produces an empty segment")]
    [InlineData("ordér-service", "non-ASCII is not safely round-tripped by every broker")]
    public void ServiceName_NotALowercaseSlug_FailsValidation(string serviceName, string because)
    {
        var act = () => Resolve(ConfigurationWith(serviceName));

        act.Should().Throw<OptionsValidationException>(because)
            .WithMessage("*lowercase slug*");
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("order-service")]
    [InlineData("orders-api-v2")]
    [InlineData("a1")]
    public void ServiceName_ValidSlug_PassesValidation(string serviceName)
    {
        Resolve(ConfigurationWith(serviceName)).ServiceName.Should().Be(serviceName);
    }

    /// <summary>
    /// The length cap leaves room for the consumer type name that is appended to form a queue name;
    /// without it, a long service name would fail at the broker rather than at startup.
    /// </summary>
    [Fact]
    public void ServiceName_LongerThanTheCap_FailsValidation()
    {
        var act = () => Resolve(ConfigurationWith(new string('a', 101)));

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*maximum is 100*");
    }

    [Fact]
    public void ServiceName_AtExactlyTheCap_PassesValidation()
    {
        var name = new string('a', 100);

        Resolve(ConfigurationWith(name)).ServiceName.Should().Be(name);
    }

    /// <summary>
    /// The service-name validator is registered once however many times the entry point is called,
    /// so one bad value is reported once rather than repeated in a startup exception.
    /// </summary>
    /// <remarks>
    /// Only this validator is counted. <c>AddValidatedOptions</c> registers a Data Annotations
    /// validator of the same service type alongside it, and that one is supposed to be there — the
    /// two run together by design, because validators resolve as a collection rather than as a
    /// single winner.
    /// </remarks>
    [Fact]
    public void AddSharedKernelMessaging_CalledTwice_RegistersOneServiceNameValidator()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelMessaging(ConfigurationWith("order-service"));
        services.AddSharedKernelMessaging(o => o.ServiceName = "order-service");

        services.Count(d =>
                d.ServiceType == typeof(IValidateOptions<MessagingOptions>)
                && d.ImplementationType == typeof(MessagingOptionsValidator))
            .Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Endpoint naming (P-561 regression)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The endpoint name formatter uses the ServiceName bound from configuration.
    /// </summary>
    /// <remarks>
    /// The regression guard for a defect <c>samples/ShippingApi</c> found against a real broker: the
    /// formatter used to be built from the inline-action value captured at registration, which is
    /// null on the configuration path — so a service configured through
    /// <c>SharedKernel:Messaging</c> declared queues with NO prefix (<c>hold-shipment</c> rather
    /// than <c>orders-api-hold-shipment</c>), and two such services sharing a broker would contend
    /// for the same queues. Nothing threw; the wrong queues were simply declared, which is why only
    /// a live broker exposed it.
    /// </remarks>
    [Fact]
    public void EndpointNameFormatter_OnTheConfigurationPath_UsesTheBoundServiceName()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(ConfigurationWith("orders-api"))
            .UseRabbitMq("rabbitmq://localhost")
            .AddConsumer<NamingProbeConsumer>()
            .Build();

        using var provider = services.BuildServiceProvider();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

        formatter.Consumer<NamingProbeConsumer>().Should().Be("orders-api-naming-probe");
    }

    /// <summary>
    /// The inline-action path produces the same name, so the two entry points are interchangeable.
    /// </summary>
    [Fact]
    public void EndpointNameFormatter_OnTheInlineActionPath_UsesTheSameName()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "orders-api")
            .UseRabbitMq("rabbitmq://localhost")
            .AddConsumer<NamingProbeConsumer>()
            .Build();

        using var provider = services.BuildServiceProvider();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

        formatter.Consumer<NamingProbeConsumer>().Should().Be("orders-api-naming-probe");
    }
}

/// <summary>A consumer whose only job is to have its endpoint name formatted.</summary>
internal sealed class NamingProbeConsumer : IConsumer<NamingProbeMessage>
{
    /// <inheritdoc />
    public Task Consume(ConsumeContext<NamingProbeMessage> context) => Task.CompletedTask;
}

/// <summary>The message <see cref="NamingProbeConsumer"/> consumes.</summary>
/// <param name="Text">Ignored.</param>
public sealed record NamingProbeMessage(string Text);
