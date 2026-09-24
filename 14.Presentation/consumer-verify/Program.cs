// consumer-verify — composes the four 14.Presentation packages the way a downstream service does and drives each one
// over a real connection: Kestrel on a loopback port, answered by a plain HttpClient, a SignalR HubConnection and a
// Grpc.Net.Client channel. Nothing is mocked; every check reads what a caller would read. Exits non-zero on the first
// failed check.
//   1. WebApi — AddSharedKernelWebApi/UseSharedKernelWebApi: typed results, the one application/problem+json error
//      shape, native authorization (401/403), an Idempotency-Key required by an IdempotencyKey parameter, ETag with
//      304, If-Match required by an IfMatch<long> parameter with 428/412 (the service's own version-conflict code
//      added to Problems.PreconditionFailedErrorCodes), both headers made optional by a nullable parameter (a missing
//      header reaches the handler as null, an unusable one is refused before it), a Paging parameter answering invalid
//      paging input with the validation problem before the handler, the exposed CORS headers, and the
//      automatic 429 body for a limiter registered with AddRateLimiter.
//   2. WebApi — settings that fail validation stop the host before it serves a request.
//   3. OpenApi — AddSharedKernelOpenApi/MapSharedKernelOpenApi generate one document per API version, each carrying the
//      ProblemDetails schema. Asp.Versioning.OpenApi reflects over Microsoft.AspNetCore.OpenApi internals, so a package
//      upgrade that breaks that fails here instead of in a service.
//   4. SignalR — AddSharedKernelSignalR: a hub method returning Result<T> returns its value; a failure reaches the
//      client as a HubException "{code}: {message}", read back with HubErrorMessage.TryParse, a server error redacted.
//   5. gRPC — AddSharedKernelGrpc: a failed result ended with SharedKernel.Core's GetValueOrThrow/ThrowIfFailure
//      reaches the client as a rich google.rpc.Status, read back with GetRpcStatus(): ErrorInfo (code, domain, trace
//      and correlation ids) and BadRequest.
//   6. All four packages in one host: the registrations compose and every protocol answers.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Asp.Versioning;
using ConsumerVerify.Grpc;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.SignalR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Security.Abstractions;

// Both System.Net.Http.Headers (EntityTagHeaderValue) and Microsoft.Net.Http.Headers declare header types; only the
// header-name constants are taken from the latter.
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

// The whole run takes seconds; a hung connection must fail the CI step, not stall the job until its timeout.
var watchdog = TimeSpan.FromMinutes(3);
var verification = VerifyAllSurfacesAsync();

if (await Task.WhenAny(verification, Task.Delay(watchdog)) != verification)
{
    Console.Error.WriteLine($"FAILED: consumer-verify did not finish within {watchdog}");
    return 1;
}

try
{
    await verification;
}
catch (VerificationException failure)
{
    Console.Error.WriteLine($"FAILED: {failure.Message}");
    return 1;
}
catch (Exception unexpected)
{
    Console.Error.WriteLine($"FAILED with an unexpected exception: {unexpected}");
    return 1;
}

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return 0;

