using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// One host with every error source a service can produce, shared by the tests of a class: minimal APIs, MVC
/// controllers, authentication, a clock, rate limiting and required and accepted headers.
/// </summary>
public sealed class FullStackHost : IAsyncLifetime
{
    public const string RateLimitPolicy = "tight";

    public const string PartitionHeader = "X-Partition";

    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private WebApplication? _app;

    public InMemoryLoggerFactory Logs { get; } = new();

    public FakeClock Clock { get; } = new(Now);

    public HttpClient Client { get; private set; } = null!;

    public WebApplication App => _app!;

    /// <summary>The runs of the handlers that record them: the endpoints that accept a header, and the oblivious ones.</summary>
    public HandlerCalls Calls => App.Services.GetRequiredService<HandlerCalls>();

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(
            MapEndpoints,
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.AddSingleton<IClock>(Clock);
                builder.Services.AddSingleton<HandlerCalls>();
                builder.Services.AddControllers().AddApplicationPart(typeof(FullStackHost).Assembly);
                builder.Services.AddRateLimiter(options => options.AddPolicy(
                    RateLimitPolicy,
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Request.Headers[PartitionHeader].ToString(),
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 })));
            },
            loggerFactory: Logs);

        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapControllers();

        app.MapGet("/ok", () => Result<string>.Success("value").ToOk());
        app.MapGet("/result-failure", () => Result<string>.Failure(TestErrors.OrderNotFound).ToOk());
        app.MapGet("/result-validation", () => Result<string>.Failure(Error.Validation(
        [
            Error.Validation("customer.name_required", "Name is required.") with
            {
                MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "Name" },
            },
            Error.Validation("customer.email_invalid", "Email is invalid.") with
            {
                MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "Email" },
            },
        ])).ToOk());
        app.MapGet("/result-validation-single", () => Result<string>.Failure(Error.Validation("customer.name_required", "Name is required.")).ToOk());
        app.MapGet("/throw-known", IResult () => throw new NotFoundException(TestErrors.OrderNotFound));
        app.MapGet("/throw-validation", IResult () => throw new ValidationException(
        [
            Error.Validation("customer.name_required", "Name is required."),
            Error.Validation("customer.email_invalid", "Email is invalid."),
        ]));
        app.MapGet("/throw-validation-single", IResult () => throw new ValidationException(Error.Validation("customer.name_required", "Name is required.")));
        app.MapGet("/throw-unknown", IResult () => throw new InvalidOperationException("Connection string Server=db;Password=secret is wrong."));
        app.MapGet("/only-get", () => "ok");
        app.MapPost("/json", ([FromBody] Payload payload) => payload.Name);
        app.MapGet("/numbers", (int id) => id);

        app.MapGet("/auth/perm", () => "ok").RequireEndpointPermission("orders.read", "orders.admin");
        app.MapGet("/auth/perm-and-role", () => "ok").RequireEndpointPermission("orders.read").RequireRole("auditor");
        app.MapGet("/auth/fresh", () => "ok").RequireFreshAuthentication(300);
        app.MapGet("/auth/fresh-timespan", () => "ok").RequireFreshAuthentication(TimeSpan.FromMinutes(5));
        app.MapGet("/auth/mfa", () => "ok").RequireAuthenticationMethod("mfa", "hwk");
        app.MapGet("/auth/otp-recent", () => "ok").RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp");
        app.MapGet("/auth/otp-recent-and-fresh", () => "ok")
            .RequireAuthenticationMethod(TimeSpan.FromMinutes(10), "otp")
            .RequireFreshAuthentication(120);
        app.MapGet("/auth/perm-and-fresh", () => "ok").RequireEndpointPermission("orders.read").RequireFreshAuthentication(300);
        app.MapGet("/auth/metadata-only", () => "ok").WithMetadata(new RequireEndpointPermissionAttribute("orders.read"));
        app.MapPost("/auth/grpc-like", () => "ok").RequireEndpointPermission("orders.read");
        app.MapPost("/auth/grpc-like-fresh", () => "ok").RequireFreshAuthentication(300);
        app.MapPost("/auth/grpc-like-mfa", () => "ok").RequireAuthenticationMethod("mfa", "hwk");
        app.MapPost("/auth/grpc-like-otp-recent", () => "ok").RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp");
        app.MapPost("/auth/grpc-like-perm-and-fresh", () => "ok").RequireEndpointPermission("orders.read").RequireFreshAuthentication(300);

        var group = app.MapGroup("/auth/group").RequireEndpointPermission("orders.read");
        group.MapGet("/item", () => "ok");

        app.MapPut("/versioned", (HttpContext context) =>
                context.GetIfMatch() == TestVersions.Current ? Result.Success().ToNoContent() : Result.Failure(TestErrors.StaleVersion).ToNoContent())
            .RequireIfMatch();
        app.MapPut("/versioned-attribute", [RequireIfMatch] (HttpContext context) =>
            context.GetIfMatch() == TestVersions.Current ? Result.Success().ToNoContent() : Result.Failure(TestErrors.StaleVersion).ToNoContent());
        app.MapPut("/versioned-parameter", (IfMatch<EntityVersion> ifMatch) =>
            ifMatch.Version == TestVersions.CurrentVersion
                ? Result.Success().ToNoContent()
                : Result.Failure(TestErrors.StaleVersion).ToNoContent());
        app.MapPut("/versioned-throw", IResult () => throw new ConflictException(TestErrors.StaleVersion)).RequireIfMatch();
        app.MapPut("/versioned-other-conflict", () => Result.Failure(TestErrors.VersionConflict).ToNoContent()).RequireIfMatch();
        app.MapPut("/not-versioned", () => Result.Failure(TestErrors.StaleVersion).ToNoContent());

        app.MapPost("/idempotent", (HttpContext context) => context.GetIdempotencyKey()).RequireIdempotencyKey();
        app.MapPost("/idempotent-attribute", [RequireIdempotencyKey] (HttpContext context) => context.GetIdempotencyKey());
        app.MapPost("/idempotent-parameter", (IdempotencyKey key) => key.Value);
        app.MapPost("/idempotent-and-versioned", () => "ok").RequireIdempotencyKey().RequireIfMatch();
        app.MapPost("/idempotent-protected", () => "ok").RequireEndpointPermission("orders.write").RequireIdempotencyKey();

        // Accepted headers: optional, but refused when sent and unusable. Each handler records what it saw.
        app.MapPost("/idempotent-optional", (HttpContext context, HandlerCalls calls) =>
                calls.Record("/idempotent-optional", context.GetIdempotencyKey()))
            .AcceptIdempotencyKey();
        app.MapPost("/idempotent-optional-attribute", [AcceptIdempotencyKey] (HttpContext context, HandlerCalls calls) =>
            calls.Record("/idempotent-optional-attribute", context.GetIdempotencyKey()));
        app.MapPost("/idempotent-optional-parameter", (IdempotencyKey? key, HandlerCalls calls) =>
            calls.Record("/idempotent-optional-parameter", key?.Value));
        app.MapPost("/idempotent-required-wins", (IdempotencyKey? key, HandlerCalls calls) =>
                calls.Record("/idempotent-required-wins", key?.Value))
            .RequireIdempotencyKey();

        app.MapPut("/versioned-optional", (HttpContext context, HandlerCalls calls) =>
                calls.Record("/versioned-optional", context.GetIfMatch()))
            .AcceptIfMatch();
        app.MapPut("/versioned-optional-attribute", [AcceptIfMatch] (HttpContext context, HandlerCalls calls) =>
            calls.Record("/versioned-optional-attribute", context.GetIfMatch()));
        app.MapPut("/versioned-optional-parameter", (IfMatch<EntityVersion>? ifMatch, HandlerCalls calls) =>
            calls.Record("/versioned-optional-parameter", ifMatch?.Version.ToString()));
        app.MapPut("/versioned-required-wins", (IfMatch<EntityVersion>? ifMatch, HandlerCalls calls) =>
                calls.Record("/versioned-required-wins", ifMatch?.Version.ToString()))
            .RequireIfMatch();

        ObliviousEndpoints.Map(app);

        app.MapGet("/limited", () => "ok").RequireRateLimiting(RateLimitPolicy);
        app.MapGet("/limited-protected", () => "ok").RequireEndpointPermission("orders.read").RequireRateLimiting(RateLimitPolicy);
    }

    public sealed record Payload(string Name);
}
