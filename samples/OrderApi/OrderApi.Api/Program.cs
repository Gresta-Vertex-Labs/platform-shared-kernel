using OrderApi.Application.Features.Orders;
using OrderApi.Infrastructure;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

// Host defaults: OpenTelemetry (traces, metrics, logs), health endpoints, the startup gate.
builder.AddServiceDefaults();

// IClock is the only sanctioned time source; analyzer SK0001 forbids DateTime.UtcNow.
builder.Services.AddSingleton<IClock, SystemClock>();

// Who is calling. The sample has no identity provider, so every caller is anonymous; a real service registers
// AddOidcAuthentication(configuration) (SharedKernel.Security.Oidc) instead and nothing below changes.
// AddSharedKernelRequestContext() turns the caller into the one IRequestContext every behavior and adapter reads,
// which is what CancelOrderCommand's [RequirePermission] is checked against. OrderApi.Tests swaps in a test
// authentication scheme to prove the 401/403/204 answers.
builder.Services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);
builder.Services.AddSharedKernelRequestContext();

// The adapters behind the application's ports: the order store, its readiness probe, the FluentValidation bridge.
builder.Services.AddOrderInfrastructure();

// The application layer in one call: the handlers of OrderApi.Application, MediatR behind the kernel's ISender (this
// adapter is the only MediatR reference), and the always-on behaviors in their fixed order: tracing, logging,
// metrics, authorization ([RequirePermission]) and validation. The behaviors that need an infrastructure seam
// (WithIdempotency, WithTransactions, WithAuditing, WithCaching) are explicit opt-ins; a missing seam fails the host
// start, as does a [RequirePermission] use case without an IRequestContext.
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR());

// Readiness maps every IReadinessProbe the adapters registered (here: "order-store").
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

// The HTTP boundary in one call, configured from SharedKernel:Presentation:WebApi. Every error response (a failed
// Result, a thrown exception, the framework's own 404/405/415) is RFC 9457 application/problem+json with errorCode,
// traceId and correlationId; plus security headers, request limits and the authorization problem bodies.
builder.AddSharedKernelWebApi();

// API versioning and one OpenAPI document per version with a Scalar reference, configured from
// SharedKernel:Presentation:OpenApi. The documents are served in Development only; ExposeInProduction publishes them
// elsewhere, by decision rather than by default. No bearer scheme: this sample has no identity provider.
builder.AddSharedKernelOpenApi(options =>
{
    options.Title = "Orders API";
    options.Bearer = false;
});

var app = builder.Build();

// The canonical pipeline. The request context goes FIRST, so the X-Correlation-Id and the caller are in scope for
// every later middleware and on every response, error responses included; then the WebApi pipeline (security
// headers, the exception handler, routing, and authentication and authorization when they are registered).
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();

// Kubernetes liveness/readiness endpoints.
app.MapDefaultHealthCheckEndpoints();

// Every IEndpointModule of this assembly, found at compile time by the generator the WebApi package ships.
app.MapEndpoints();

// /openapi/v1.json and the Scalar reference at /scalar in Development; outside it, nothing is mapped.
app.MapSharedKernelOpenApi();

// Readiness reports 503 until the app signals it has finished starting, so Kubernetes does not route traffic
// to a pod that is still migrating or seeding. The sample has no startup work, so it signals immediately.
// Omitting this call leaves /health/ready at 503 forever.
app.Services.GetRequiredService<StartupGate>().MarkReady();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