static async Task VerifyAllSurfacesAsync()
{
    await Surface1_WebApiOneCallSetup();
    await Surface2_InvalidWebApiSettingsStopTheHost();
    await Surface3_OpenApiDocumentPerVersion();
    await Surface4_SignalRResultHubMethods();
    await Surface5_GrpcRichStatus();
    await Surface6_AllFourPackagesInOneHost();
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 1 — the WebApi core, set up with its two calls and nothing else.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface1_WebApiOneCallSetup()
{
    var builder = Hosts.CreateBuilder(Environments.Production);
    builder.AddSharedKernelWebApi(options =>
    {
        options.Cors.AllowedOrigins.Add(Values.AllowedOrigin);

        // The service's own version-conflict code: a conditional request failing with it is answered 412, like the
        // platform's persistence.concurrency_conflict. Every other conflict stays 409.
        options.Problems.PreconditionFailedErrorCodes.Add(Values.DocumentVersionMismatch);
    });
    builder.AddHeaderAuthentication();

    // No OnRejected: the WebApi core supplies the 429 problem body itself.
    builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(Values.OnePerWindowPolicy, limiter =>
    {
        limiter.PermitLimit = 1;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
    }));

    await using var app = builder.Build();
    app.UseSharedKernelWebApi();
    OrdersHttpApi.Map(app);
    await app.StartAsync();

    using var client = new HttpClient { BaseAddress = Hosts.AddressOf(app) };

    // Typed results: the success path of Result<T>.
    using (var found = await client.GetAsync("/orders/1"))
    {
        Check.Status(found, HttpStatusCode.OK, "GET /orders/1 (ToOk)");
        using var body = await Check.JsonAsync(found);
        Check.That(body.RootElement.GetProperty("id").GetInt32() == 1, "ToOk writes the value as JSON");
    }

    using (var created = await client.PostAsJsonAsync("/orders", new OrderDto(5, "open")))
    {
        Check.Status(created, HttpStatusCode.Created, "POST /orders (ToCreated)");
        Check.That(created.Headers.Location?.OriginalString == "/orders/5", "ToCreated sets Location from the value");
    }

    // The one error shape: a failed Result<T> is application/problem+json with every D1 member.
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/orders/404"))
    {
        request.Headers.Add(WellKnownHeaders.CorrelationId, "verify-flow-1");
        using var missing = await client.SendAsync(request);
        using var problem = await Check.ProblemAsync(missing, HttpStatusCode.NotFound, "order.not_found", "GET /orders/404");
        Check.That(problem.RootElement.GetProperty("detail").GetString() == "Order 404 was not found.", "detail carries the error message");
        Check.That(problem.RootElement.GetProperty("instance").GetString() == "/orders/404", "instance is the request path");
        Check.That(
            problem.RootElement.GetProperty(ProblemDetailsExtensionNames.CorrelationId).GetString() == "verify-flow-1",
            "a valid inbound correlation id is kept and reported in the problem");
    }

    // A framework-generated error gets the same shape, with an http.{status} code.
    using (var unmatched = await client.GetAsync("/no-such-route"))
    {
        (await Check.ProblemAsync(unmatched, HttpStatusCode.NotFound, "http.404", "an unmatched route")).Dispose();
    }

    // Native authorization: anonymous → 401, signed in without the permission → 403, with it → 200.
    using (var anonymous = await client.GetAsync("/orders/1/audit"))
    {
        (await Check.ProblemAsync(anonymous, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized.Default, "RequirePermission, anonymous")).Dispose();
    }

    using (var forbidden = await client.SendAsync(HeaderAuthentication.SignedIn(HttpMethod.Get, "/orders/1/audit", permissions: "orders.read")))
    {
        (await Check.ProblemAsync(forbidden, HttpStatusCode.Forbidden, ErrorCodes.Forbidden.InsufficientPermission, "RequirePermission, missing permission")).Dispose();
    }

    using (var allowed = await client.SendAsync(HeaderAuthentication.SignedIn(HttpMethod.Get, "/orders/1/audit", permissions: $"orders.read,{Values.AuditPermission}")))
    {
        Check.Status(allowed, HttpStatusCode.OK, "RequirePermission, permission held");
    }

    // An IdempotencyKey parameter requires the header: missing → 400, malformed → 400, valid (quoted or not) → the
    // handler receives the key without quotes.
    using (var withoutKey = await client.PostAsync("/payments", content: null))
    {
        (await Check.ProblemAsync(withoutKey, HttpStatusCode.BadRequest, PresentationErrorCodes.IdempotencyKeyRequired, "IdempotencyKey parameter, missing")).Dispose();
    }

    using (var request = new HttpRequestMessage(HttpMethod.Post, "/payments"))
    {
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, "not a valid key");
        using var malformed = await client.SendAsync(request);
        (await Check.ProblemAsync(malformed, HttpStatusCode.BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid, "IdempotencyKey parameter, malformed")).Dispose();
    }

    foreach (var sent in new[] { "pay-7f3a", "\"pay-7f3a\"" })
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments");
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, sent);
        using var accepted = await client.SendAsync(request);
        Check.Status(accepted, HttpStatusCode.OK, $"IdempotencyKey parameter, key {sent}");
        using var body = await Check.JsonAsync(accepted);
        Check.That(body.RootElement.GetProperty("key").GetString() == "pay-7f3a", $"the IdempotencyKey parameter carries the key sent as {sent}");
    }

    // A Paging parameter: absent parameters are the defaults; one out of range is the platform's validation problem,
    // keyed by the query parameter, with 04.Contracts' pagination code.
    using (var firstPage = await client.GetAsync("/orders"))
    {
        Check.Status(firstPage, HttpStatusCode.OK, "Paging parameter, defaults");
        using var body = await Check.JsonAsync(firstPage);
        Check.That(body.RootElement.GetProperty("page").GetInt32() == 1 && body.RootElement.GetProperty("pageSize").GetInt32() == PageRequest.DefaultPageSize, "Paging binds the defaults of PageRequest.Create");
    }

    using (var outOfRange = await client.GetAsync("/orders?pageSize=0"))
    {
        using var problem = await Check.ProblemAsync(outOfRange, HttpStatusCode.BadRequest, ErrorCodes.Validation.Failed, "Paging parameter, pageSize=0");
        Check.That(
            problem.RootElement.GetProperty(ProblemDetailsExtensionNames.ErrorCodes).GetProperty("pageSize")[0].GetString() == PaginationErrorCodes.PageSizeOutOfRange,
            "an invalid pageSize is reported under pageSize with its pagination code");
    }

    // ETag and 304: ToOkWithETag answers a matching If-None-Match with 304 and no body.
    using (var document = await client.GetAsync("/documents/1"))
    {
        Check.Status(document, HttpStatusCode.OK, "GET /documents/1 (ToOkWithETag)");
        Check.That(document.Headers.ETag?.Tag == Values.ETagOf(Values.CurrentDocumentVersion), "ToOkWithETag writes the version as a quoted ETag");
    }

    using (var request = new HttpRequestMessage(HttpMethod.Get, "/documents/1"))
    {
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Values.ETagOf(Values.CurrentDocumentVersion)));
        using var notModified = await client.SendAsync(request);
        Check.Status(notModified, HttpStatusCode.NotModified, "GET /documents/1 with a matching If-None-Match");
        Check.That(notModified.Headers.ETag is not null, "a 304 repeats the ETag");
        Check.That((await notModified.Content.ReadAsByteArrayAsync()).Length == 0, "a 304 has no body");
    }

    // An IfMatch<long> parameter requires the header: missing → 428; a tag that is no version (not a long) → 412
    // precondition.failed before the handler runs; a stale version → the handler's Conflict, 412 because its code is
    // configured above; the current version → 204.
    using (var withoutIfMatch = await client.PutAsync("/documents/1", content: null))
    {
        (await Check.ProblemAsync(withoutIfMatch, HttpStatusCode.PreconditionRequired, PresentationErrorCodes.PreconditionRequired, "IfMatch<long> parameter, missing")).Dispose();
    }

    using (var request = new HttpRequestMessage(HttpMethod.Put, "/documents/1"))
    {
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"v6\""));
        using var notAVersion = await client.SendAsync(request);
        (await Check.ProblemAsync(notAVersion, HttpStatusCode.PreconditionFailed, PresentationErrorCodes.PreconditionFailed, "IfMatch<long> parameter, a tag that is no version")).Dispose();
    }

    using (var request = new HttpRequestMessage(HttpMethod.Put, "/documents/1"))
    {
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(Values.ETagOf(Values.CurrentDocumentVersion - 1)));
        using var stale = await client.SendAsync(request);
        (await Check.ProblemAsync(stale, HttpStatusCode.PreconditionFailed, Values.DocumentVersionMismatch, "IfMatch<long> parameter, stale version")).Dispose();
    }

    using (var request = new HttpRequestMessage(HttpMethod.Put, "/documents/1"))
    {
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(Values.ETagOf(Values.CurrentDocumentVersion)));
        using var updated = await client.SendAsync(request);
        Check.Status(updated, HttpStatusCode.NoContent, "IfMatch<long> parameter, current version");
    }

    // An IdempotencyKey? parameter accepts the header without requiring it: missing → the handler runs with null; a
    // valid key → the handler gets it; an invalid one → 400 before the handler runs, never read as "no key".
    await OptionalHeaders.VerifyAsync(
        client,
        HttpMethod.Post,
        "/transfers",
        WellKnownHeaders.IdempotencyKey,
        "IdempotencyKey? parameter",
        (null, HttpStatusCode.OK, null, null),
        ("\"pay-7f3a\"", HttpStatusCode.OK, "pay-7f3a", null),
        ("not a valid key", HttpStatusCode.BadRequest, null, PresentationErrorCodes.IdempotencyKeyInvalid));

    // An IfMatch<long>? parameter accepts If-Match the same way: missing → null (unconditional); one strong tag that is a
    // long → its version; malformed or * → 400; weak, or a tag that is no version → 412; each before the handler runs.
    await OptionalHeaders.VerifyAsync(
        client,
        HttpMethod.Delete,
        "/documents/1",
        HeaderNames.IfMatch,
        "IfMatch<long>? parameter",
        (null, HttpStatusCode.OK, null, null),
        (Values.ETagOf(Values.CurrentDocumentVersion), HttpStatusCode.OK, Values.VersionText(Values.CurrentDocumentVersion), null),
        (Values.VersionText(Values.CurrentDocumentVersion), HttpStatusCode.BadRequest, null, PresentationErrorCodes.PreconditionInvalid),
        ("*", HttpStatusCode.BadRequest, null, PresentationErrorCodes.PreconditionInvalid),
        ("W/" + Values.ETagOf(Values.CurrentDocumentVersion), HttpStatusCode.PreconditionFailed, null, PresentationErrorCodes.PreconditionFailed),
        ("\"v6\"", HttpStatusCode.PreconditionFailed, null, PresentationErrorCodes.PreconditionFailed));

    // CORS from settings: the allowed origin gets the platform's exposed headers, another origin gets nothing.
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1"))
    {
        request.Headers.Add(HeaderNames.Origin, Values.AllowedOrigin);
        using var crossOrigin = await client.SendAsync(request);
        Check.Status(crossOrigin, HttpStatusCode.OK, "a cross-origin GET from the allowed origin");
        Check.That(
            crossOrigin.Headers.TryGetValues(HeaderNames.AccessControlAllowOrigin, out var allowOrigin) && allowOrigin.Single() == Values.AllowedOrigin,
            "the allowed origin is echoed in Access-Control-Allow-Origin");

        var exposed = crossOrigin.Headers.TryGetValues(HeaderNames.AccessControlExposeHeaders, out var exposeValues)
            ? string.Join(',', exposeValues).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];
        foreach (var header in new[] { WellKnownHeaders.CorrelationId, HeaderNames.ETag, HeaderNames.Location, HeaderNames.RetryAfter })
        {
            Check.That(exposed.Contains(header, StringComparer.OrdinalIgnoreCase), $"Access-Control-Expose-Headers includes {header}");
        }
    }

    using (var request = new HttpRequestMessage(HttpMethod.Options, "/orders"))
    {
        request.Headers.Add(HeaderNames.Origin, Values.AllowedOrigin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, HttpMethods.Post);
        using var preflight = await client.SendAsync(request);
        Check.Status(preflight, HttpStatusCode.NoContent, "a preflight from the allowed origin");
        Check.That(preflight.Headers.Contains(HeaderNames.AccessControlAllowOrigin), "a preflight from the allowed origin is allowed");
    }

    using (var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1"))
    {
        request.Headers.Add(HeaderNames.Origin, "https://elsewhere.example.com");
        using var otherOrigin = await client.SendAsync(request);
        Check.That(!otherOrigin.Headers.Contains(HeaderNames.AccessControlAllowOrigin), "an origin that is not configured gets no Access-Control-Allow-Origin");
    }

    // Rate limiting registered with plain AddRateLimiter: the rejection is a 429 problem with Retry-After.
    using (var first = await client.GetAsync("/limited"))
    {
        Check.Status(first, HttpStatusCode.OK, "the first request within the window");
    }

    using (var rejected = await client.GetAsync("/limited"))
    {
        (await Check.ProblemAsync(rejected, HttpStatusCode.TooManyRequests, PresentationErrorCodes.RateLimitExceeded, "the second request within the window")).Dispose();
        Check.That(rejected.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero, "the 429 carries the limiter's Retry-After");
    }

    Console.WriteLine("Surface 1 PASSED — WebApi: typed results, problem+json, 401/403, Idempotency-Key, ETag/304, If-Match 428/412, optional headers, paging, CORS, 429");
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 2 — invalid settings are refused before the host serves anything.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface2_InvalidWebApiSettingsStopTheHost()
{
    var builder = Hosts.CreateBuilder(Environments.Production);

    // Credentials with no explicit origin: browsers refuse it, so the platform refuses to start.
    builder.AddSharedKernelWebApi(options => options.Cors.AllowCredentials = true);

    // On Kestrel the settings are first read while Build() creates the server (they configure its limits), so the
    // failure can come from Build(), UseSharedKernelWebApi() or StartAsync(); all three are refusals to serve.
    WebApplication? app = null;
    var failure = await Check.CatchAsync<OptionsValidationException>(async () =>
    {
        app = builder.Build();
        app.UseSharedKernelWebApi();
        await app.StartAsync();
    });

    if (app is not null)
    {
        await app.DisposeAsync();
    }

    Check.That(failure is not null, "AllowCredentials without AllowedOrigins fails with OptionsValidationException");
    Check.That(failure!.Message.Contains("AllowCredentials", StringComparison.Ordinal), "the failure names the setting to fix");

    Console.WriteLine("Surface 2 PASSED — WebApi: settings that fail validation stop the host");
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 3 — the OpenAPI add-on generates one document per API version.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface3_OpenApiDocumentPerVersion()
{
    var builder = Hosts.CreateBuilder(Environments.Development);
    builder.AddSharedKernelWebApi();
    builder.AddSharedKernelOpenApi(options => options.Title = "Consumer Verify Orders");

    await using var app = builder.Build();
    app.UseSharedKernelWebApi();
    OrdersHttpApi.MapVersioned(app);
    app.MapSharedKernelOpenApi();
    await app.StartAsync();

    using var client = new HttpClient { BaseAddress = Hosts.AddressOf(app) };

    foreach (var (name, version, historyPath) in new[] { ("v1", "1.0", "/v1/orders/{id}/history"), ("v2", "2.0", "/v2/orders/{id}/history") })
    {
        using var response = await client.GetAsync($"/openapi/{name}.json");
        Check.Status(response, HttpStatusCode.OK, $"GET /openapi/{name}.json");

        using var document = await Check.JsonAsync(response);
        var root = document.RootElement;

        Check.That(root.GetProperty("openapi").GetString()?.StartsWith("3.1", StringComparison.Ordinal) == true, $"{name} is an OpenAPI 3.1 document");
        Check.That(root.GetProperty("info").GetProperty("version").GetString() == version, $"{name} describes API version {version}");
        Check.That(root.GetProperty("info").GetProperty("title").GetString() == "Consumer Verify Orders", $"{name} carries the configured title");

        Check.That(
            root.TryGetProperty("components", out var components)
                && components.TryGetProperty("schemas", out var schemas)
                && schemas.TryGetProperty("ProblemDetails", out var problemSchema)
                && problemSchema.TryGetProperty("properties", out var problemProperties)
                && problemProperties.TryGetProperty(ProblemDetailsExtensionNames.ErrorCode, out _)
                && problemProperties.TryGetProperty(ProblemDetailsExtensionNames.TraceId, out _),
            $"{name} describes the ProblemDetails schema with errorCode and traceId");

        var getOrder = root.GetProperty("paths").GetProperty($"/{name}/orders/{{id}}").GetProperty("get");
        Check.That(
            getOrder.GetProperty("responses").TryGetProperty("default", out var defaultResponse)
                && defaultResponse.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString()
                    == "#/components/schemas/ProblemDetails",
            $"{name}: GET orders/{{id}} documents its errors as the ProblemDetails schema");

        var hasHistory = root.GetProperty("paths").TryGetProperty(historyPath, out _);
        Check.That(hasHistory == (name == "v2"), $"{name} lists the history endpoint only when it belongs to that version");
    }

    using (var reference = await client.GetAsync("/scalar/"))
    {
        Check.Status(reference, HttpStatusCode.OK, "the Scalar API reference");
    }

    Console.WriteLine("Surface 3 PASSED — OpenApi: /openapi/v1.json and /openapi/v2.json, each with the ProblemDetails schema");
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 4 — SignalR hub methods returning Result<T>, over a WebSocket connection.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface4_SignalRResultHubMethods()
{
    var builder = Hosts.CreateBuilder(Environments.Production);
    builder.AddSharedKernelWebApi();
    builder.AddSharedKernelSignalR();

    await using var app = builder.Build();
    app.UseSharedKernelWebApi();
    app.MapHub<OrdersHub>(OrdersHub.Path);
    await app.StartAsync();

    await using var connection = await Hubs.ConnectAsync(Hosts.AddressOf(app));

    var order = await connection.InvokeAsync<OrderDto>(nameof(OrdersHub.GetOrder), 1);
    Check.That(order == new OrderDto(1, "open"), "a successful Result<T> returns its value to the client");

    var notFound = await Hubs.InvokeExpectingErrorAsync(connection, nameof(OrdersHub.GetOrder), 404);
    Check.That(notFound == ("order.not_found", "Order 404 was not found."), $"a failed Result<T> arrives as its code and message (got {notFound})");

    var outage = await Hubs.InvokeExpectingErrorAsync(connection, nameof(OrdersHub.GetOrderDuringOutage), 1);
    Check.That(outage.Code == "orders.store_unavailable", $"a server error keeps its code (got {outage})");
    Check.That(!outage.Message.Contains("db-7", StringComparison.Ordinal), "a server error's internal detail is redacted outside Development");

    Console.WriteLine("Surface 4 PASSED — SignalR: Result<T> values, coded HubException read with HubErrorMessage.TryParse, server errors redacted");
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 5 — gRPC failures as a rich google.rpc.Status, over HTTP/2.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface5_GrpcRichStatus()
{
    var builder = Hosts.CreateBuilder(Environments.Production, useDefaultUrl: false);
    ListenOptions? grpcListener = null;
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen =>
    {
        listen.Protocols = HttpProtocols.Http2;
        grpcListener = listen;
    }));

    builder.AddSharedKernelWebApi();
    builder.AddSharedKernelGrpc(options => options.ErrorDomain = Values.GrpcErrorDomain);

    await using var app = builder.Build();
    app.UseSharedKernelWebApi();
    app.MapGrpcService<OrderGrpcService>();
    await app.StartAsync();

    using var channel = GrpcChannel.ForAddress(Hosts.AddressOf(grpcListener));
    await GrpcChecks.VerifyAsync(new OrderService.OrderServiceClient(channel), "gRPC host");

    Console.WriteLine("Surface 5 PASSED — gRPC: Core's GetValueOrThrow/ThrowIfFailure as google.rpc.Status with ErrorInfo and BadRequest");
}

// -------------------------------------------------------------------------------------------------------------------
// Surface 6 — one service composing all four packages: HTTP/1.1 for the API, documents and hub, HTTP/2 for gRPC.
// -------------------------------------------------------------------------------------------------------------------
static async Task Surface6_AllFourPackagesInOneHost()
{
    var builder = Hosts.CreateBuilder(Environments.Production, useDefaultUrl: false);
    ListenOptions? httpListener = null;
    ListenOptions? grpcListener = null;
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
            httpListener = listen;
        });
        kestrel.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
            grpcListener = listen;
        });
    });

    builder.AddSharedKernelWebApi();
    builder.AddSharedKernelOpenApi(options => options.ExposeInProduction = true);
    builder.AddSharedKernelSignalR();
    builder.AddSharedKernelGrpc(options => options.ErrorDomain = Values.GrpcErrorDomain);
    builder.AddHeaderAuthentication();

    await using var app = builder.Build();
    app.UseSharedKernelWebApi();
    OrdersHttpApi.MapVersioned(app);
    app.MapHub<OrdersHub>(OrdersHub.Path);
    app.MapGrpcService<OrderGrpcService>();
    app.MapSharedKernelOpenApi();
    await app.StartAsync();

    var httpAddress = Hosts.AddressOf(httpListener);
    using var client = new HttpClient { BaseAddress = httpAddress };

    using (var found = await client.GetAsync("/v1/orders/1"))
    {
        Check.Status(found, HttpStatusCode.OK, "combined host: GET /v1/orders/1");
    }

    using (var missing = await client.GetAsync("/v1/orders/404"))
    {
        (await Check.ProblemAsync(missing, HttpStatusCode.NotFound, "order.not_found", "combined host: GET /v1/orders/404")).Dispose();
    }

    using (var document = await client.GetAsync("/openapi/v1.json"))
    {
        Check.Status(document, HttpStatusCode.OK, "combined host: the v1 document, exposed in Production on request");
        Check.That((await document.Content.ReadAsStringAsync()).Contains("\"ProblemDetails\"", StringComparison.Ordinal), "combined host: the v1 document carries the ProblemDetails schema");
    }

    await using (var connection = await Hubs.ConnectAsync(httpAddress))
    {
        var order = await connection.InvokeAsync<OrderDto>(nameof(OrdersHub.GetOrder), 1);
        Check.That(order == new OrderDto(1, "open"), "combined host: the hub answers");
    }

    using var channel = GrpcChannel.ForAddress(Hosts.AddressOf(grpcListener));
    await GrpcChecks.VerifyAsync(new OrderService.OrderServiceClient(channel), "combined host");

    Console.WriteLine("Surface 6 PASSED — WebApi, OpenApi, SignalR and gRPC compose in one host");
}

