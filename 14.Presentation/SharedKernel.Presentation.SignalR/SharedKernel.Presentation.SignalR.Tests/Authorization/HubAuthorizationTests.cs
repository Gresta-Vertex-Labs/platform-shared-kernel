using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Authorization;

/// <summary>
/// Design D3/D12/D16: the WebApi requirements on a hub method, on the hub class and on <c>MapHub&lt;T&gt;()</c>.
/// SignalR on its own reads only <c>[Authorize]</c> on hub methods, so without this package's filter a
/// <c>[RequirePermission]</c> there would let every connected caller through (the B1 fail-open class, on hubs).
/// </summary>
public sealed class HubAuthorizationTests
{
    private const string Unauthenticated = $"{ErrorCodes.Unauthorized.Default}: Authentication is required to access this resource.";

    private const string Forbidden = $"{ErrorCodes.Forbidden.InsufficientPermission}: You are not permitted to perform this operation.";

    private const string StepUp = $"{PresentationErrorCodes.StepUpRequired}: This operation requires a more recent or stronger authentication.";

    [Theory]
    [InlineData("orders.read")]
    [InlineData("orders.admin")]
    public async Task HubMethod_AnyPermissionOfOneAttribute_Suffices(string permission)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: permission));

        var orders = await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.ReadOrders));

        orders.Should().Be("orders");
    }

    [Fact]
    public async Task HubMethod_MissingPermission_IsRefused_WithoutRunningTheMethod_OrNamingThePermission()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.write"));

        var message = await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.ReadOrders));

        message.Should().Be(Forbidden);
        message.Should().NotContain("orders.read").And.NotContain("orders.admin");
        app.Services.GetRequiredService<InvocationCounter>().Count.Should().Be(0);
    }

    [Fact]
    public async Task HubMethod_AnonymousCaller_IsRefused_AsUnauthenticated()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.MethodAuthorization);

        var message = await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.ReadOrders));

        message.Should().Be(Unauthenticated);
    }

    [Theory]
    [InlineData("orders.read", null, false)]
    [InlineData(null, "auditor", false)]
    [InlineData("orders.read", "auditor", true)]
    public async Task HubMethod_RequirementsOfSeveralAttributes_MustAllHold(string? permission, string? role, bool allowed)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: permission, roles: role));

        if (allowed)
        {
            (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Audit))).Should().Be("audited");
        }
        else
        {
            (await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.Audit))).Should().Be(Forbidden);
        }
    }

    [Fact]
    public async Task HubMethod_StaleAuthentication_IsAskedToStepUp()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(authTime: DateTimeOffset.UtcNow.AddHours(-1)));

        var message = await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.Transfer));

        message.Should().Be(StepUp);
    }

    [Fact]
    public async Task HubMethod_FreshAuthentication_Passes()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(authTime: DateTimeOffset.UtcNow));

        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Transfer))).Should().Be("transferred");
    }

    [Theory]
    [InlineData("pwd", false)]
    [InlineData("pwd,mfa", true)]
    public async Task HubMethod_AuthenticationMethod_IsRequired(string methods, bool allowed)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(methods: methods));

        if (allowed)
        {
            (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.ChangePassword))).Should().Be("changed");
        }
        else
        {
            (await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.ChangePassword))).Should().Be(StepUp);
        }
    }

    [Fact]
    public async Task HubMethod_WithoutRequirements_IsOpenToAnyConnectedCaller()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.MethodAuthorization);

        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Open))).Should().Be("open");
    }

    [Fact]
    public async Task HubMethod_Refusal_IsLoggedAtWarning_WithoutPrincipalData()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(user: "alice-the-user", permissions: "orders.write"));

        await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.ReadOrders));

        var record = loggerFactory.GetLogger(typeof(HubMethodAuthorizationFilter).FullName!).Records
            .ShouldHaveLogged(new EventId(14105), LogLevel.Warning);
        record.Message.Should().Contain(ErrorCodes.Forbidden.InsufficientPermission).And.NotContain("alice-the-user");
    }

    [Fact]
    public async Task HubMethod_RequirementsAreEnforced_WithoutTheWebApiSetup()
    {
        await using var app = await StartAsync(withWebApi: false);
        await using var denied = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.write"));
        await using var allowed = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.read"));

        (await denied.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.ReadOrders))).Should().Be(Forbidden);
        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.ReadOrders))).Should().Be("orders");
    }

    [Fact]
    public async Task HubClass_RefusesAnAnonymousConnection_With401()
    {
        await using var app = await StartAsync();

        var connect = () => app.ConnectAsync(HubPaths.Protected);

        var failure = await connect.Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HubClass_RefusesAConnectionWithoutThePermission_With403()
    {
        await using var app = await StartAsync();

        var connect = () => app.ConnectAsync(HubPaths.Protected, TestAuthentication.SignedIn(permissions: "orders.read"));

        var failure = await connect.Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HubClass_AdmitsAConnectionWithThePermission()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Protected, TestAuthentication.SignedIn(permissions: "hub.connect"));

        (await connection.InvokeAsync<string>(nameof(ProtectedHub.Ping))).Should().Be("pong");
    }

    [Fact]
    public async Task MapHubRequirePermission_RefusesAnAnonymousConnection_AndAdmitsAPermittedOne()
    {
        await using var app = await StartAsync();

        var anonymous = () => app.ConnectAsync(HubPaths.Plain);
        var failure = await anonymous.Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var connection = await app.ConnectAsync(HubPaths.Plain, TestAuthentication.SignedIn(permissions: "hub.connect"));
        (await connection.InvokeAsync<string>(nameof(PlainHub.Ping))).Should().Be("pong");
    }

    private static Task<WebApplication> StartAsync(InMemoryLoggerFactory? loggerFactory = null, bool withWebApi = true) =>
        SignalRTestHost.StartAsync(
            app =>
            {
                app.MapHub<MethodAuthorizationHub>(HubPaths.MethodAuthorization);
                app.MapHub<ProtectedHub>(HubPaths.Protected);
                app.MapHub<PlainHub>(HubPaths.Plain).RequirePermission("hub.connect");
            },
            loggerFactory: loggerFactory,
            withWebApi: withWebApi);
}
