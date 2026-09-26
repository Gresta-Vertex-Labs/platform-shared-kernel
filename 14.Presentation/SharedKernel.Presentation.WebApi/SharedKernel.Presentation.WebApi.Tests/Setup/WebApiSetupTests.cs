using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Startup;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Setup;

/// <summary>
/// Design D4: the one-call setup — bound from configuration, adjusted by code after binding, validated at startup,
/// safe to call twice, robust against the order of a service's own registrations (R4), with startup warnings for a
/// setup that works but not as intended (R16, R17, R23) and hooks at fixed positions of the pipeline (R2).
/// </summary>
public sealed class WebApiSetupTests
{
    private static readonly string Section = SharedKernelWebApiOptions.SectionName + ":";

    [Fact]
    public void Settings_BindFromConfiguration_ThenTheCallbackRuns()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Section + "Limits:MaxJsonDepth"] = "10",
            [Section + "Problems:TypeBaseUri"] = "https://errors.example.com/",
            [Section + "Problems:PreconditionFailedErrorCodes:0"] = "orders.stale",
            [Section + "RemoveServerHeader"] = "false",
        });

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 20);
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SharedKernelWebApiOptions>>().Value;

        options.Limits.MaxJsonDepth.Should().Be(20);
        options.Problems.TypeBaseUri.Should().Be(new Uri("https://errors.example.com/"));
        options.Problems.PreconditionFailedErrorCodes.Should().Contain(["persistence.concurrency_conflict", "orders.stale"]);
        options.RemoveServerHeader.Should().BeFalse();
    }

    [Fact]
    public void Defaults_AreTheSecureChoices()
    {
        var options = new SharedKernelWebApiOptions();
        options.SecurityHeaders.CacheControl.Should().Be("no-store");
        options.Limits.MaxJsonDepth.Should().BeNull();
        options.Problems.PreconditionFailedErrorCodes.Should().Equal(
            "persistence.concurrency_conflict",
            "storage.precondition_failed",
            "storage.already_exists");
    }

    [Theory]
    [InlineData("https://errors.example.com/problems", "https://errors.example.com/problems/")]
    [InlineData("https://errors.example.com/problems/", "https://errors.example.com/problems/")]
    [InlineData("https://errors.example.com", "https://errors.example.com/")]
    public void R12_TypeBaseUri_IsGivenATrailingSlash(string configured, string expected)
    {
        var options = new WebApiProblemsOptions { TypeBaseUri = new Uri(configured) };

        options.TypeBaseUri!.AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void CallingTwice_RegistersOnce_AndAppliesEveryCallback()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 12);
        builder.AddSharedKernelWebApi(options => options.RemoveServerHeader = false);
        using var provider = builder.Services.BuildServiceProvider();

        builder.Services.Count(descriptor => descriptor.ServiceType == typeof(SharedKernelExceptionHandler)).Should().Be(1);
        builder.Services.Count(descriptor => descriptor.ServiceType == typeof(IAuthorizationPolicyProvider)).Should().Be(1);
        provider.GetRequiredService<IAuthorizationPolicyProvider>().Should().BeOfType<SharedKernelAuthorizationPolicyProvider>();
        provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>().Should().BeOfType<SharedKernelAuthorizationResultHandler>();
        var options = provider.GetRequiredService<IOptions<SharedKernelWebApiOptions>>().Value;
        options.Limits.MaxJsonDepth.Should().Be(12);
        options.RemoveServerHeader.Should().BeFalse();
    }

    [Fact]
    public void JsonDepth_AppliesToMinimalApisAndMvc_WhenSet()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 7);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions.MaxDepth.Should().Be(7);
        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions.MaxDepth.Should().Be(7);
    }

    [Fact]
    public void R13_JsonDepth_KeepsTheFrameworkDefault_WhenNotSet()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddSharedKernelWebApi();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions.MaxDepth
            .Should().Be(new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions.MaxDepth);
        provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions.MaxDepth
            .Should().Be(new Microsoft.AspNetCore.Mvc.JsonOptions().JsonSerializerOptions.MaxDepth);
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
    [InlineData("Problems:TypeBaseUri", "/relative/")]
    [InlineData("Problems:TypeBaseUri", "https://errors.example.com/?v=1")]
    [InlineData("Problems:PreconditionFailedErrorCodes:3", " ")]
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
    public void R28_InvalidSettings_ThrowWhenThePipelineIsBuilt_WithTestServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddSharedKernelWebApi(options => options.Limits.MaxJsonDepth = 0);
        var app = builder.Build();

        var act = () => app.UseSharedKernelWebApi();

        act.Should().Throw<OptionsValidationException>();
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
    public async Task R17_AddWithoutUse_WarnsAtStartup()
    {
        var logs = new InMemoryLoggerFactory();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ILoggerFactory>(logs);
        builder.AddSharedKernelWebApi();
        await using var app = builder.Build();
        app.MapGet("/ping", () => "pong");

        await app.StartAsync();

        logs.GetLogger(typeof(WebApiStartupDiagnostics).FullName!).Records.Should().ContainSingle(record =>
            record.EventId.Id == LoggingEventIdRanges.Presentation + 11 && record.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task R17_AddAndUse_DoesNotWarn()
    {
        var logs = new InMemoryLoggerFactory();

        await using var app = await WebApiTestHost.StartAsync(app => app.MapGet("/ping", () => "pong"), loggerFactory: logs);

        logs.GetLogger(typeof(WebApiStartupDiagnostics).FullName!).Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WebApiTestHost.Production, true, true)]
    [InlineData(WebApiTestHost.Development, true, false)]
    [InlineData(WebApiTestHost.Production, null, false)]
    public async Task R23_ExceptionDetailsOutsideDevelopment_WarnAtStartup(string environment, bool? include, bool warns)
    {
        var logs = new InMemoryLoggerFactory();

        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/ping", () => "pong"),
            configureOptions: options => options.Problems.IncludeExceptionDetails = include,
            environment: environment,
            loggerFactory: logs);

        logs.GetLogger(typeof(WebApiStartupDiagnostics).FullName!).Records
            .Any(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 12).Should().Be(warns);
    }

    [Fact]
    public async Task WithoutAuthentication_ProtectedEndpoint_Is401_WithABearerChallenge()
    {
        await using var app = await WebApiTestHost.StartAsync(app => app.MapGet("/secured", () => "ok").RequireEndpointPermission("orders.read"));

        using var response = await app.GetTestClient().GetAsync("/secured");

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task R15_WithoutAuthentication_ProtectedGrpcCall_Is401_WithoutABody()
    {
        await using var app = await WebApiTestHost.StartAsync(app => app.MapPost("/secured", () => "ok").RequireEndpointPermission("orders.read"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/secured")
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc") } },
        };

        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LaterAddAuthorization_KeepsThePolicies_AndServicePoliciesStillWork()
    {
        await using var app = await WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/perm", () => "ok").RequireEndpointPermission("orders.read");
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
        app.MapGet("/perm", () => "ok").RequireEndpointPermission("orders.read");
        await app.StartAsync();

        using var forbidden = await app.GetTestClient().SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.write"));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task R4_ServicePolicyProvider_RegisteredBefore_IsDecorated_AndBothKindsOfPolicyWork()
    {
        await using var app = await WebApiTestHost.StartAsync(
            MapAuthorizationEndpoints,
            builder => builder.AddTestAuthentication(),
            configureBeforeWebApi: builder => builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>());
        var client = app.GetTestClient();

        using var platformAllowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.read"));
        using var platformRefused = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.write"));
        using var serviceAllowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/dynamic").SignedIn(roles: "operator"));
        using var serviceRefused = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/dynamic").SignedIn(roles: "viewer"));

        platformAllowed.StatusCode.Should().Be(HttpStatusCode.OK);
        await platformRefused.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
        serviceAllowed.StatusCode.Should().Be(HttpStatusCode.OK);
        await serviceRefused.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
    }

    [Fact]
    public async Task R4_ServicePolicyProvider_RegisteredAfter_FailsAtStartup_NamingIt()
    {
        var builder = CreateBuilder();
        builder.AddSharedKernelWebApi();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
        await using var app = builder.Build();
        app.UseSharedKernelWebApi();

        var act = () => app.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(IAuthorizationPolicyProvider)).And.Contain(typeof(DynamicPolicyProvider).FullName!);
    }

    [Fact]
    public async Task R4_ServiceResultHandler_RegisteredBefore_IsDecorated_AndRefusalsKeepTheirProblemBody()
    {
        await using var app = await WebApiTestHost.StartAsync(
            MapAuthorizationEndpoints,
            builder => builder.AddTestAuthentication(),
            configureBeforeWebApi: builder => builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MarkingResultHandler>());
        var client = app.GetTestClient();

        using var allowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.read"));
        using var refused = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.write"));

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.Headers.GetValues(MarkingResultHandler.Header).Should().ContainSingle("the service's handler runs for every result");
        await refused.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
        refused.Headers.GetValues(MarkingResultHandler.Header).Should().ContainSingle();
    }

    [Fact]
    public async Task R4_ServiceResultHandler_RegisteredAfter_FailsAtStartup_NamingIt()
    {
        var builder = CreateBuilder();
        builder.AddSharedKernelWebApi();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MarkingResultHandler>();
        await using var app = builder.Build();
        app.UseSharedKernelWebApi();

        var act = () => app.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(IAuthorizationMiddlewareResultHandler)).And.Contain(typeof(MarkingResultHandler).FullName!);
    }

    [Fact]
    public async Task R16_PrincipalNoMapperUnderstands_IsRefused_AndTheReasonIsLogged()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/fresh", () => "ok").RequireFreshAuthentication(300),
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.RemoveAll<SharedKernel.Security.Abstractions.IUserContextMapper>();
            },
            loggerFactory: logs);

        using var response = await app.GetTestClient().SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/fresh").SignedIn(authTime: DateTimeOffset.UtcNow));

        await response.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
        var runtime = logs.GetLogger(typeof(SharedKernelRequirementHandler).FullName!).Records
            .Should().ContainSingle(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 9).Subject;
        runtime.LogLevel.Should().Be(LogLevel.Warning);
        runtime.Message.Should().Contain(TestAuthentication.Scheme);
    }

    [Fact]
    public async Task R16_SchemeWithoutAMapper_IsNamedInAStartupWarning()
    {
        var logs = new InMemoryLoggerFactory();

        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/ping", () => "pong"),
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Unmapped", _ => { });
            },
            loggerFactory: logs);

        var warnings = logs.GetLogger(typeof(SharedKernelAuthorizationStartupCheck).FullName!).Records
            .Where(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 10)
            .ToArray();
        warnings.Should().ContainSingle().Which.Message.Should().Contain("Unmapped");
    }

    [Fact]
    public void R21_EverydayTypes_LiveInTheRootNamespace()
    {
        Type[] everyday =
        [
            typeof(RequireIdempotencyKeyAttribute), typeof(RequireIfMatchAttribute),
            typeof(ErrorHttpResult), typeof(OkWithETag<>), typeof(IdempotencyKey), typeof(IfMatch<>), typeof(WebApiPipeline),
            typeof(ResultHttpExtensions), typeof(ConditionalRequestExtensions),
            typeof(IdempotencyKeyExtensions), typeof(CorrelationIdHttpContextExtensions),
        ];

        everyday.Should().OnlyContain(type => type.Namespace == "SharedKernel.Presentation.WebApi");
    }

    [Fact]
    public void P579_AuthorizationTypes_LiveInTheSharedAuthorizationNamespace()
    {
        // Shared with gRPC and SignalR through SharedKernel.Presentation.Core, so not WebApi's own namespace.
        Type[] authorization =
        [
            typeof(RequireEndpointPermissionAttribute), typeof(RequireRoleAttribute), typeof(RequireFreshAuthenticationAttribute),
            typeof(RequireAuthenticationMethodAttribute), typeof(AuthorizationConventionExtensions),
        ];

        authorization.Should().OnlyContain(type => type.Namespace == "SharedKernel.Presentation.Authorization");
    }

    [Fact]
    public async Task R2_Hooks_RunAtTheirPositions()
    {
        var seen = new List<string>();

        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/perm", () => "ok").RequireEndpointPermission("orders.read"),
            builder => builder.AddTestAuthentication(),
            configurePipeline: pipeline => pipeline
                .AtStart(app => app.Use((context, next) =>
                {
                    seen.Add($"start:endpoint={context.GetEndpoint() is not null}:correlation={context.GetCorrelationId() is not null}");
                    return next(context);
                }))
                .BeforeAuthentication(app => app.Use((context, next) =>
                {
                    seen.Add($"before-authentication:endpoint={context.GetEndpoint() is not null}:user={context.User.Identity?.IsAuthenticated == true}");
                    return next(context);
                }))
                .BeforeAuthorization(app => app.Use((context, next) =>
                {
                    seen.Add($"before-authorization:user={context.User.Identity?.IsAuthenticated == true}");
                    return next(context);
                })));

        using var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Get, "/perm").SignedIn(permissions: "orders.read"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        seen.Should().Equal(
            "start:endpoint=False:correlation=True",
            "before-authentication:endpoint=True:user=False",
            "before-authorization:user=True");
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
            options => options.Problems.TypeBaseUri = new Uri("https://errors.example.com/problems"));

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

    private static void MapAuthorizationEndpoints(WebApplication app)
    {
        app.MapGet("/perm", () => "ok").RequireEndpointPermission("orders.read");
        app.MapGet("/dynamic", () => "ok").RequireAuthorization(DynamicPolicyProvider.Prefix + "orders");
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = WebApiTestHost.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddTestAuthentication();
        return builder;
    }

    private static Task<WebApplication> StartWithControllersAsync(bool mvcRegisteredFirst, Action<SharedKernelWebApiOptions>? configure)
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