// ===================================================================================================================
// The service under verification: one order catalog, exposed over HTTP, SignalR and gRPC.
// ===================================================================================================================

/// <summary>An order, as every protocol returns it.</summary>
public sealed record OrderDto(int Id, string Status);

/// <summary>A versioned document, returned with its version as an ETag.</summary>
public sealed record DocumentDto(int Id, long Version);

/// <summary>Values shared by the service and the checks.</summary>
internal static class Values
{
    public const string AllowedOrigin = "https://app.example.com";

    public const string OnePerWindowPolicy = "one-per-window";

    public const string AuditPermission = "orders.audit";

    /// <summary>The version every document is at.</summary>
    public const long CurrentDocumentVersion = 7;

    /// <summary>The service's own code for an update that names a version that is no longer current.</summary>
    public const string DocumentVersionMismatch = "document.version_mismatch";

    public const string GrpcErrorDomain = "consumer-verify.example";

    /// <summary>The ETag of a document version, as sent on the wire: <c>"7"</c>.</summary>
    public static string ETagOf(long version) => $"\"{VersionText(version)}\"";

    /// <summary>A document version as text, the value <c>ToOkWithETag</c> quotes.</summary>
    public static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The application layer: every operation returns a <see cref="Result"/>.</summary>
internal static class OrderCatalog
{
    public static Result<OrderDto> Find(int id) =>
        id == 1 ? new OrderDto(1, "open") : Error.NotFound("order.not_found", $"Order {id} was not found.");

