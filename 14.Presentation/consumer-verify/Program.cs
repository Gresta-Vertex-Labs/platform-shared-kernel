// consumer-verify — exercises the composed Program.cs shapes documented in
// SharedKernel.Presentation.WebApi/README.md and SharedKernel.Presentation.SignalR/README.md:
// the full WebApi stack (ProblemDetails, exception handler, versioning, OpenAPI/Scalar) and
// AddSharedKernelSignalR with and without WithRedisBackplane, confirming every surface composes
// into a resolvable DI container / request pipeline with zero DI exceptions.

using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.SignalR.Extensions;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Middleware;
using SharedKernel.Presentation.WebApi.OpenApi;
using SharedKernel.Presentation.WebApi.Versioning;

// ── Surface 1: full WebApi stack — ProblemDetails + exception handler ───────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelCorrelationId();
    Console.WriteLine("Surface 1 PASS: AddProblemDetails / AddExceptionHandler<SharedKernelExceptionHandler> / AddSharedKernelCorrelationId");

    // ── Surface 2: API versioning + OpenAPI + Scalar composed alongside it ──
    builder.Services.AddSharedKernelApiVersioning();
    builder.Services.AddSharedKernelOpenApi(title: "Consumer Verify API");
    Console.WriteLine("Surface 2 PASS: AddSharedKernelApiVersioning / AddSharedKernelOpenApi");

    var app = builder.Build();

    // ── Surface 3: core WebApi services resolve without exception ───────────
    using (var scope = app.Services.CreateScope())
    {
        var exceptionHandlers = scope.ServiceProvider.GetServices<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>().ToList();
        Verify(exceptionHandlers.Any(h => h is SharedKernelExceptionHandler), "SharedKernelExceptionHandler resolves as a registered IExceptionHandler");

        var versionDescriptionProvider = scope.ServiceProvider.GetRequiredService<IApiVersionDescriptionProvider>();
        Verify(versionDescriptionProvider.ApiVersionDescriptions.Count > 0, "IApiVersionDescriptionProvider resolves with at least one discovered version");
    }

    Console.WriteLine("Surface 3 PASS: WebApi core services resolve (IExceptionHandler, IApiVersionDescriptionProvider)");

    // ── Surface 4: pipeline + endpoint mapping construct without exception ──
    app.UseSharedKernelCorrelationId();
    app.UseExceptionHandler();
    app.MapSharedKernelOpenApi();
    Console.WriteLine("Surface 4 PASS: UseSharedKernelCorrelationId / UseExceptionHandler / MapSharedKernelOpenApi pipeline construction");
}

// ── Surface 5: AddSharedKernelSignalR — in-memory, no backplane ─────────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSharedKernelSignalR();

    using var provider = services.BuildServiceProvider();
    var hubFilters = provider.GetServices<TenantContextHubFilter>().Concat<object>(provider.GetServices<HubExceptionMappingFilter>()).ToList();
    Verify(hubFilters.Count == 2, "TenantContextHubFilter and HubExceptionMappingFilter both resolve as registered singletons");

    // HubOptions is configured, not directly resolvable as IOptions<HubOptions> without a Hub type
    // in this minimal DI graph — resolving HubConnectionHandler<TestHub> proves the full SignalR
    // pipeline (including the globally-registered hub filters) builds without a DI exception.
    var hubHandler = provider.GetRequiredService<HubConnectionHandler<ConsumerVerifyHub>>();
    Verify(hubHandler is not null, "HubConnectionHandler<ConsumerVerifyHub> resolves with both platform filters wired");
}

Console.WriteLine("Surface 5 PASS: AddSharedKernelSignalR (in-memory, no backplane) resolves with zero DI exceptions");

// ── Surface 6: AddSharedKernelSignalR + WithRedisBackplane ──────────────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services
        .AddSharedKernelSignalR()
        .WithRedisBackplane("localhost:6379,abortConnect=false");

    // WithRedisBackplane registers HubLifetimeManager<THub> services lazily per-hub and does not
    // open a connection during registration — StackExchange.Redis's ConnectionMultiplexer.Connect
    // is deferred until first use. Resolving HubConnectionHandler<TestHub> here proves the full DI
    // graph (platform filters + Redis-backed lifetime manager) wires up with no resolution
    // exceptions; it deliberately does not require a live Redis instance.
    using var provider = services.BuildServiceProvider();
    var hubHandler = provider.GetRequiredService<HubConnectionHandler<ConsumerVerifyHub>>();
    Verify(hubHandler is not null, "HubConnectionHandler<ConsumerVerifyHub> resolves with the Redis-backed HubLifetimeManager wired");
}

Console.WriteLine("Surface 6 PASS: AddSharedKernelSignalR().WithRedisBackplane(...) resolves with zero DI exceptions");

// ── Surface 7: full WebApi stack + AddSharedKernelAuthorizationFilters ──────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelCorrelationId();
    builder.Services.AddSharedKernelApiVersioning();
    builder.Services.AddSharedKernelOpenApi(title: "Consumer Verify API (Authorization)");
    builder.Services.AddSharedKernelAuthorizationFilters();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var authorizationFilter = scope.ServiceProvider.GetRequiredService<AuthorizationRequirementEndpointFilter>();
        Verify(authorizationFilter is not null, "AuthorizationRequirementEndpointFilter resolves as a registered singleton alongside the full WebApi stack");
    }

    app.UseSharedKernelCorrelationId();
    app.UseExceptionHandler();
    app.MapSharedKernelOpenApi();

    // Prove the documented wiring form itself constructs without exception: attaching the filter
    // to a mapped minimal-API route group via .AddEndpointFilter<AuthorizationRequirementEndpointFilter>()
    // and stacking .RequireRole/.RequirePermission sugar on an individual route.
    var authorizedGroup = app
        .MapGroup("/consumer-verify")
        .AddEndpointFilter<AuthorizationRequirementEndpointFilter>();
    authorizedGroup
        .MapGet("/secure", () => Results.Ok())
        .RequireRole("Admin")
        .RequirePermission("orders:read");

    Console.WriteLine("Surface 7 PASS: AddSharedKernelAuthorizationFilters + .AddEndpointFilter<AuthorizationRequirementEndpointFilter>() + .RequireRole/.RequirePermission compose alongside the full WebApi stack with zero DI exceptions");
}

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");

static void Verify(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {label}");
}

// ── Test Hub used solely to prove the SignalR DI graph resolves end-to-end ───
/// <summary>
/// Minimal <see cref="Hub"/> used only by this verification harness so
/// <c>HubConnectionHandler&lt;ConsumerVerifyHub&gt;</c> can be resolved to prove the full SignalR
/// pipeline — including the globally-registered platform hub filters — builds without a DI
/// exception. Never receives real client connections.
/// </summary>
internal sealed class ConsumerVerifyHub : Hub;
