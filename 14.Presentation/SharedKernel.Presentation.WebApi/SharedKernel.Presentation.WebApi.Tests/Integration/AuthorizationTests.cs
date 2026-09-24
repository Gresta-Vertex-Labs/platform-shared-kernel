using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D3/D16: the authorization matrix on a minimal-API endpoint and on an MVC action — anonymous 401, missing
/// permission 403, OR within an attribute, AND across attributes, and the RFC 9470 step-up challenge for freshness
/// and authentication method, which a gRPC call gets as the same 401 and header without a body (so it ends as
/// Unauthenticated, not PermissionDenied). Regression tests for B1 (attributes enforced without extra registration),
/// B2 (anonymous is 401, not 403) and B14 (platform codes, no requirement names in messages).
/// </summary>
public sealed class AuthorizationTests : IClassFixture<FullStackHost>
{
    private const string MinimalApi = "";

    private const string Mvc = "/mvc-api";

    private readonly FullStackHost _host;

    public AuthorizationTests(FullStackHost host)
    {
        _host = host;
    }

    [Theory]
    [InlineData(MinimalApi)]
    [InlineData(Mvc)]
    public async Task Anonymous_IsChallenged_With401_AndTheSchemesOwnChallenge(string prefix)
    {
        using var response = await _host.Client.GetAsync($"{prefix}/auth/perm");

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default);
        response.Headers.WwwAuthenticate.ToString().Should().Be(TestAuthentication.Challenge);
    }

    [Theory]
    [InlineData(MinimalApi, "orders.read")]
    [InlineData(MinimalApi, "orders.admin")]
    [InlineData(Mvc, "orders.read")]
    [InlineData(Mvc, "orders.admin")]
    public async Task AnyPermissionOfOneRequirement_Suffices(string prefix, string permission)
    {
        using var response = await SendAsync($"{prefix}/auth/perm", request => request.SignedIn(permissions: permission));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(MinimalApi)]
    [InlineData(Mvc)]
    public async Task B14_MissingPermission_Is403_WithThePlatformCode_WithoutNamingThePermission(string prefix)
    {
        using var response = await SendAsync($"{prefix}/auth/perm", request => request.SignedIn(permissions: "orders.write"));

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
        problem.Detail().Should().NotContain("orders.read").And.NotContain("orders.admin");
    }

    [Theory]
    [InlineData(MinimalApi, "orders.read", null, HttpStatusCode.Forbidden)]
    [InlineData(MinimalApi, null, "auditor", HttpStatusCode.Forbidden)]
    [InlineData(MinimalApi, "orders.read", "auditor", HttpStatusCode.OK)]
    [InlineData(Mvc, "orders.read", null, HttpStatusCode.Forbidden)]
    [InlineData(Mvc, null, "auditor", HttpStatusCode.Forbidden)]
    [InlineData(Mvc, "orders.read", "auditor", HttpStatusCode.OK)]
    public async Task RequirementsOfSeveralAttributes_MustAllHold(string prefix, string? permission, string? role, HttpStatusCode expected)
    {
        using var response = await SendAsync(
            $"{prefix}/auth/perm-and-role",
            request => request.SignedIn(permissions: permission, roles: role));

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "reports.read", HttpStatusCode.Forbidden)]
    [InlineData("admin", null, HttpStatusCode.Forbidden)]
    [InlineData("admin", "reports.read", HttpStatusCode.OK)]
    public async Task ControllerAndActionAttributes_MustBothHold(string? role, string? permission, HttpStatusCode expected)
    {
        using var response = await SendAsync("/mvc-admin/report", request => request.SignedIn(permissions: permission, roles: role));

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(MinimalApi + "/auth/fresh")]
    [InlineData(MinimalApi + "/auth/fresh-timespan")]
    [InlineData(Mvc + "/auth/fresh")]
    public async Task RecentAuthentication_IsAccepted(string path)
    {
        using var response = await SendAsync(path, request => request.SignedIn(authTime: FullStackHost.Now.AddSeconds(-60)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(MinimalApi + "/auth/fresh")]
    [InlineData(MinimalApi + "/auth/fresh-timespan")]
    [InlineData(Mvc + "/auth/fresh")]
    public async Task StaleAuthentication_IsAskedToStepUp_WithMaxAge(string path)
    {
        using var response = await SendAsync(path, request => request.SignedIn(authTime: FullStackHost.Now.AddMinutes(-10)));

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired);
        response.Headers.WwwAuthenticate.ToString().Should().Be(
            "Bearer error=\"insufficient_user_authentication\", error_description=\"More recent authentication is required\", max_age=\"300\"");
    }

    [Fact]
    public async Task MissingAuthenticationTime_IsAskedToStepUp()
    {
        using var response = await SendAsync("/auth/fresh", request => request.SignedIn());

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired);
    }

    [Theory]
    [InlineData(MinimalApi, "mfa")]
    [InlineData(MinimalApi, "hwk")]
    [InlineData(Mvc, "mfa")]
    [InlineData(Mvc, "hwk")]
    public async Task AnyAuthenticationMethodOfTheRequirement_Suffices(string prefix, string method)
    {
        using var response = await SendAsync($"{prefix}/auth/mfa", request => request.SignedIn(methods: $"pwd,{method}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(MinimalApi)]
    [InlineData(Mvc)]
    public async Task WeakerAuthenticationMethod_IsAskedToStepUp_WithoutMaxAge(string prefix)
    {
        using var response = await SendAsync($"{prefix}/auth/mfa", request => request.SignedIn(methods: "pwd"));

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired);
        response.Headers.WwwAuthenticate.ToString().Should().Be(
            "Bearer error=\"insufficient_user_authentication\", error_description=\"A stronger authentication method is required\"");
    }

    [Theory]
    [InlineData("DPoP some-token", "DPoP")]
    [InlineData("dpop some-token", "DPoP")]
    [InlineData("Bearer some-token", "Bearer")]
    public async Task StepUpChallenge_UsesTheSchemeTheClientAuthenticatedWith(string authorization, string scheme)
    {
        using var response = await SendAsync(
            "/auth/fresh",
            request =>
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
                return request.SignedIn(authTime: FullStackHost.Now.AddHours(-1));
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().StartWith(scheme + " error=\"insufficient_user_authentication\"");
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Evil-Scheme token")]
    [InlineData("DPoPX token")]
    public async Task R26_StepUpChallenge_NeverEchoesAnotherScheme(string authorization)
    {
        using var response = await SendAsync(
            "/auth/fresh",
            request =>
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
                return request.SignedIn(authTime: FullStackHost.Now.AddHours(-1));
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().StartWith("Bearer error=\"insufficient_user_authentication\"");
    }

    [Fact]
    public async Task MissingPermission_AndStaleAuthentication_IsRefused_NotAskedToStepUp()
    {
        using var response = await SendAsync(
            "/auth/perm-and-fresh",
            request => request.SignedIn(permissions: "orders.write", authTime: FullStackHost.Now.AddHours(-1)));

        await response.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
    }

    [Fact]
    public async Task AnonymousCaller_OnAFreshnessEndpoint_IsChallenged_NotAskedToStepUp()
    {
        using var response = await _host.Client.GetAsync("/auth/fresh");

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default);
    }

    [Fact]
    public async Task B1_AttributeAsPlainMetadata_IsEnforced_WithoutAnyFilterRegistration()
    {
        using var anonymous = await _host.Client.GetAsync("/auth/metadata-only");
        using var forbidden = await SendAsync("/auth/metadata-only", request => request.SignedIn(permissions: "orders.write"));
        using var allowed = await SendAsync("/auth/metadata-only", request => request.SignedIn(permissions: "orders.read"));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GroupConvention_AppliesToEveryEndpointOfTheGroup()
    {
        using var forbidden = await SendAsync("/auth/group/item", request => request.SignedIn(permissions: "orders.write"));
        using var allowed = await SendAsync("/auth/group/item", request => request.SignedIn(permissions: "orders.read"));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("orders.write", HttpStatusCode.Forbidden)]
    public async Task GrpcCall_IsRefused_WithTheStatusOnly(string? permission, HttpStatusCode expected)
    {
        using var request = GrpcRequest("/auth/grpc-like");
        if (permission is not null)
        {
            request.SignedIn(permissions: permission);
        }

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        "/auth/grpc-like-fresh",
        "Bearer error=\"insufficient_user_authentication\", error_description=\"More recent authentication is required\", max_age=\"300\"")]
    [InlineData(
        "/auth/grpc-like-mfa",
        "Bearer error=\"insufficient_user_authentication\", error_description=\"A stronger authentication method is required\"")]
    public async Task GrpcCall_NeedingStepUp_Is401_WithTheRfc9470Challenge_AndNoBody(string path, string challenge)
    {
        // 401, not 403: gRPC reports it as Unauthenticated, the way HTTP answers a step-up.
        using var request = GrpcRequest(path).SignedIn(methods: "pwd", authTime: FullStackHost.Now.AddHours(-1));

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be(challenge);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        var logger = _host.Logs.GetLogger(typeof(SharedKernelAuthorizationResultHandler).FullName!);
        logger.Records.Should().Contain(record =>
            record.EventId.Id == LoggingEventIdRanges.Presentation + 2
            && record.Message.Contains(path, StringComparison.Ordinal)
            && record.Message.Contains(PresentationErrorCodes.StepUpRequired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GrpcCall_MissingPermission_AndStaleAuthentication_IsRefused_NotAskedToStepUp()
    {
        using var request = GrpcRequest("/auth/grpc-like-perm-and-fresh")
            .SignedIn(permissions: "orders.write", authTime: FullStackHost.Now.AddHours(-1));

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.Should().BeEmpty();
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task B2_AnonymousCaller_Is401_Not403()
    {
        using var response = await _host.Client.GetAsync("/auth/perm-and-role");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refusal_IsLoggedAtWarning_WithTheCode_AndNoPrincipalData()
    {
        using var response = await SendAsync("/auth/perm", request => request.SignedIn(user: "alice-secret-id", permissions: "orders.write"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var logger = _host.Logs.GetLogger(typeof(SharedKernelAuthorizationResultHandler).FullName!);
        var record = logger.Records.Last(r => r.EventId.Id == LoggingEventIdRanges.Presentation + 2);
        record.LogLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        record.Message.Should().Contain(ErrorCodes.Forbidden.InsufficientPermission);
        record.Message.Should().NotContain("alice-secret-id");
    }

    private static HttpRequestMessage GrpcRequest(string path) => new(HttpMethod.Post, path)
    {
        Content = new ByteArrayContent([]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc") } },
    };

    private Task<HttpResponseMessage> SendAsync(string path, Func<HttpRequestMessage, HttpRequestMessage> configure)
    {
        var request = configure(new HttpRequestMessage(HttpMethod.Get, path));
        return _host.Client.SendAsync(request);
    }
}