    public static Result Place(int quantity, string sku)
    {
        List<Error> errors = [];

        if (quantity < 1)
        {
            errors.Add(Error.Validation("order.quantity_invalid", "Quantity must be at least 1."));
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            errors.Add(Error.Validation("order.sku_required", "A SKU is required."));
        }

        return errors.Count == 0 ? Result.Success() : Error.Validation(errors);
    }
}

/// <summary>The HTTP API, mapped with the WebApi core's typed results and endpoint conventions.</summary>
internal static class OrdersHttpApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/orders/{id:int}", (int id) => OrderCatalog.Find(id).ToOk());
        app.MapPost("/orders", (OrderDto order) => Result<OrderDto>.Success(order).ToCreated(created => $"/orders/{created.Id}"));
        app.MapGet("/orders/{id:int}/audit", (int id) => OrderCatalog.Find(id).ToOk()).RequirePermission(Values.AuditPermission);

        // Declaring the parameter requires, validates and documents the header; the handler always gets a valid key.
        app.MapPost("/payments", (IdempotencyKey key) => TypedResults.Ok(new { key = key.Value }));

        app.MapGet("/documents/{id:int}", (int id) =>
            Result<DocumentDto>.Success(new DocumentDto(id, Values.CurrentDocumentVersion))
                .ToOkWithETag(document => Values.VersionText(document.Version)));

