using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Integration tests for <c>GrpcExceptionInterceptor</c> over a real gRPC-over-HTTP2 in-process
/// test server — never a mocked <see cref="ServerCallContext"/> (T-70, WO-074/P-468).
/// </summary>
public class GrpcExceptionInterceptorIntegrationTests
{
    [Fact]
    public async Task KnownSharedKernelException_MapsToExpectedRpcException()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        var client = GrpcClientHelper.CreateClient(factory);

        var act = async () => await client.ThrowKnownAsync(new EchoRequest { Value = "x" });

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.NotFound);
        exception.Status.Detail.Should().Be("The requested test resource was not found.");
    }

    [Fact]
    public async Task UnknownException_InDevelopment_MapsToInternalWithUnsuppressedDetail()
    {
        // WebApplicationFactory defaults to the Development environment.
        await using var factory = new GrpcTestWebApplicationFactory();
        var client = GrpcClientHelper.CreateClient(factory);

        var act = async () => await client.ThrowUnknownAsync(new EchoRequest { Value = "x" });

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.Internal);
        exception.Status.Detail.Should().Be("boom");
    }

    [Fact]
    public async Task UnknownException_OutsideDevelopment_SuppressesDetail()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var productionFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment(Environments.Production));
        var client = GrpcClientHelper.CreateClient(productionFactory);

        var act = async () => await client.ThrowUnknownAsync(new EchoRequest { Value = "x" });

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.Internal);
        exception.Status.Detail.Should().Be("An unexpected error occurred.");
        exception.Status.Detail.Should().NotContain("boom");
    }

    [Fact]
    public async Task ServerStreamingCall_KnownException_MapsToExpectedRpcException()
    {
        // Proves the exception interceptor's ServerStreamingServerHandler override is genuinely
        // wired, not just UnaryServerHandler (T-70's explicit streaming-shape requirement).
        await using var factory = new GrpcTestWebApplicationFactory();
        var client = GrpcClientHelper.CreateClient(factory);

        using var call = client.StreamThrowKnown(new EchoRequest { Value = "x" });

        var receivedFirst = await call.ResponseStream.MoveNext(CancellationToken.None);
        receivedFirst.Should().BeTrue();
        call.ResponseStream.Current.Value.Should().Be("first");

        var act = async () => await call.ResponseStream.MoveNext(CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.Aborted);
        exception.Status.Detail.Should().Be("A conflicting test resource already exists.");
    }
}
