using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// One host with every error source a service can produce, shared by the tests of a class: minimal APIs, MVC
/// controllers, authentication, a clock, rate limiting and required headers.
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

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(
            MapEndpoints,
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.AddSingleton<IClock>(Clock);
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
        app.MapGet("/throw-known", IResult () => throw new NotFoundException(TestErrors.OrderNotFound));
        app.MapGet("/throw-validation", IResult () => throw new ValidationException(
        [
            Error.Validation("customer.name_required", "Name is required."),
            Error.Validation("customer.email_invalid", "Email is invalid."),
        ]));
        app.MapGet("/throw-unknown", IResult () => throw new InvalidOperationException("Connection string Server=db;Password=secret is wrong."));
        app.MapGet("/only-get", () => "ok");
        app.MapPost("/json", ([FromBody] Payload payload) => payload.Name);

        app.MapGet("/auth/perm", () => "ok").RequirePermission("orders.read", "orders.admin");
        app.MapGet("/auth/perm-and-role", () => "ok").RequirePermission("orders.read").RequireRole("auditor");
        app.MapGet("/auth/fresh", () => "ok").RequireFreshAuthentication(300);
        app.MapGet("/auth/fresh-timespan", () => "ok").RequireFreshAuthentication(TimeSpan.FromMinutes(5));
        app.MapGet("/auth/mfa", () => "ok").RequireAuthenticationMethod("mfa", "hwk");
        app.MapGet("/auth/perm-and-fresh", () => "ok").RequirePermission("orders.read").RequireFreshAuthentication(300);
        app.MapGet("/auth/metadata-only", () => "ok").WithMetadata(new RequirePermissionAttribute("orders.read"));
        app.MapPost("/auth/grpc-like", () => "ok").RequirePermission("orders.read");
        app.MapPost("/auth/grpc-like-fresh", () => "ok").RequireFreshAuthentication(300);
        app.MapPost("/auth/grpc-like-mfa", () => "ok").RequireAuthenticationMethod("mfa", "hwk");
        app.MapPost("/auth/grpc-like-perm-and-fresh", () => "ok").RequirePermission("orders.read").RequireFreshAuthentication(300);

        var group = app.MapGroup("/auth/group").RequirePermission("orders.read");
        group.MapGet("/item", () => "ok");

        app.MapPut("/versioned", (HttpContext context) =>
                context.GetIfMatch() == "1" ? Result.Success().ToNoContent() : Result.Failure(TestErrors.VersionConflict).ToNoContent())
            .RequireIfMatch();
        app.MapPut("/versioned-throw", IResult () => throw new ConflictException(TestErrors.VersionConflict)).RequireIfMatch();
        app.MapPut("/not-versioned", () => Result.Failure(TestErrors.VersionConflict).ToNoContent());

        app.MapPost("/idempotent", (HttpContext context) => context.GetIdempotencyKey()).RequireIdempotencyKey();

        app.MapGet("/limited", () => "ok").RequireRateLimiting(RateLimitPolicy);
    }

    public sealed record Payload(string Name);
}
