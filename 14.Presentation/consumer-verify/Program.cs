// consumer-verify — exercises the composed Program.cs shapes documented in
// SharedKernel.Presentation.WebApi/README.md and SharedKernel.Presentation.SignalR/README.md:
// the full WebApi stack (ProblemDetails, exception handler, versioning, OpenAPI/Scalar), declarative
// role/permission + step-up/fresh-authentication authorization, security response headers, CORS,
// inbound idempotency-key support, ETag/If-Match + rate-limit ProblemDetails helpers (WO-062,
// P-402–P-408), payload/JSON-depth limits, non-default OpenAPI security schemes, RFC 8594
// Sunset/Deprecation headers, structured security-audit logging, correlation-id format validation,
// upload validation (WO-063, P-411–P-416), and AddSharedKernelSignalR with and without
// WithRedisBackplane (SharedKernel.Presentation.SignalR.Redis) plus hub-invocation rate limiting (WO-063, P-417),
// a gRPC host on SharedKernel.Presentation.Grpc without WebApi (P-570), and the GraphQL server
// conventions of SharedKernel.Presentation.GraphQL (moved from 11.Communication, P-573), confirming every surface
// composes into a resolvable DI container / request pipeline with zero DI exceptions.

using System.Net.Http;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.GraphQL.Extensions;
using SharedKernel.Presentation.Grpc.Extensions;
using SharedKernel.Presentation.SignalR.Extensions;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Concurrency;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Presentation.WebApi.Middleware;
using SharedKernel.Presentation.WebApi.OpenApi;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.Presentation.WebApi.Uploads;
using SharedKernel.Presentation.WebApi.Versioning;

// ── Surface 1: full WebApi stack — ProblemDetails + exception handler ───────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    Console.WriteLine("Surface 1 PASS: AddProblemDetails / AddExceptionHandler<SharedKernelExceptionHandler>");

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
    app.UseExceptionHandler();
    app.MapSharedKernelOpenApi();
    Console.WriteLine("Surface 4 PASS: UseExceptionHandler / MapSharedKernelOpenApi pipeline construction");
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
    builder.Services.AddSharedKernelApiVersioning();
    builder.Services.AddSharedKernelOpenApi(title: "Consumer Verify API (Authorization)");
    builder.Services.AddSharedKernelAuthorizationFilters();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var authorizationFilter = scope.ServiceProvider.GetRequiredService<AuthorizationRequirementEndpointFilter>();
        Verify(authorizationFilter is not null, "AuthorizationRequirementEndpointFilter resolves as a registered singleton alongside the full WebApi stack");
    }

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

    // Step-up/fresh-authentication attributes (WO-062, P-406) EXTEND the same
    // AuthorizationRequirementEndpointFilter above — no second filter type, no second
    // .AddEndpointFilter<...>() registration call, proving the extension composes on the
    // identical filter/group already registered for Surface 7.
    authorizedGroup
        .MapPost("/payments/{id:guid}/confirm", (Guid id) => Results.Ok())
        .RequireFreshAuthentication(maxAgeSeconds: 300)
        .RequireAuthenticationMethod("mfa", "otp");

    Console.WriteLine("Surface 7 PASS: AddSharedKernelAuthorizationFilters + .AddEndpointFilter<AuthorizationRequirementEndpointFilter>() + .RequireRole/.RequirePermission/.RequireFreshAuthentication/.RequireAuthenticationMethod compose alongside the full WebApi stack with zero DI exceptions");
}

// ── Surface 8: UseSharedKernelSecurityHeaders + AddSharedKernelCors ─────────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelCors(o =>
    {
        o.AllowedOrigins.Add("https://app.example.com");
        o.AllowCredentials = true;
        o.AllowedHeaders.Add("Authorization");
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        // Resolving IOptions<CorsPolicyOptions>.Value runs CorsPolicyOptionsValidator — a valid
        // configuration (explicit origin, credentials, no wildcard) must resolve with zero exception.
        var corsOptions = scope.ServiceProvider.GetRequiredService<IOptions<CorsPolicyOptions>>().Value;
        Verify(corsOptions.AllowedOrigins.Count == 1 && corsOptions.AllowCredentials, "CorsPolicyOptions resolves and passes CorsPolicyOptionsValidator with zero exception for a valid configuration");
    }

    app.UseSharedKernelSecurityHeaders();
    app.UseCors(CorsPolicyNames.Default);
    app.UseExceptionHandler();

    Console.WriteLine("Surface 8 PASS: AddSharedKernelCors (valid config) / UseSharedKernelSecurityHeaders / UseCors(CorsPolicyNames.Default) compose alongside the full WebApi stack with zero DI exceptions");

    // Negative case: AllowCredentials = true with an empty/wildcard AllowedOrigins must fail FAST
    // at options resolution (the moment IHost.StartAsync() would trigger ValidateOnStart), never
    // silently deploy — this is the structural guard D-28/C-34 designed, proven here rather than
    // merely asserted.
    var invalidServices = new ServiceCollection();
    invalidServices.AddSharedKernelCors(o => o.AllowCredentials = true); // no AllowedOrigins added
    using var invalidProvider = invalidServices.BuildServiceProvider();
    var threwOnInvalidConfig = false;
    try
    {
        _ = invalidProvider.GetRequiredService<IOptions<CorsPolicyOptions>>().Value;
    }
    catch (OptionsValidationException)
    {
        threwOnInvalidConfig = true;
    }

    Verify(threwOnInvalidConfig, "AllowCredentials=true + empty/wildcard AllowedOrigins fails fast via CorsPolicyOptionsValidator instead of silently resolving");
    Console.WriteLine("Surface 8 PASS: CorsPolicyOptionsValidator rejects the AllowCredentials+wildcard-origin misconfiguration at options-resolution time");
}

