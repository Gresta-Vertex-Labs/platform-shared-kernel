using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.MassTransit.Extensions;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests.BuilderTests;

/// <summary>
/// T-06: Azure Service Bus builder guards — one transport per bus; ASB both/neither options throws.
/// Moved from the core <c>MessagingBusBuilderGuardTests</c> with the transport (P-570).
/// </summary>
public sealed class AzureServiceBusBuilderGuardTests
{
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
}
