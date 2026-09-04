using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Extensions;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Round-trip test proving <c>GrpcCorrelationInterceptor</c> reads the identical gRPC metadata
/// key <c>SharedKernel.Communication.Grpc</c>'s real client-side <c>CorrelationTracingInterceptor</c>
/// writes, and that <c>GrpcTenantContextInterceptor</c>'s <see cref="ITenantProvider"/>-resolution
/// contract (D-72) composes correctly with a tenant provider fed by the identical <c>x-tenant-id</c>
/// metadata key the real client-side <c>TenantIdInterceptor</c> writes — using the real client
/// interceptor types via <c>AddSharedKernelGrpcCommunication().AddGrpcClient&lt;TClient&gt;()</c>,
/// never a hand-rolled metadata stand-in (T-71, WO-074/P-468).
/// </summary>
public class GrpcCorrelationTenantRoundTripTests
{
    private static WebApplicationFactoryWrapper CreateServerWithHeaderTenantProvider()
    {
        var factory = new GrpcTestWebApplicationFactory();
        var customized = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHttpContextAccessor();
            // Overrides the base Startup's default FakeTenantProvider singleton — last
            // registration wins for single-instance DI resolution.
            services.AddSingleton<ITenantProvider, HeaderTenantProvider>();
        }));

        return new WebApplicationFactoryWrapper(factory, customized);
    }

    [Fact]
    public async Task RealClientInterceptors_PropagateCorrelationAndTenantId_ReadByServerInterceptors()
    {
        using var server = CreateServerWithHeaderTenantProvider();
        var expectedTenantId = Guid.NewGuid();

        // Client-side: a fake inbound HttpContext carrying an ITenantProvider, exactly what
        // TenantIdInterceptor resolves from via IHttpContextAccessor in a real service that is
        // itself forwarding an inbound request's tenant identity to a downstream gRPC call.
        var clientServices = new ServiceCollection();
        clientServices.AddSharedKernelGrpcCommunication()
            .AddGrpcClient<TestService.TestServiceClient>("http://localhost");

        // Route the generated client's HttpClient through the server's in-memory TestServer
        // instead of a real socket.
        clientServices.AddHttpClient(typeof(TestService.TestServiceClient).Name)
            .AddHttpMessageHandler(() => new ResponseVersionHandler())
            .ConfigurePrimaryHttpMessageHandler(() => server.Customized.Server.CreateHandler());

        await using var clientProvider = clientServices.BuildServiceProvider();

        var httpContextAccessor = clientProvider.GetRequiredService<IHttpContextAccessor>();
        var tenantServices = new ServiceCollection();
        tenantServices.AddSingleton<ITenantProvider>(new FakeTenantProvider(expectedTenantId));
        httpContextAccessor.HttpContext = new DefaultHttpContext { RequestServices = tenantServices.BuildServiceProvider() };

        var client = clientProvider.GetRequiredService<TestService.TestServiceClient>();

        var reply = await client.GetContextAsync(new EchoRequest { Value = "x" });

        reply.CorrelationId.Should().NotBeNullOrWhiteSpace();
        reply.TenantId.Should().Be(expectedTenantId.ToString());
    }

    [Fact]
    public async Task RealClientInterceptors_NoInboundHttpContext_TenantIdIsEmptyGuid()
    {
        // TenantIdInterceptor no-ops (silent) when IHttpContextAccessor.HttpContext is null — the
        // documented "background/non-request-scoped caller" case, so no x-tenant-id metadata is
        // ever sent. GrpcTenantContextInterceptor + HeaderTenantProvider must then resolve no
        // tenant, matching ITenantProvider's own Guid.Empty convention end-to-end.
        using var server = CreateServerWithHeaderTenantProvider();

        var clientServices = new ServiceCollection();
        clientServices.AddSharedKernelGrpcCommunication()
            .AddGrpcClient<TestService.TestServiceClient>("http://localhost");

        clientServices.AddHttpClient(typeof(TestService.TestServiceClient).Name)
            .AddHttpMessageHandler(() => new ResponseVersionHandler())
            .ConfigurePrimaryHttpMessageHandler(() => server.Customized.Server.CreateHandler());

        await using var clientProvider = clientServices.BuildServiceProvider();
        var client = clientProvider.GetRequiredService<TestService.TestServiceClient>();

        var reply = await client.GetContextAsync(new EchoRequest { Value = "x" });

        reply.TenantId.Should().Be(Guid.Empty.ToString());
        reply.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Owns both the original and <c>WithWebHostBuilder</c>-customized factory so both dispose together.</summary>
    private sealed class WebApplicationFactoryWrapper(
        GrpcTestWebApplicationFactory original,
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<GrpcTestWebApplicationFactory> customized) : IDisposable
    {
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<GrpcTestWebApplicationFactory> Customized { get; } = customized;

        public void Dispose()
        {
            Customized.Dispose();
            original.Dispose();
        }
    }
}