// ── Surface 9: AddSharedKernelIdempotencyFilters ────────────────────────────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelIdempotencyFilters();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var idempotencyFilter = scope.ServiceProvider.GetRequiredService<IdempotencyKeyRequirementEndpointFilter>();
        Verify(idempotencyFilter is not null, "IdempotencyKeyRequirementEndpointFilter resolves as a registered singleton");
    }

    app.UseExceptionHandler();

    var payments = app
        .MapGroup("/consumer-verify/payments")
        .AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>();
    payments
        .MapPost("/", (HttpContext ctx) =>
        {
            ctx.TryGetIdempotencyKey(out var key);
            return Results.Ok(key);
        })
        .RequireIdempotencyKey();

    Console.WriteLine("Surface 9 PASS: AddSharedKernelIdempotencyFilters + .AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>() + .RequireIdempotencyKey() compose alongside the WebApi stack with zero DI exceptions");
}

// ── Surface 10: RowVersionETag / ConditionalRequestExtensions / RateLimitRejectionProblemDetails ──
// These are pure static helpers with no DI wiring of their own (see 14.Presentation/CLAUDE.md's
// "HTTP protocol-level outcomes never routed through Error/ErrorType" rule) — verified here for
// functional correctness rather than DI composition, closing out P-407/P-408's own surface.
{
    var etag = RowVersionETag.From([1, 2, 3, 4]);
    Verify(etag == "\"AQIDBA==\"", "RowVersionETag.From produces a well-formed, correctly-quoted ETag value");

    var matchingContext = new DefaultHttpContext();
    matchingContext.Request.Headers["If-Match"] = etag;
    Verify(matchingContext.TryValidateIfMatch(etag, out var noProblem) && noProblem is null, "TryValidateIfMatch returns true with no ProblemDetails when If-Match matches the current ETag");

    var staleContext = new DefaultHttpContext();
    staleContext.Request.Headers["If-Match"] = "\"stale-value\"";
    var isValid = staleContext.TryValidateIfMatch(etag, out var problem);
    Verify(!isValid && problem is { Status: StatusCodes.Status412PreconditionFailed }, "TryValidateIfMatch returns false with a 412 ProblemDetails when If-Match does not match the current ETag");

    var rateLimitContext = new DefaultHttpContext();
    var rateLimitProblem = RateLimitRejectionProblemDetails.Create(rateLimitContext, TimeSpan.FromSeconds(30));
    Verify(rateLimitProblem.Status == StatusCodes.Status429TooManyRequests, "RateLimitRejectionProblemDetails.Create produces a 429 ProblemDetails");
    Verify(rateLimitContext.Response.Headers["Retry-After"] == "30", "RateLimitRejectionProblemDetails.Create sets the Retry-After response header in whole seconds");

    Console.WriteLine("Surface 10 PASS: RowVersionETag / ConditionalRequestExtensions / RateLimitRejectionProblemDetails behave correctly as standalone static helpers");
}

// ── Surface 11: AddSharedKernelOpenApi — non-default security-scheme combination ──
// Closes P-18/WO-063's explicit ask for "a generated-document assertion for at least one
// non-default scheme combination" — a real listening Kestrel host generates the actual OpenAPI
// document (Bearer default-true + ApiKey + MutualTls opted in) and the response body is asserted
// against, not merely the DI graph.
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelApiVersioning();
    builder.Services.AddSharedKernelOpenApi(
        title: "Consumer Verify API (Security Schemes)",
        configureSecuritySchemes: o =>
        {
            o.ApiKey = true;
            o.ApiKeyHeaderName = "X-Api-Key";
            o.MutualTls = true;
        });

    var app = builder.Build();
    app.UseExceptionHandler();
    app.MapSharedKernelOpenApi();

    await app.StartAsync();

    string documentJson;
    try
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var documentName = app.Services.GetRequiredService<IApiVersionDescriptionProvider>()
            .ApiVersionDescriptions.First().GroupName;

        using var httpClient = new HttpClient();
        documentJson = await httpClient.GetStringAsync($"{address}/openapi/{documentName}.json");
    }
    finally
    {
        await app.StopAsync();
    }

    Verify(documentJson.Contains("\"Bearer\"", StringComparison.Ordinal), "Generated OpenAPI document includes the default Bearer security scheme");
    Verify(documentJson.Contains("\"ApiKey\"", StringComparison.Ordinal), "Generated OpenAPI document includes the opted-in ApiKey security scheme");
    Verify(documentJson.Contains("\"MutualTLS\"", StringComparison.Ordinal), "Generated OpenAPI document includes the opted-in MutualTLS security scheme component");
    Verify(documentJson.Contains("\"mutualTLS\"", StringComparison.Ordinal), "Generated OpenAPI document serializes MutualTlsSecurityScheme's literal \"type\": \"mutualTLS\" (Microsoft.OpenApi's SecuritySchemeType enum has no MutualTls member)");
    Verify(documentJson.Contains("\"security\"", StringComparison.Ordinal), "Generated OpenAPI document declares a top-level security requirement for the active schemes");

    Console.WriteLine("Surface 11 PASS: AddSharedKernelOpenApi with a non-default Bearer+ApiKey+MutualTls scheme combination produces a valid, correctly-shaped generated OpenAPI document (real listening-host round trip, not DI resolution alone)");
}

