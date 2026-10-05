using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// RO-06: WithSendEndpointRoute override test — per-type route is used when registered.
/// RO-07: Convention fallback test — ConventionSendEndpointResolver used when no route registered.
/// </summary>
public sealed class RoutingTests
{
    // -------------------------------------------------------------------------
    // Test message types (internal to avoid MassTransit type-matching issues)
    // -------------------------------------------------------------------------

    internal sealed class ProcessPaymentCommand { }

    internal sealed class OrderCreatedCommand { }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static (IPublishEndpoint, ISendEndpointProvider, ISendEndpoint) CreateMocks()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sendEndpointProvider = Substitute.For<ISendEndpointProvider>();
        var sendEndpoint = Substitute.For<ISendEndpoint>();

        sendEndpointProvider
            .GetSendEndpoint(Arg.Any<Uri>())
            .Returns(Task.FromResult(sendEndpoint));

        sendEndpoint
            .Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        sendEndpoint
            .Send(Arg.Any<OrderCreatedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return (publishEndpoint, sendEndpointProvider, sendEndpoint);
    }

    private static MassTransitMessageBus CreateBus(
        ISendEndpointProvider sendEndpointProvider,
        IReadOnlyDictionary<Type, string> routeMap,
        string serviceName = "test-service")
    {
        var options = MsOptions.Create(new MessagingOptions { ServiceName = serviceName });
        var resolver = new ConventionSendEndpointResolver(options);

        return new MassTransitMessageBus(
            publishEndpoint: Substitute.For<IPublishEndpoint>(),
            sendEndpointProvider: sendEndpointProvider,
            serviceProvider: Substitute.For<IServiceProvider>(),
            routeMap: routeMap,
            resolver: resolver);
    }

    // -------------------------------------------------------------------------
    // RO-06: Per-type route override is used when registered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithRegisteredRoute_UsesOverrideQueueName()
    {
        // Arrange
        var (_, sendEndpointProvider, _) = CreateMocks();

        var capturedUri = default(Uri);
        await sendEndpointProvider
            .GetSendEndpoint(Arg.Do<Uri>(u => capturedUri = u));

        var routeMap = new Dictionary<Type, string>
        {
            [typeof(ProcessPaymentCommand)] = "payment-service-process-payment"
        };

        var bus = CreateBus(sendEndpointProvider, routeMap);

        // Act
        await bus.SendAsync(new ProcessPaymentCommand(), CancellationToken.None);

        // Assert
        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Contain("payment-service-process-payment",
            "the registered route override must be used for the command type");
    }

    [Fact]
    public async Task SendAsync_WithRegisteredRoute_GetSendEndpointCalledWithCorrectUri()
    {
        // Arrange
        var (_, sendEndpointProvider, _) = CreateMocks();

        var routeMap = new Dictionary<Type, string>
        {
            [typeof(ProcessPaymentCommand)] = "payment-service-process-payment"
        };

        var bus = CreateBus(sendEndpointProvider, routeMap);

        // Act
        await bus.SendAsync(new ProcessPaymentCommand(), CancellationToken.None);

        // Assert
        await sendEndpointProvider.Received(1)
            .GetSendEndpoint(Arg.Is<Uri>(u => u.ToString() == "queue:payment-service-process-payment"));
    }

    // -------------------------------------------------------------------------
    // RO-07: Convention fallback when no route registered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithNoRegisteredRoute_UsesConventionResolver()
    {
        // Arrange
        var (_, sendEndpointProvider, _) = CreateMocks();

        // Empty route map — no override for OrderCreatedCommand
        var routeMap = new Dictionary<Type, string>();

        var bus = CreateBus(sendEndpointProvider, routeMap, serviceName: "test-service");

        // Act
        await bus.SendAsync(new OrderCreatedCommand(), CancellationToken.None);

        // Assert: convention = {service-name}-{kebab-case-type-name}
        // KebabCaseEndpointNameFormatter.Instance.SanitizeName("OrderCreatedCommand") → "order-created-command"
        await sendEndpointProvider.Received(1)
            .GetSendEndpoint(Arg.Is<Uri>(u => u.ToString() == "queue:test-service-order-created-command"));
    }