        // Declaring the parameter requires If-Match with one strong tag that parses as a long; the handler gets the
        // version and reports a stale one with the service's own conflict code.
        app.MapPut("/documents/{id:int}", (int id, IfMatch<long> ifMatch) =>
        {
            var result = ifMatch.Version == Values.CurrentDocumentVersion
                ? Result.Success()
                : Result.Failure(Error.Conflict(Values.DocumentVersionMismatch, $"Document {id} was changed by someone else."));

            return result.ToNoContent();
        });

        // Declared nullable, the parameters accept the header without requiring it: the handler gets null only when the
        // request sent none, and reports what it saw.
        app.MapPost("/transfers", (IdempotencyKey? key) => OptionalHeaders.Seen(key?.Value));
        app.MapDelete("/documents/{id:int}", (int id, IfMatch<long>? ifMatch) =>
            OptionalHeaders.Seen(ifMatch is null ? null : Values.VersionText(ifMatch.Version)));

        // Paging binds page and pageSize into a validated PageRequest; invalid input is refused before the handler.
        app.MapGet("/orders", (Paging paging) => TypedResults.Ok(new { page = paging.Request.Page, pageSize = paging.Request.PageSize }));

        app.MapGet("/limited", () => "ok").RequireRateLimiting(Values.OnePerWindowPolicy);
    }

    /// <summary>The same catalog as a versioned API: GET orders/{id} in 1.0 and 2.0, its history in 2.0 only.</summary>
    public static void MapVersioned(WebApplication app)
    {
        var orders = app.NewVersionedApi("Orders")
            .MapGroup("/v{version:apiVersion}/orders")
            .HasApiVersion(new ApiVersion(1, 0))
            .HasApiVersion(new ApiVersion(2, 0));

        orders.MapGet("/{id:int}", (int id) => OrderCatalog.Find(id).ToOk());
        orders.MapGet("/{id:int}/history", (int id) => OrderCatalog.Find(id).ToOk(order => new[] { order.Status }))
            .MapToApiVersion(new ApiVersion(2, 0));
    }
}

