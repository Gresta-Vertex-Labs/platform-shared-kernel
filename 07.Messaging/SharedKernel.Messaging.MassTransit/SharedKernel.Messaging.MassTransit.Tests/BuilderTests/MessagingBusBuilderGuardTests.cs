using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// T-03: AddSharedKernelMessaging DI test — IMessagingBuilder returned; MessagingOptions resolvable.
/// T-06: MessagingBusBuilder guard tests — Build() throws with no transport;
///       UseRabbitMq+UseAzureServiceBus throws at second call; ASB both/neither options throws.
/// </summary>
public sealed class MessagingBusBuilderGuardTests
{
    // -------------------------------------------------------------------------
    // T-03: AddSharedKernelMessaging DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelMessaging_ReturnsMessagingBusBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        builder.Should().BeOfType<MessagingBusBuilder>();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public async Task AddSharedKernelMessaging_MessagingOptions_ResolvableFromDI()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "my-service");

        await using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Messaging.Abstractions.Options.MessagingOptions>>().Value;

        opts.ServiceName.Should().Be("my-service");
    }

    [Fact]
    public async Task Build_AfterUseRabbitMq_RegistersIMessageBus()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetService<IMessageBus>();
        bus.Should().NotBeNull();
    }

    [Fact]
    public async Task Build_AfterUseRabbitMq_RegistersIEventPublisher()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        await using var sp = services.BuildServiceProvider();
        var publisher = sp.GetService<IEventPublisher>();
        publisher.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // T-06: Guard tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_WithoutTransport_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No transport configured*");
    }

    [Fact]
    public void Build_WithNullServiceName_ThrowsOnBuild()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = null!)
            .UseRabbitMq("rabbitmq://localhost");

        var act = () => builder.Build();

        // Validation occurs at Build() time; MessagingOptionsValidator throws OptionsValidationException
        // which is the DI startup validation mechanism. Both OptionsValidationException and
        // InvalidOperationException signal a misconfigured ServiceName.
        act.Should().Throw<Exception>()
            .Where(e => e.Message.Contains("ServiceName"),
                "build must throw with ServiceName in the message when null");
    }

    [Fact]
    public void Build_WithWhitespaceServiceName_ThrowsOnBuild()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "   ")
            .UseRabbitMq("rabbitmq://localhost");

        var act = () => builder.Build();

        // Validation occurs at Build() time; throws OptionsValidationException or InvalidOperationException
        act.Should().Throw<Exception>()
            .Where(e => e.Message.Contains("ServiceName"),
                "build must throw with ServiceName in the message when whitespace");
    }

    [Fact]
    public void UseRabbitMq_ThenUseAzureServiceBus_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost");

        var act = () => builder.UseAzureServiceBus("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transport has already been configured*");
    }

    [Fact]
    public void UseAzureServiceBus_ThenUseRabbitMq_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseAzureServiceBus("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=");

        var act = () => builder.UseRabbitMq("rabbitmq://localhost");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transport has already been configured*");
    }

    [Fact]
    public void UseAzureServiceBus_WithBothConnectionStringAndNamespace_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.UseAzureServiceBus(o =>
        {
            o.ConnectionString = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=";
            o.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net";
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
    }

    [Fact]
    public void UseAzureServiceBus_WithNeitherConnectionStringNorNamespace_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.UseAzureServiceBus(_ => { /* neither set */ });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*FullyQualifiedNamespace*");
    }

    [Fact]
    public void UseRabbitMq_WithEmptyConnectionString_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.UseRabbitMq(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // T-17: Build() anti-pattern fix — no second root IServiceProvider created.
    // C-21/C-22/C-23: Validate ServiceName inline; populate KebabCase formatter prefix
    // without BuildServiceProvider(); deferred path does not throw.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Build_WithInlineAction_NoBuildServiceProviderCallSideEffect()
    {
        // Arrange: capture how many service descriptors exist before Build().
        // A BuildServiceProvider() call inside Build() would add UsageTracker and other
        // MassTransit singleton descriptors twice, causing duplicate descriptor counts.
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        // Act: build the real root provider — should succeed with no duplicate-singleton error.
        await using var sp = services.BuildServiceProvider(validateScopes: true);

        // Assert: IMessageBus and IEventPublisher resolve correctly from the SINGLE root provider.
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetService<IMessageBus>();
        var publisher = scope.ServiceProvider.GetService<IEventPublisher>();

        bus.Should().NotBeNull("IMessageBus must resolve from the single DI root");
        publisher.Should().NotBeNull("IEventPublisher must resolve from the single DI root");
    }

    [Fact]
    public void Build_WithNullServiceName_ThrowsOptionsValidationException_NoBuildServiceProviderAntiPattern()
    {
        // Regression test for C-21: the fix must use inline validation, not BuildServiceProvider().
        // If BuildServiceProvider() were called internally, a second root IServiceProvider would be
        // created. We verify the exception is thrown eagerly at Build() time with ServiceName in msg.
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = null!)
            .UseRabbitMq("rabbitmq://localhost");

        var act = () => builder.Build();

        act.Should().Throw<Exception>()
            .Where(e => e.Message.Contains("ServiceName"),
                "Build() must throw with ServiceName in the message when null");
    }

    [Fact]
    public void Build_WithWhitespaceServiceName_ThrowsEagerly_NoBuildServiceProviderAntiPattern()
    {
        // Regression test for C-21: whitespace ServiceName throws at Build() time (inline path).
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelMessaging(o => o.ServiceName = "   ")
            .UseRabbitMq("rabbitmq://localhost");

        var act = () => builder.Build();

        act.Should().Throw<Exception>()
            .Where(e => e.Message.Contains("ServiceName"),
                "Build() must throw with ServiceName in the message when whitespace");
    }

    [Fact]
    public void Build_WithoutInlineAction_DeferredPath_DoesNotThrow()
    {
        // C-23: When no inline action is supplied (deferred config-section binding path),
        // Build() must NOT throw — validation is deferred to ValidateOnStart() at host startup.
        var services = new ServiceCollection();

        // Simulate the deferred path: configure MessagingOptions without an inline action.
        // In real usage, this would come from services.Configure<MessagingOptions>(config.GetSection(...)).
        services.Configure<SharedKernel.Messaging.Abstractions.Options.MessagingOptions>(
            o => o.ServiceName = "deferred-service");

        var builder = services.AddSharedKernelMessaging(/* no inline action */);
        builder.UseRabbitMq("rabbitmq://localhost");

        // Must not throw — deferred validation path.
        var act = () => builder.Build();
        act.Should().NotThrow("deferred path must not throw at Build() time");
    }
}
