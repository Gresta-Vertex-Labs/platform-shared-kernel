using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Builders;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.Handlers;

namespace SharedKernel.Communication.Rest.Tests.DI;

public sealed class RestCommunicationBuilderTests
{
    [Fact]
    public void AddSharedKernelRestCommunication_ReturnsBuilderWithServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddSharedKernelRestCommunication();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<IRestCommunicationBuilder>();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddSharedKernelRestCommunication_RegistersCorrelationHandlerAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication();

        // Assert
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(CorrelationIdDelegatingHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddSharedKernelRestCommunication_RegistersTenantHandlerAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication();

        // Assert
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(TenantIdDelegatingHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddSharedKernelRestCommunication_RegistersIHttpContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication();

        // Assert
        services.Any(d => d.ServiceType == typeof(IHttpContextAccessor))
            .Should().BeTrue("IHttpContextAccessor must be registered for TenantIdDelegatingHandler");
    }

    [Fact]
    public void AddRestClient_WithBaseAddress_RegistersTypedClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication()
            .AddRestClient<TestTypedClient>(
                "test-client",
                o => o.BaseAddress = "http://test-service");

        using var sp = services.BuildServiceProvider();

        // Assert
        var client = sp.GetService<TestTypedClient>();
        client.Should().NotBeNull("typed client should be resolvable from DI");
    }

    [Fact]
    public void AddRestClient_WithoutBaseAddressAndNoResolver_ThrowsInvalidOperation()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelRestCommunication();

        // Act
        Action act = () => builder.AddRestClient<TestTypedClient>("test-client");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BaseAddress*IServiceEndpointResolver*");
    }

    [Fact]
    public void AddRestClient_IsChainable_ReturnsSameBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelRestCommunication();

        // Act
        var returned = builder.AddRestClient<TestTypedClient>(
            "test-client",
            o => o.BaseAddress = "http://test-service");

        // Assert
        returned.Should().BeSameAs(builder, "AddRestClient must return 'this' for chaining");
    }
}

/// <summary>Minimal typed client for DI registration tests.</summary>
internal sealed class TestTypedClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}