/// <summary>The SignalR hub: methods return <see cref="Result{T}"/> exactly like the application layer.</summary>
public sealed class OrdersHub : Hub
{
    public const string Path = "/hubs/orders";

    public Result<OrderDto> GetOrder(int id) => OrderCatalog.Find(id);

    public Result<OrderDto> GetOrderDuringOutage(int id) =>
        Error.Unavailable("orders.store_unavailable", $"Connection to db-7 refused while reading order {id}.");
}

/// <summary>
/// The gRPC service: a failed result ends the call through <c>SharedKernel.Core</c>'s <c>GetValueOrThrow</c> and
/// <c>ThrowIfFailure</c>, whose exception the platform's interceptor turns into the rich status.
/// </summary>
internal sealed class OrderGrpcService : OrderService.OrderServiceBase
{
    public override Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        var order = OrderCatalog.Find(request.Id).GetValueOrThrow();
        return Task.FromResult(new OrderReply { Id = order.Id, Status = order.Status });
    }

    public override Task<OrderReply> PlaceOrder(PlaceOrderRequest request, ServerCallContext context)
    {
        OrderCatalog.Place(request.Quantity, request.Sku).ThrowIfFailure();
        return Task.FromResult(new OrderReply { Id = 99, Status = "placed" });
    }
}

// ===================================================================================================================
// Verification helpers.
// ===================================================================================================================