    [Fact]
    public async Task SendAsync_WithNoRegisteredRoute_EndpointMatchesConventionResolverOutput()
    {
        // Arrange
        var (_, sendEndpointProvider, _) = CreateMocks();

        var capturedUri = default(Uri);
        await sendEndpointProvider
            .GetSendEndpoint(Arg.Do<Uri>(u => capturedUri = u));

        var routeMap = new Dictionary<Type, string>();
        var options = MsOptions.Create(new MessagingOptions { ServiceName = "order-service" });
        var resolver = new ConventionSendEndpointResolver(options);

        var bus = new MassTransitMessageBus(
            publishEndpoint: Substitute.For<IPublishEndpoint>(),
            sendEndpointProvider: sendEndpointProvider,
            serviceProvider: Substitute.For<IServiceProvider>(),
            routeMap: routeMap,
            resolver: resolver);

        // Act
        await bus.SendAsync(new OrderCreatedCommand(), CancellationToken.None);

        // Assert: resolver output must match what the bus uses
        var expectedUri = resolver.Resolve<OrderCreatedCommand>();
        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Be(expectedUri.ToString(),
            "SendAsync must use ConventionSendEndpointResolver output when no route is registered");
    }

    [Fact]
    public void ConventionSendEndpointResolver_Resolve_ReturnsServiceNamePrefixedKebabCaseName()
    {
        // Arrange
        var options = MsOptions.Create(new MessagingOptions { ServiceName = "payment-service" });
        var resolver = new ConventionSendEndpointResolver(options);

        // Act
        var result = resolver.Resolve<ProcessPaymentCommand>();

        // Assert: "payment-service" + "-" + SanitizeName("ProcessPaymentCommand")
        // KebabCaseEndpointNameFormatter strips nothing from non-Consumer/Event suffixes
        // P-560: the resolver returns a scheme-qualified Uri, not a bare queue name, so callers
        // cannot drift from the convention by re-adding the scheme themselves.
        result.ToString().Should().Be("queue:payment-service-process-payment-command",
            "convention resolver must prefix with ServiceName and convert type name to kebab-case");
    }

    [Fact]
    public async Task SendAsync_RouteOverride_TakesPrecedenceOverConvention()
    {
        // Arrange: register an override that differs from what the convention would produce
        var (_, sendEndpointProvider, _) = CreateMocks();

        var routeMap = new Dictionary<Type, string>
        {
            [typeof(ProcessPaymentCommand)] = "custom-queue-name"
        };

        var bus = CreateBus(sendEndpointProvider, routeMap, serviceName: "any-service");

        // Act
        await bus.SendAsync(new ProcessPaymentCommand(), CancellationToken.None);

        // Assert: override is used, not convention
        await sendEndpointProvider.Received(1)
            .GetSendEndpoint(Arg.Is<Uri>(u => u.ToString() == "queue:custom-queue-name"));
        await sendEndpointProvider.DidNotReceive()
            .GetSendEndpoint(Arg.Is<Uri>(u => u.ToString().Contains("any-service")));
    }

    // -------------------------------------------------------------------------
    // WithSendEndpointRoute builder validation
    // -------------------------------------------------------------------------

    [Fact]
    public void WithSendEndpointRoute_NullQueueName_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.WithSendEndpointRoute<ProcessPaymentCommand>(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithSendEndpointRoute_EmptyQueueName_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.WithSendEndpointRoute<ProcessPaymentCommand>(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithSendEndpointRoute_WhitespaceQueueName_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "test-service");

        var act = () => builder.WithSendEndpointRoute<ProcessPaymentCommand>("   ");

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // DI registration verification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Build_WithSendEndpointRoute_RegistersReadOnlyDictionarySingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithSendEndpointRoute<ProcessPaymentCommand>("payment-service-process-payment")
            .Build();

        // Act
        await using var sp = services.BuildServiceProvider();
        var routeMap = sp.GetService<IReadOnlyDictionary<Type, string>>();

        // Assert
        routeMap.Should().NotBeNull();
        routeMap!.Should().ContainKey(typeof(ProcessPaymentCommand));
        routeMap[typeof(ProcessPaymentCommand)].Should().Be("payment-service-process-payment");
    }

    [Fact]
    public async Task Build_WithNoRoutes_RegistersEmptyReadOnlyDictionary()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .Build();

        // Act
        await using var sp = services.BuildServiceProvider();
        var routeMap = sp.GetService<IReadOnlyDictionary<Type, string>>();

        // Assert
        routeMap.Should().NotBeNull();
        routeMap!.Should().BeEmpty();
    }
}
