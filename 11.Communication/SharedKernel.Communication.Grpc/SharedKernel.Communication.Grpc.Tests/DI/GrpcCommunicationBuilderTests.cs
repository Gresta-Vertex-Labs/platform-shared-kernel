using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Grpc.Builders;
using SharedKernel.Communication.Grpc.Extensions;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Communication.Grpc.Options;

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

    /// <summary>
    /// T-35 (P-359/WO-056): <c>GrpcClientOptionsValidator</c> must reject a non-positive
    /// <c>DeadlineSeconds</c> synchronously, at the <c>AddGrpcClient&lt;TClient&gt;</c> call site —
    /// registration time, not deferred to first call.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddGrpcClient_WithNonPositiveDeadlineSeconds_ThrowsOptionsValidationException(int deadlineSeconds)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelGrpcCommunication();

        // Act
        Action act = () => builder.AddGrpcClient<FakeGrpcClient>(
            "http://localhost:5001",
            o => o.DeadlineSeconds = deadlineSeconds);

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*DeadlineSeconds*");
    }

    /// <summary>
    /// T-35 (P-359/WO-056): proves a gRPC call issued through a client built via
    /// <c>AddGrpcClient&lt;TClient&gt;</c> is cancelled/faulted with <see cref="StatusCode.DeadlineExceeded"/>
    /// once the configured <c>DeadlineSeconds</c> elapses — a real, enforced per-call deadline, not
    /// merely a documented-but-unused option. Uses a primary <see cref="HttpMessageHandler"/> that
    /// never completes its response, deliberately delaying past the configured deadline.
    /// </summary>
    [Fact]
    public async Task AddGrpcClient_CallExceedingDeadline_ThrowsRpcExceptionWithDeadlineExceeded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSharedKernelGrpcCommunication()
            .AddGrpcClient<SlowGrpcTestClient>("http://localhost:50999", o => o.DeadlineSeconds = 1);

        // Grpc.Net.ClientFactory's AddGrpcClient<TClient> registers a named HttpClient keyed by
        // typeof(TClient).Name — calling the parameterless overload again against the SAME client
        // type is additive configuration on that same named client, not a fresh/conflicting
        // registration, letting this test attach a controllable primary handler without touching
        // production wiring.
        services.AddGrpcClient<SlowGrpcTestClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new NeverRespondingHandler());

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<SlowGrpcTestClient>();

        // Act
        Func<Task> act = async () => await client.SlowCallAsync("ping").ResponseAsync;

        // Assert
        var assertion = await act.Should().ThrowAsync<RpcException>();
        assertion.Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
    }
}

/// <summary>Minimal gRPC client stub for DI registration tests.</summary>
internal sealed class FakeGrpcClient(ChannelBase channel) : ClientBase<FakeGrpcClient>(channel)
{
    protected override FakeGrpcClient NewInstance(ClientBaseConfiguration configuration) => this;
}

/// <summary>Minimal gRPC client stub carrying one real unary method, for deadline-enforcement tests.</summary>
/// <remarks>
/// Constructed with a <see cref="CallInvoker"/>, not a <see cref="ChannelBase"/> — this is the
/// constructor shape <c>Grpc.Net.ClientFactory</c>'s <c>DefaultClientActivator&lt;T&gt;</c> actually
/// looks for when activating a typed client through DI (mirroring real generated gRPC client code).
/// </remarks>
internal sealed class SlowGrpcTestClient(CallInvoker callInvoker) : ClientBase<SlowGrpcTestClient>(callInvoker)
{
    private static readonly Method<string, string> SlowMethod = new(
        MethodType.Unary,
        "TestService",
        "SlowMethod",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    protected override SlowGrpcTestClient NewInstance(ClientBaseConfiguration configuration) => this;

    public AsyncUnaryCall<string> SlowCallAsync(string request, CallOptions options = default) =>
        CallInvoker.AsyncUnaryCall(SlowMethod, null, options, request);
}

/// <summary>Test double <see cref="HttpMessageHandler"/> that never completes its response.</summary>
internal sealed class NeverRespondingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable — the delay above never completes normally.");
    }
}
