using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Setup;

/// <summary>
/// Design D4: the one-call setup — bound from configuration, adjusted by code after binding, validated at startup,
/// safe to call twice, and robust against the order of a service's own registrations.
/// </summary>
public sealed class WebApiSetupTests
{
    private static readonly string Section = WebApiOptions.SectionName + ":";

    [Fact]
    public void Settings_BindFromConfiguration_ThenTheCallbackRuns()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Section + "Limits:MaxJsonDepth"] = "10",
            [Section + "CorrelationId:MaxLength"] = "64",
            [Section + "Problems:TypeBaseUri"] = "https://errors.example.com/",
            [Section + "RemoveServerHeader"] = "false",
        });

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 20);
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WebApiOptions>>().Value;

        options.Limits.MaxJsonDepth.Should().Be(20);
        options.CorrelationId.MaxLength.Should().Be(64);
        options.Problems.TypeBaseUri.Should().Be(new Uri("https://errors.example.com/"));
        options.RemoveServerHeader.Should().BeFalse();
    }

    [Fact]
    public void CallingTwice_RegistersOnce_AndAppliesEveryCallback()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 12);
        builder.AddSharedKernelWebApi(options => options.CorrelationId.MaxLength = 50);
        using var provider = builder.Services.BuildServiceProvider();

        builder.Services.Count(descriptor => descriptor.ServiceType == typeof(IExceptionHandler)).Should().Be(1);
        builder.Services.Count(descriptor => descriptor.ServiceType == typeof(IAuthorizationPolicyProvider)
            && descriptor.ImplementationType == typeof(SharedKernelAuthorizationPolicyProvider)).Should().Be(1);
        var options = provider.GetRequiredService<IOptions<WebApiOptions>>().Value;
        options.Limits.MaxJsonDepth.Should().Be(12);
        options.CorrelationId.MaxLength.Should().Be(50);
    }

    [Fact]
    public void JsonDepth_AppliesToMinimalApisAndMvc()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 7);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions.MaxDepth.Should().Be(7);
        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions.MaxDepth.Should().Be(7);
    }

    [Fact]
    public void Kestrel_DropsTheServerHeader_AndLimitsTheBody_ByDefault()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi();
        using var provider = builder.Services.BuildServiceProvider();
        var kestrel = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        kestrel.AddServerHeader.Should().BeFalse();
        kestrel.Limits.MaxRequestBodySize.Should().Be(4 * 1024 * 1024);
    }

    [Fact]
    public void NullBodyLimit_KeepsTheServerDefault()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi(options => options.Limits.MaxRequestBodySize = null);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize
            .Should().Be(new KestrelServerOptions().Limits.MaxRequestBodySize);
    }

    [Theory]
    [InlineData("Limits:MaxJsonDepth", "0")]
    [InlineData("Limits:MaxRequestBodySize", "0")]
    [InlineData("CorrelationId:MaxLength", "0")]
    [InlineData("CorrelationId:AllowedCharacterPattern", "(")]
    [InlineData("Problems:TypeBaseUri", "/relative/")]
    [InlineData("Cors:PreflightMaxAge", "-00:00:01")]
    public async Task InvalidSettings_FailAtStartup(string key, string value)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [Section + key] = value });
        builder.AddSharedKernelWebApi();
        await using var app = builder.Build();

        var act = () => app.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void UseWithoutAdd_ExplainsWhatIsMissing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var act = () => app.UseSharedKernelWebApi();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSharedKernelWebApi*");
    }

    [Fact]
    public async Task UsingTwice_AddsThePipelineOnce()
    {
        await using var app = await WebApiTestHost.StartAsync(app =>
        {
            app.UseSharedKernelWebApi();
            app.MapGet("/ping", () => "pong");
        });

        using var response = await app.GetTestClient().GetAsync("/ping");

        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle();
        response.Headers.GetValues(HeaderNames.XContentTypeOptions).Should().ContainSingle();
    }

    [Fact]
    public async Task WithoutAuthentication_ProtectedEndpoint_Is401_WithABearerChallenge()
    {
        await using var app = await WebApiTestHost.StartAsync(app => app.MapGet("/secured", () => "ok").RequirePermission("orders.read"));

        using var response = await app.GetTestClient().GetAsync("/secured");

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task LaterAddAuthorization_KeepsThePolicies_AndServicePoliciesStillWork()
    {
        await using var app = await WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/perm", () => "ok").RequirePermission("orders.read");
                app.MapGet("/named", () => "ok").RequireAuthorization("service-policy");
            },
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.AddAuthorization(options => options.AddPolicy(
                    "service-policy",
                    policy => policy.RequireClaim(TestAuthentication.RoleClaim, "operator")));
            });
        var client = app.GetTestClient();

        using var allowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.read"));
        using var named = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/named").SignedIn(roles: "operator"));
        using var namedForbidden = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/named").SignedIn(roles: "viewer"));

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        named.StatusCode.Should().Be(HttpStatusCode.OK);
        await namedForbidden.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
    }

    [Fact]
    public async Task EarlierAddAuthorization_IsFine_Too()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.AddTestAuthentication();
        builder.AddSharedKernelWebApi();
        await using var app = builder.Build();
        app.UseSharedKernelWebApi();
        app.MapGet("/perm", () => "ok").RequirePermission("orders.read");
        await app.StartAsync();

        using var forbidden = await app.GetTestClient().SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.write"));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PrincipalNoMapperUnderstands_IsRefused_NotAskedToStepUp()
    {
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/fresh", () => "ok").RequireFreshAuthentication(300),
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.RemoveAll<SharedKernel.Security.Abstractions.IUserContextMapper>();
            });

        using var response = await app.GetTestClient().SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/fresh").SignedIn(authTime: DateTimeOffset.UtcNow));

        await response.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
    }

    [Theory]
    [InlineData("/failure", "order.not_found", false)]
    [InlineData("/missing", "http.404", false)]
    [InlineData("/mvc-api/failure", "order.not_found", false)]
    [InlineData("/mvc-api/failure", "order.not_found", true)]
    [InlineData("/mvc-api/not-found", "http.404", false)]
    [InlineData("/mvc-api/not-found", "http.404", true)]
    [InlineData("/mvc-api/throw", "order.not_found", true)]
    [InlineData("/mvc-plain/failure", "order.not_found", false)]
    [InlineData("/mvc-plain/failure", "order.not_found", true)]
    public async Task TypeBaseUri_MakesTheTypeTheCodesAddress_OnEveryProblem(string path, string errorCode, bool mvcRegisteredFirst)
    {
        await using var app = await StartWithControllersAsync(
            mvcRegisteredFirst,
            options => options.Problems.TypeBaseUri = new Uri("https://errors.example.com/problems/"));

        using var response = await app.GetTestClient().GetAsync(path);

        (await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, errorCode))
            .GetProperty("type").GetString().Should().Be("https://errors.example.com/problems/" + errorCode);
    }

    [Theory]
    [InlineData("/mvc-api/failure", "order.not_found")]
    [InlineData("/mvc-api/throw", "order.not_found")]
    [InlineData("/mvc-api/not-found", "http.404")]
    [InlineData("/mvc-plain/failure", "order.not_found")]
    public async Task MvcRegisteredBeforeThisPackage_StillWritesTheOneShape(string path, string errorCode)
    {
        // IProblemDetailsService then asks MVC's writer first, which rebuilds [ApiController] problems through its own
        // factory and silently writes nothing for other controllers.
        await using var app = await StartWithControllersAsync(mvcRegisteredFirst: true, configure: null);

        using var response = await app.GetTestClient().GetAsync(path);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, errorCode);
        problem.GetProperty("instance").GetString().Should().Be(path);
    }

    private static Task<WebApplication> StartWithControllersAsync(bool mvcRegisteredFirst, Action<WebApiOptions>? configure)
    {
        void AddControllers(WebApplicationBuilder builder) =>
            builder.Services.AddControllers().AddApplicationPart(typeof(ApiTestController).Assembly);

        return WebApiTestHost.StartAsync(
            app =>
            {
                app.MapControllers();
                app.MapGet("/failure", () => Result<string>.Failure(TestErrors.OrderNotFound).ToOk());
            },
            configureBuilder: mvcRegisteredFirst ? null : AddControllers,
            configureOptions: configure,
            configureBeforeWebApi: mvcRegisteredFirst ? AddControllers : null);
    }
}