/// <summary>Builds hosts on Kestrel at an ephemeral loopback port.</summary>
internal static class Hosts
{
    private const string LoopbackUrl = "http://127.0.0.1:0";

    public static WebApplicationBuilder CreateBuilder(string environment, bool useDefaultUrl = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();

        if (useDefaultUrl)
        {
            builder.WebHost.UseUrls(LoopbackUrl);
        }

        return builder;
    }

    /// <summary>The address of a started host with one listener.</summary>
    public static Uri AddressOf(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single());

    /// <summary>The address a listener was bound to; Kestrel writes the assigned port back when it starts.</summary>
    public static Uri AddressOf(ListenOptions? listener)
    {
        var endPoint = listener?.IPEndPoint;
        Check.That(endPoint is { Port: > 0 }, "Kestrel reports the port it bound the listener to");
        return new Uri($"http://{endPoint}");
    }
}

/// <summary>A header-driven authentication scheme and the <see cref="IUserContextMapper"/> an authentication package registers.</summary>
internal static class HeaderAuthentication
{
    public const string SchemeName = "Header";

    public const string UserHeader = "X-Verify-User";

    public const string PermissionsHeader = "X-Verify-Permissions";

    private const string SubjectClaim = "sub";

    private const string PermissionClaim = "perm";

    public static WebApplicationBuilder AddHeaderAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, Handler>(SchemeName, _ => { });
        builder.Services.AddSingleton<IUserContextMapper, Mapper>();
        return builder;
    }

    public static HttpRequestMessage SignedIn(HttpMethod method, string path, string permissions)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(UserHeader, "user-1");
        request.Headers.Add(PermissionsHeader, permissions);
        return request;
    }

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].ToString();
            if (string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new(SubjectClaim, user)];
            claims.AddRange(Request.Headers[PermissionsHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(PermissionClaim, permission)));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class Mapper : IUserContextMapper
    {
        public string AuthenticationType => SchemeName;

        public IUserContext Map(ClaimsIdentity identity) =>
            new UserContext(IdentityKind.User, identity.FindFirst(SubjectClaim)!.Value)
            {
                Permissions = [.. identity.FindAll(PermissionClaim).Select(claim => claim.Value)],
            };
    }
}

/// <summary>SignalR client helpers.</summary>
internal static class Hubs
{
    public static async Task<HubConnection> ConnectAsync(Uri baseAddress)
    {
        var connection = new HubConnectionBuilder().WithUrl(new Uri(baseAddress, OrdersHub.Path)).Build();

        try
        {
            await connection.StartAsync();
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    /// <summary>
    /// Invokes a method expected to fail and returns the error code and message the server sent, read with
    /// <see cref="HubErrorMessage.TryParse"/>: the client receives them behind SignalR's own sentence.
    /// </summary>
    public static async Task<(string Code, string Message)> InvokeExpectingErrorAsync(HubConnection connection, string method, params object?[] arguments)
    {
        var failure = await Check.CatchAsync<HubException>(() => connection.InvokeCoreAsync<object?>(method, arguments));
        Check.That(failure is not null, $"{method} fails with a HubException");

        Check.That(
            HubErrorMessage.TryParse(failure!.Message, out var code, out var message),
            $"{method} fails with a coded HubException (got \"{failure.Message}\")");
        return (code!, message!);
    }
}

/// <summary>The gRPC checks, run against both the dedicated gRPC host and the combined host.</summary>
internal static class GrpcChecks
{
    public static async Task VerifyAsync(OrderService.OrderServiceClient client, string host)
    {
        var reply = await client.GetOrderAsync(new GetOrderRequest { Id = 1 });
        Check.That(reply.Id == 1 && reply.Status == "open", $"{host}: a successful Result<T> returns its value");

        var headers = new Metadata { { WellKnownHeaders.CorrelationId, "verify-grpc-1" } };
        var notFound = await Check.CatchAsync<RpcException>(async () => await client.GetOrderAsync(new GetOrderRequest { Id = 404 }, headers));
        Check.That(notFound?.StatusCode == StatusCode.NotFound, $"{host}: order.not_found maps to NotFound (got {notFound?.StatusCode})");

        var status = notFound!.GetRpcStatus();
        Check.That(status is not null, $"{host}: the failure carries a google.rpc.Status (GetRpcStatus)");
        Check.That(status!.Code == (int)StatusCode.NotFound, $"{host}: the rich status repeats the status code");
        Check.That(status.Message == "Order 404 was not found.", $"{host}: the rich status carries the client message");

        var errorInfo = status.GetDetail<ErrorInfo>();
        Check.That(errorInfo is not null, $"{host}: the rich status carries an ErrorInfo detail");
        Check.That(errorInfo!.Reason == "order.not_found", $"{host}: ErrorInfo.Reason is the error code");
        Check.That(errorInfo.Domain == Values.GrpcErrorDomain, $"{host}: ErrorInfo.Domain is the configured ErrorDomain");
        Check.That(
            errorInfo.Metadata.TryGetValue(ProblemDetailsExtensionNames.TraceId, out var traceId) && !string.IsNullOrWhiteSpace(traceId),
            $"{host}: ErrorInfo carries the trace id");
        Check.That(
            errorInfo.Metadata.TryGetValue(ProblemDetailsExtensionNames.CorrelationId, out var correlationId) && correlationId == "verify-grpc-1",
            $"{host}: ErrorInfo carries the caller's correlation id");

        var invalid = await Check.CatchAsync<RpcException>(async () => await client.PlaceOrderAsync(new PlaceOrderRequest { Quantity = 0, Sku = string.Empty }));
        Check.That(invalid?.StatusCode == StatusCode.InvalidArgument, $"{host}: a validation failure maps to InvalidArgument (got {invalid?.StatusCode})");

        var badRequest = invalid!.GetRpcStatus()?.GetDetail<BadRequest>();
        Check.That(badRequest is not null, $"{host}: a validation failure carries a BadRequest detail");
        Check.That(
            badRequest!.FieldViolations.Select(violation => violation.Reason).SequenceEqual(["order.quantity_invalid", "order.sku_required"]),
            $"{host}: BadRequest lists every field violation with its code");
    }
}

/// <summary>
/// The handlers that accept an optional header, and the checks that drive them. Each handler run is counted, so a check
/// can tell that a refusal came before the handler.
/// </summary>
internal static class OptionalHeaders
{
    private static int _handlerRuns;

    /// <summary>Counts a handler run and answers with the header value the handler saw (null when none was sent).</summary>
    public static IResult Seen(string? value)
    {
        Interlocked.Increment(ref _handlerRuns);
        return TypedResults.Ok(new SeenHeader(value));
    }

    /// <summary>
    /// Sends one request per case — the header value to send (null: none), the expected status, and either the value the
    /// handler must see or the code of the refusal — and checks that the handler ran exactly when the request passed.
    /// </summary>
    public static async Task VerifyAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string header,
        string what,
        params (string? Sent, HttpStatusCode Status, string? Seen, string? Code)[] cases)
    {
        foreach (var (sent, status, seen, code) in cases)
        {
            var runs = Volatile.Read(ref _handlerRuns);
            using var request = new HttpRequestMessage(method, path);
            if (sent is not null)
            {
                request.Headers.TryAddWithoutValidation(header, sent);
            }

            using var response = await client.SendAsync(request);
            var label = sent is null ? $"{what}, no header" : $"{what}, header {sent}";

            if (code is null)
            {
                Check.Status(response, status, label);
                using var body = await Check.JsonAsync(response);
                var actual = body.RootElement.GetProperty("seen");
                Check.That(
                    seen is null ? actual.ValueKind == JsonValueKind.Null : actual.GetString() == seen,
                    $"{label}: the handler gets {seen ?? "null"}");
                Check.That(Volatile.Read(ref _handlerRuns) == runs + 1, $"{label}: the handler runs");
            }
            else
            {
                (await Check.ProblemAsync(response, status, code, label)).Dispose();
                Check.That(Volatile.Read(ref _handlerRuns) == runs, $"{label}: refused before the handler runs");
            }
        }
    }
}

/// <summary>What a handler accepting an optional header saw: its value, or null when the request sent none.</summary>
public sealed record SeenHeader(string? Seen);

/// <summary>Assertions that stop the run with a message naming what failed.</summary>
internal static class Check
{
    private const string ProblemJson = "application/problem+json";

