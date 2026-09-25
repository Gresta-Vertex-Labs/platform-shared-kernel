using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D3/D13/D16: the core's authorization attributes and conventions are native ASP.NET Core authorization, so
/// they work on gRPC methods with no gRPC-specific code — an anonymous caller gets <see cref="StatusCode.Unauthenticated"/>
/// (B2), a caller without the permission <see cref="StatusCode.PermissionDenied"/>, a caller whose sign-in is too old
/// <see cref="StatusCode.Unauthenticated"/> (the RFC 9470 step-up, as over HTTP), and the method never runs.
/// </summary>
public sealed class AuthorizationTests
{
    [Fact]
    public async Task B2_RequireEndpointPermission_AnonymousCaller_IsUnauthenticated()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var act = async () => await app.CreateClient().ReadOrdersAsync(new EchoRequest());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task RequireEndpointPermission_CallerWithoutThePermission_IsPermissionDenied()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var act = async () => await app.CreateClient().ReadOrdersAsync(
            new EchoRequest(),
            TestAuthentication.SignedIn(permissions: "orders.write"));

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task RequireEndpointPermission_CallerWithThePermission_Succeeds()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var reply = await app.CreateClient().ReadOrdersAsync(
            new EchoRequest(),
            TestAuthentication.SignedIn(permissions: $"orders.write, {TestAuthentication.ReadPermission}"));

        reply.Value.Should().Be("orders");
    }

    [Fact]
    public async Task RequireFreshAuthentication_StaleSignIn_IsUnauthenticated_AndARecentOneSucceeds()
    {
        await using var app = await GrpcTestHost.StartAsync();
        var client = app.CreateClient();

        var stale = async () => await client.ApproveOrderAsync(
            new EchoRequest(),
            TestAuthentication.SignedIn(authTime: DateTimeOffset.UtcNow.AddMinutes(-10)));
        var reply = await client.ApproveOrderAsync(
            new EchoRequest(),
            TestAuthentication.SignedIn(authTime: DateTimeOffset.UtcNow.AddSeconds(-5)));

        // A step-up refusal is HTTP 401 with the RFC 9470 challenge header and no body, as over HTTP, which gRPC
        // reports as Unauthenticated: the caller has to authenticate again, not ask for a permission.
        (await stale.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        reply.Value.Should().Be("approved");
    }

    [Fact]
    public async Task UnprotectedMethod_NeedsNoSignIn()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var reply = await app.CreateClient().EchoAsync(new EchoRequest { Value = "open" });

        reply.Value.Should().Be("open");
    }

    [Fact]
    public async Task MapGrpcServiceConvention_ProtectsEveryMethodOfTheService()
    {
        await using var app = await GrpcTestHost.StartAsync(
            configureService: service => service.RequireEndpointPermission(TestAuthentication.AdminPermission));
        var client = app.CreateClient();

        var anonymous = async () => await client.EchoAsync(new EchoRequest());
        var withoutPermission = async () => await client.EchoAsync(new EchoRequest(), TestAuthentication.SignedIn(permissions: TestAuthentication.ReadPermission));
        var reply = await client.EchoAsync(new EchoRequest { Value = "admin" }, TestAuthentication.SignedIn(permissions: TestAuthentication.AdminPermission));

        (await anonymous.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        (await withoutPermission.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        reply.Value.Should().Be("admin");
    }

    [Fact]
    public async Task MapGrpcServiceConvention_AnyOfItsPermissionsSuffices()
    {
        // OR within one requirement, as over HTTP.
        await using var app = await GrpcTestHost.StartAsync(
            configureService: service => service.RequireEndpointPermission(TestAuthentication.AdminPermission, "orders.support"));
        var client = app.CreateClient();

        var admin = await client.EchoAsync(new EchoRequest { Value = "a" }, TestAuthentication.SignedIn(permissions: TestAuthentication.AdminPermission));
        var support = await client.EchoAsync(new EchoRequest { Value = "s" }, TestAuthentication.SignedIn(permissions: "orders.support"));
        var neither = async () => await client.EchoAsync(new EchoRequest(), TestAuthentication.SignedIn(permissions: TestAuthentication.ReadPermission));

        admin.Value.Should().Be("a");
        support.Value.Should().Be("s");
        (await neither.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task GrpcOnlyHost_WithoutTheWebApiPipeline_EnforcesTheAttributes()
    {
        // AddSharedKernelGrpc registers the authorization itself; UseRouting/UseAuthentication/UseAuthorization apply it.
        await using var app = await GrpcTestHost.StartAsync(useWebApi: false);
        var client = app.CreateClient();

        var anonymous = async () => await client.ReadOrdersAsync(new EchoRequest());
        var withoutPermission = async () => await client.ReadOrdersAsync(new EchoRequest(), TestAuthentication.SignedIn(permissions: "orders.write"));
        var reply = await client.ReadOrdersAsync(new EchoRequest(), TestAuthentication.SignedIn(permissions: TestAuthentication.ReadPermission));

        (await anonymous.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        (await withoutPermission.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        reply.Value.Should().Be("orders");
    }

    [Fact]
    public async Task MapGrpcServiceConvention_AndMethodAttribute_BothApply()
    {
        await using var app = await GrpcTestHost.StartAsync(
            configureService: service => service.RequireEndpointPermission(TestAuthentication.AdminPermission));
        var client = app.CreateClient();

        var adminOnly = async () => await client.ReadOrdersAsync(new EchoRequest(), TestAuthentication.SignedIn(permissions: TestAuthentication.AdminPermission));
        var reply = await client.ReadOrdersAsync(
            new EchoRequest(),
            TestAuthentication.SignedIn(permissions: $"{TestAuthentication.AdminPermission},{TestAuthentication.ReadPermission}"));

        (await adminOnly.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        reply.Value.Should().Be("orders");
    }
}
