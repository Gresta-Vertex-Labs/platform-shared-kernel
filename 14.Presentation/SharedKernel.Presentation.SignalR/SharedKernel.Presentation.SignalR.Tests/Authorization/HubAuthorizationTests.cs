using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Options;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Authorization;

/// <summary>
/// Design D3/D12/D16: the WebApi requirements on a hub method, on the hub class and on <c>MapHub&lt;T&gt;()</c>. The
/// attributes are <c>[Authorize]</c> attributes, and SignalR authorizes a hub method only through those, so it
/// enforces them itself — before any hub filter runs, with its own refusal — even in a host that never registers this
/// package. A <c>[RequirePermission]</c> that is not an <c>[Authorize]</c> would let every connected caller through
/// (the B1 fail-open class, on hubs), which the WebApi attribute tests pin at the source.
/// </summary>
public sealed class HubAuthorizationTests
{
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
    public async Task HubMethod_MissingPermission_IsRefusedBySignalR_WithoutRunningTheMethod_OrNamingThePermission()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.write"));

        var message = await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));

        message.Should().NotContain("orders.read").And.NotContain("orders.admin");
        InvocationCount(app).Should().Be(0);
    }

    [Fact]
    public async Task HubMethod_AnonymousCaller_IsRefused_WithoutRunningTheMethod()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.MethodAuthorization);

        await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));

        InvocationCount(app).Should().Be(0);
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
            InvocationCount(app).Should().Be(1);
        }
        else
        {
            await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.Audit));
            InvocationCount(app).Should().Be(0);
        }
    }

    [Fact]
    public async Task HubMethod_StaleAuthentication_IsRefused_WithoutRunningTheMethod()
    {
        // SignalR has no step-up answer: a stale sign-in gets the same refusal as a missing permission.
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(authTime: DateTimeOffset.UtcNow.AddHours(-1)));

        await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.Transfer));

        InvocationCount(app).Should().Be(0);
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
            await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ChangePassword));
            InvocationCount(app).Should().Be(0);
        }
    }

    [Fact]
    public async Task X1_StepUpWithMaxAge_EndsOnAConnectionThatStaysOpen()
    {
        // Security review S3, probe P5: the connection keeps the principal it opened with — amr=otp and the step-up's
        // time — however long it stays open. With a maximum age, SignalR's per-call authorization compares that time
        // with the clock, so the step-up ends on the connection too.
        var steppedUpAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(steppedUpAt);
        await using var app = await StartAsync(configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(methods: "pwd,otp", methodTimes: [("otp", steppedUpAt)]));

        clock.Set(steppedUpAt.AddMinutes(1));
        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.ApprovePayout))).Should().Be("approved");

        clock.Set(steppedUpAt.AddMinutes(60));
        await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ApprovePayout));
        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Open))).Should().Be("open", "the connection itself stays usable");

        InvocationCount(app).Should().Be(2);
    }

    [Fact]
    public async Task HubMethod_WithoutRequirements_IsOpenToAnyConnectedCaller()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.MethodAuthorization);

        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Open))).Should().Be("open");
    }

    [Fact]
    public async Task HubMethod_Refusal_HappensBeforeTheHubFilters_SoItSpendsNoRateLimitPermit()
    {
        // SignalR authorizes before any hub filter runs: the refusals below never reach the rate limit (one permit per
        // minute), so the permit is still there for the permitted invocation, and the message is SignalR's own, not the
        // error mapping's "{code}: {message}".
        await using var app = await StartAsync(configureSignalR: options =>
        {
            options.InvocationRateLimit.PermitLimit = 1;
            options.InvocationRateLimit.Window = TimeSpan.FromMinutes(1);
        });
        await using var connection = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.write"));

        await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));
        await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));

        (await connection.InvokeAsync<string>(nameof(MethodAuthorizationHub.Open))).Should().Be("open");
        (await connection.InvokeExpectingErrorAsync(nameof(MethodAuthorizationHub.Open)))
            .Should().Be($"{PresentationErrorCodes.RateLimitExceeded}: Too many requests.");
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

        await denied.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));
        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.ReadOrders))).Should().Be("orders");
        InvocationCount(app).Should().Be(1);
    }

    [Fact]
    public async Task HubMethod_RequirementsAreEnforced_WithPlainSignalR_AndOnlyTheCoreAuthorization()
    {
        // No AddSharedKernelWebApi, no AddSharedKernelSignalR, so none of this package's hub filters: just SignalR and
        // the policies behind the attributes. The enforcement is SignalR's own.
        await using var app = await StartAsync(
            withWebApi: false,
            withSharedKernelSignalR: false,
            configureBuilder: builder =>
            {
                builder.Services.AddSignalR();
                builder.Services.AddSharedKernelAuthorization();
            });
        app.Services.GetService<HubExceptionMappingFilter>().Should().BeNull("the platform's SignalR setup is not registered");

        await using var anonymous = await app.ConnectAsync(HubPaths.MethodAuthorization);
        await using var denied = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.write", authTime: DateTimeOffset.UtcNow.AddHours(-1)));
        await using var allowed = await app.ConnectAsync(
            HubPaths.MethodAuthorization,
            TestAuthentication.SignedIn(permissions: "orders.read", roles: "auditor", methods: "mfa", authTime: DateTimeOffset.UtcNow));

        await anonymous.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));
        await denied.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));
        await denied.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.Audit));
        await denied.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.Transfer));
        await denied.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ChangePassword));
        InvocationCount(app).Should().Be(0);

        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.ReadOrders))).Should().Be("orders");
        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.Audit))).Should().Be("audited");
        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.Transfer))).Should().Be("transferred");
        (await allowed.InvokeAsync<string>(nameof(MethodAuthorizationHub.ChangePassword))).Should().Be("changed");
        InvocationCount(app).Should().Be(4);
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

    private static int InvocationCount(WebApplication app) => app.Services.GetRequiredService<InvocationCounter>().Count;

    private static Task<WebApplication> StartAsync(
        Action<SharedKernelSignalROptions>? configureSignalR = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        bool withWebApi = true,
        bool withSharedKernelSignalR = true) =>
        SignalRTestHost.StartAsync(
            app =>
            {
                app.MapHub<MethodAuthorizationHub>(HubPaths.MethodAuthorization);
                app.MapHub<ProtectedHub>(HubPaths.Protected);
                app.MapHub<PlainHub>(HubPaths.Plain).RequirePermission("hub.connect");
            },
            configureSignalR: configureSignalR,
            configureBuilder: configureBuilder,
            withWebApi: withWebApi,
            withSharedKernelSignalR: withSharedKernelSignalR);
}