    public static void That(bool condition, string what)
    {
        if (!condition)
        {
            throw new VerificationException(what);
        }
    }

    public static void Status(HttpResponseMessage response, HttpStatusCode expected, string what) =>
        That(response.StatusCode == expected, $"{what}: expected {(int)expected}, got {(int)response.StatusCode}");

    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>
    /// Asserts the platform's one error shape: <c>application/problem+json</c> with <c>status</c>, <c>errorCode</c>, a
    /// <c>title</c> that is the reason phrase (never the code), <c>type</c>, <c>instance</c>, <c>traceId</c> and a
    /// <c>correlationId</c> matching the response header.
    /// </summary>
    public static async Task<JsonDocument> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode, string what)
    {
        var body = await response.Content.ReadAsStringAsync();

        That(response.StatusCode == status, $"{what}: expected {(int)status}, got {(int)response.StatusCode} {body}");
        That(response.Content.Headers.ContentType?.MediaType == ProblemJson, $"{what}: expected {ProblemJson}, got {response.Content.Headers.ContentType}");

        var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        That(root.GetProperty("status").GetInt32() == (int)status, $"{what}: the body's status");
        That(root.GetProperty(ProblemDetailsExtensionNames.ErrorCode).GetString() == errorCode, $"{what}: errorCode {errorCode} (got {body})");
        That(root.GetProperty("title").GetString() is { Length: > 0 } title && title != errorCode, $"{what}: title is the reason phrase, not the code");
        That(root.GetProperty("type").GetString() is { Length: > 0 }, $"{what}: type is set");
        That(root.GetProperty("instance").GetString()?.StartsWith('/') == true, $"{what}: instance is the request path");
        That(root.GetProperty(ProblemDetailsExtensionNames.TraceId).GetString() is { Length: > 0 }, $"{what}: traceId is set");

        var correlationId = root.GetProperty(ProblemDetailsExtensionNames.CorrelationId).GetString();
        That(
            correlationId is { Length: > 0 }
                && response.Headers.TryGetValues(WellKnownHeaders.CorrelationId, out var header)
                && header.Single() == correlationId,
            $"{what}: correlationId matches the {WellKnownHeaders.CorrelationId} response header");

        return document;
    }

    public static async Task<TException?> CatchAsync<TException>(Func<Task> act)
        where TException : Exception
    {
        try
        {
            await act();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}

/// <summary>A failed check.</summary>
internal sealed class VerificationException(string message) : Exception(message);
