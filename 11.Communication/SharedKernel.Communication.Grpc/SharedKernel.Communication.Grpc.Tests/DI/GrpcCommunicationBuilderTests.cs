using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Builders;
using SharedKernel.Communication.Grpc.Extensions;
using SharedKernel.Communication.Grpc.Interceptors;

namespace SharedKernel.Communication.Grpc.Tests.DI;

public sealed class GrpcCommunicationBuilderTests
{
    [Fact]
    public void AddSharedKernelGrpcCommunication_RegistersIHttpContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSharedKernelGrpcCommunication();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<IHttpContextAccessor>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelGrpcCommunication_RegistersCorrelationTracingInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSharedKernelGrpcCommunication();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<CorrelationTracingInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelGrpcCommunication_RegistersTenantIdInterceptor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSharedKernelGrpcCommunication();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<TenantIdInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelGrpcCommunication_ReturnsIGrpcCommunicationBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddSharedKernelGrpcCommunication();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<IGrpcCommunicationBuilder>();
    }

    [Fact]
    public void AddGrpcClient_WithAddress_RegistersTypedGrpcClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSharedKernelGrpcCommunication()
            .AddGrpcClient<FakeGrpcClient>("http://localhost:5001");

        var sp = services.BuildServiceProvider();

        // Assert — GrpcClientFactory should be registered
        sp.GetService<GrpcClientFactory>().Should().NotBeNull(
            "AddGrpcClient must register GrpcClientFactory");
    }

    [Fact]
    public void AddGrpcClient_WithoutAddressAndNoResolver_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var builder = services.AddSharedKernelGrpcCommunication();

        // Assert — must throw when no address and no IServiceEndpointResolver
        Action act = () => builder.AddGrpcClient<FakeGrpcClient>(string.Empty);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Address*IServiceEndpointResolver*");
    }

    [Fact]
    public void AddGrpcClient_ReturnsSameBuilderForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelGrpcCommunication();

        // Act
        var result = builder.AddGrpcClient<FakeGrpcClient>("http://localhost:5001");

        // Assert
        result.Should().BeSameAs(builder, "AddGrpcClient must return the same builder for fluent chaining");
    }

    [Fact]
    public void AddSharedKernelGrpcCommunication_CalledTwice_DoesNotDoubleRegisterInterceptors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSharedKernelGrpcCommunication();
        services.AddSharedKernelGrpcCommunication();

        var sp = services.BuildServiceProvider();

        // Assert — TryAddSingleton means second call is no-op
        var correlationInterceptors = sp.GetServices<CorrelationTracingInterceptor>().ToList();
        correlationInterceptors.Should().HaveCount(1,
            "TryAddSingleton prevents double-registration of CorrelationTracingInterceptor");
    }
}

/// <summary>Minimal gRPC client stub for DI registration tests.</summary>
internal sealed class FakeGrpcClient(ChannelBase channel) : ClientBase<FakeGrpcClient>(channel)
{
    protected override FakeGrpcClient NewInstance(ClientBaseConfiguration configuration) => this;
}