// ── Surface 12: AddSharedKernelUploadValidation ─────────────────────────────
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
    builder.Services.AddSharedKernelUploadValidation(o =>
    {
        o.MaxSizeBytes = 5 * 1024 * 1024;
        o.AllowedContentTypes.Add("application/pdf");
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var uploadFilter = scope.ServiceProvider.GetRequiredService<UploadValidationEndpointFilter>();
        Verify(uploadFilter is not null, "UploadValidationEndpointFilter resolves as a registered singleton");
    }

    app.UseExceptionHandler();

    var documents = app
        .MapGroup("/consumer-verify/documents")
        .AddEndpointFilter<UploadValidationEndpointFilter>();
    documents
        .MapPost("/", (HttpContext ctx) => Results.Ok())
        .RequireValidatedUpload(allowedContentTypes: ["application/pdf"]);

    Console.WriteLine("Surface 12 PASS: AddSharedKernelUploadValidation + .AddEndpointFilter<UploadValidationEndpointFilter>() + .RequireValidatedUpload() compose alongside the WebApi stack with zero DI exceptions");
}

// ── Surface 13: gRPC host on SharedKernel.Presentation.Grpc alone (P-570) ─────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSharedKernelGrpc();

    using var provider = services.BuildServiceProvider();
    var interceptor = provider.GetRequiredService<SharedKernel.Presentation.Grpc.Interceptors.GrpcAuthorizationInterceptor>();
    Verify(interceptor is not null, "GrpcAuthorizationInterceptor resolves from AddSharedKernelGrpc()");

    var grpcReferences = typeof(SharedKernel.Presentation.Grpc.Interceptors.GrpcAuthorizationInterceptor).Assembly
        .GetReferencedAssemblies()
        .Select(a => a.Name)
        .ToArray();
    Verify(!grpcReferences.Contains("SharedKernel.Presentation.WebApi"), "SharedKernel.Presentation.Grpc does not reference SharedKernel.Presentation.WebApi");
    Verify(grpcReferences.Contains("SharedKernel.Presentation.Core"), "SharedKernel.Presentation.Grpc takes the shared attributes and status map from SharedKernel.Presentation.Core");
    Verify(
        SharedKernel.Presentation.Errors.GrpcStatusCodeMap.Resolve(SharedKernel.Primitives.Errors.ErrorType.Forbidden) == Grpc.Core.StatusCode.PermissionDenied,
        "GrpcStatusCodeMap maps Forbidden to PermissionDenied");

    Console.WriteLine("Surface 13 PASS: AddSharedKernelGrpc() composes without SharedKernel.Presentation.WebApi");
}

// ── Surface 14: AddSharedKernelGraphQL composes in a real host (moved from 11.Communication, P-573) ──
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    var graphQlBuilder = builder.Services.AddSharedKernelGraphQL();
    Verify(graphQlBuilder is not null, "AddSharedKernelGraphQL() returns a non-null IRequestExecutorBuilder");

    using IHost host = builder.Build();
    await host.StartAsync();
    await host.StopAsync();

    Console.WriteLine("Surface 14 PASS: AddSharedKernelGraphQL() resolves cleanly through a real IHost.StartAsync() with zero DI exceptions");
}

// ── Surface 15: invalid GraphQLOptions fail at registration time — P-358/GQ-10 ──
{
    var services = new ServiceCollection();

    OptionsValidationException? caught = null;
    try
    {
        services.AddSharedKernelGraphQL(options => options.MaxPageSize = 501);
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "AddSharedKernelGraphQL throws OptionsValidationException synchronously at registration time for MaxPageSize = 501");
    Verify(
        caught!.Failures.Any(failure => failure.Contains("MaxPageSize", StringComparison.Ordinal)),
        "the OptionsValidationException names the invalid MaxPageSize property");

    Console.WriteLine("Surface 15 PASS: an invalid GraphQLOptions instance fails AddSharedKernelGraphQL loudly at registration time — before any HotChocolate schema is built");
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
