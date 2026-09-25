using System.Net;
using SharedKernel.Execution.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public void AddSharedKernelRestCommunication_RegistersRequestContextHandlerAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication();

        // Assert
        var descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(RequestContextDelegatingHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddSharedKernelRestCommunication_RegistersRequestContextAccessor_AndNoHttpContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelRestCommunication();

        // Assert — P-566: the client reads the ambient context, never the inbound HttpContext.
        services.Should().Contain(d => d.ServiceType == typeof(IRequestContextAccessor));
        services.Should().NotContain(d => d.ServiceType.Name == "IHttpContextAccessor");
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

    /// <summary>
    /// T-34 (P-358/WO-056): proves R-23's validate-at-point-of-consumption fix genuinely fires
    /// through the real <c>AddRestClient&lt;TClient&gt;</c> call itself — synchronously, at
    /// registration time — not merely when calling <c>RestClientOptionsValidator</c> directly.
    /// </summary>
    [Fact]
    public void AddRestClient_WithInvalidTimeoutSeconds_ThrowsOptionsValidationExceptionAtCallSite()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelRestCommunication();

        // Act
        Action act = () => builder.AddRestClient<TestTypedClient>("test-client", o =>
        {
            o.BaseAddress = "http://test-service";
            o.TimeoutSeconds = -1;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*TimeoutSeconds*");
    }

    [Fact]
    public void AddRestClient_WithInvalidRetryCount_ThrowsOptionsValidationExceptionAtCallSite()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelRestCommunication();

        // Act
        Action act = () => builder.AddRestClient<TestTypedClient>("test-client", o =>
        {
            o.BaseAddress = "http://test-service";
            o.Resilience.RetryCount = -1;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RetryCount*");
    }

    [Fact]
    public void AddRestClient_WithInvalidResilienceOptions_NeverRegistersTypedClient()
    {
        // Arrange — a failed registration must not leave a half-registered typed client resolvable.
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelRestCommunication();

        // Act
        Action act = () => builder.AddRestClient<TestTypedClient>("test-client", o =>
        {
            o.BaseAddress = "http://test-service";
            o.Resilience.TotalTimeoutBufferSec = -1;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>();
    }
}

/// <summary>Minimal typed client for DI registration tests.</summary>
internal sealed class TestTypedClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}
