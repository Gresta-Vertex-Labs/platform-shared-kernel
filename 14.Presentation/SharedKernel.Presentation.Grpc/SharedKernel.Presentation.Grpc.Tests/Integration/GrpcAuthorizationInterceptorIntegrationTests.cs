using SharedKernel.Execution.Context;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Integration tests for <c>GrpcAuthorizationInterceptor</c> over a real gRPC-over-HTTP2
/// in-process test server, proving <see cref="RequireRoleAttribute"/>/
/// <see cref="RequireFreshAuthenticationAttribute"/>/<see cref="RequireAuthenticationMethodAttribute"/>
/// applied directly to a gRPC service method are picked up via ASP.NET Core's endpoint-metadata
/// mechanism with no gRPC-specific wiring (T-72/T-73, WO-074/P-468).
/// </summary>
public class GrpcAuthorizationInterceptorIntegrationTests
{
    [Fact]
    public async Task RequireRole_CallerHasRole_MethodExecutes()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var authorized = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserContext>(new FakeUserContext { Roles = ["admin"] })));

        var client = GrpcClientHelper.CreateClient(authorized);

        var reply = await client.RequireRoleMethodAsync(new EchoRequest { Value = "ok" });

        reply.Value.Should().Be("ok");
    }

    [Fact]
    public async Task RequireRole_CallerLacksRole_RejectedWithPermissionDenied_MethodNeverExecutes()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var unauthorized = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserContext>(new FakeUserContext { Roles = [] })));

        var client = GrpcClientHelper.CreateClient(unauthorized);

        var act = async () => await client.RequireRoleMethodAsync(new EchoRequest { Value = "ok" });

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task RequireRole_AnonymousCaller_RejectedThroughOrdinaryHasRoleFalsePath()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var anonymous = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserContext>(new FakeUserContext { ActorKind = ActorKind.Anonymous, SubjectId = null, Roles = [] })));

        var client = GrpcClientHelper.CreateClient(anonymous);

        var act = async () => await client.RequireRoleMethodAsync(new EchoRequest { Value = "ok" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task RequireFreshAuthentication_RecentAuthTime_MethodExecutes()
    {
        var clock = new FakeClock();
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var fresh = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IUserContext>(new FakeUserContext { AuthTime = clock.UtcNow.AddSeconds(-10) });
        }));

        var client = GrpcClientHelper.CreateClient(fresh);

        var reply = await client.RequireFreshAuthMethodAsync(new EchoRequest { Value = "ok" });

        reply.Value.Should().Be("ok");
    }

    [Fact]
    public async Task RequireFreshAuthentication_StaleAuthTime_RejectedWithPermissionDenied()
    {
        var clock = new FakeClock();
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var stale = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IUserContext>(new FakeUserContext { AuthTime = clock.UtcNow.AddSeconds(-120) });
        }));

        var client = GrpcClientHelper.CreateClient(stale);

        var act = async () => await client.RequireFreshAuthMethodAsync(new EchoRequest { Value = "ok" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task RequireAuthenticationMethod_MatchingMethod_MethodExecutes()
    {
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var mfa = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserContext>(new FakeUserContext { AuthenticationMethods = ["mfa"] })));

        var client = GrpcClientHelper.CreateClient(mfa);

        var reply = await client.RequireAuthMethodMethodAsync(new EchoRequest { Value = "ok" });

        reply.Value.Should().Be("ok");
    }

    [Fact]
    public async Task RequireAuthenticationMethod_NonMatchingMethod_RejectedWithPermissionDenied()
    {
        // Regression proof (T-73): identical semantics to AuthorizationRequirementEndpointFilter's
        // HTTP-side RequireAuthenticationMethodAttribute evaluation.
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var noMfa = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserContext>(new FakeUserContext { AuthenticationMethods = ["pwd"] })));

        var client = GrpcClientHelper.CreateClient(noMfa);

        var act = async () => await client.RequireAuthMethodMethodAsync(new EchoRequest { Value = "ok" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task Echo_NoAuthorizationAttribute_NeverResolvesIUserContext()
    {
        // No-op proof: an endpoint with none of the four attributes must not require IUserContext
        // to be registered at all — mirrors AuthorizationRequirementEndpointFilterTests' identical
        // "empty container never resolves" technique.
        await using var factory = new GrpcTestWebApplicationFactory();
        await using var noUserContext = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Remove the base Startup's default IUserContext registration entirely.
            services.RemoveAll<IUserContext>();
        }));

        var client = GrpcClientHelper.CreateClient(noUserContext);

        var reply = await client.EchoAsync(new EchoRequest { Value = "no-auth-needed" });

        reply.Value.Should().Be("no-auth-needed");
    }
}
