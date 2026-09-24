using OrderApi.Api;
using OrderApi.Application;
using OrderApi.Infrastructure;
using SharedKernel.Application;
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry (traces/metrics/logs) plus base health-check wiring.
builder.AddServiceDefaults();

// 01.Core — IClock is the only sanctioned time source; analyzer SK0001 forbids DateTime.UtcNow.
builder.Services.AddSingleton<IClock, SystemClock>();

// 05.Application — one call: MediatR with the handlers and FluentValidation validators of this assembly, the
// domain-event dispatcher, and the always-on behaviors (tracing, logging, metrics, validation) in the fixed pipeline
// order. The behaviors that need an infrastructure seam (WithAuthorization over IRequestContext, WithIdempotency over
// IRequestIdempotencyStore and IRequestContext, WithTransactions over IUnitOfWork, WithAuditing over IAuditTrailWriter,
// WithCaching from SharedKernel.Application.Caching) are explicit opt-ins; a missing seam fails the host start. This
// API authenticates nobody, so it opts into none of them.
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly);

// 14.Presentation — the HTTP boundary in one call, configured from SharedKernel:Presentation:WebApi. Every error
// response — a failed Result, a thrown exception, the framework's own 404/405/415 — is RFC 9457
// application/problem+json with errorCode, traceId and correlationId; plus correlation ids, security headers,
// request limits and authorization.
builder.AddSharedKernelWebApi();

// API versioning and one OpenAPI document per API version with a Scalar reference, configured from
// SharedKernel:Presentation:OpenApi. The documents are served in Development only; ExposeInProduction publishes
// them elsewhere, by decision rather than by default. This API authenticates nobody, so it declares no bearer
// scheme; a protected operation would document its security requirement and 401/403 by itself.
builder.AddSharedKernelOpenApi(options =>
{
    options.Title = "Orders API";
    options.Bearer = false;
});

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

var app = builder.Build();

// First, before any endpoint: correlation id, security headers, the exception handler, problem bodies for bodiless
// error statuses, routing, authentication and authorization — in the order they must run.
app.UseSharedKernelWebApi();

// K8s liveness/readiness endpoints from 13.ServiceDefaults.
app.MapDefaultHealthCheckEndpoints();

// Readiness deliberately reports 503 until the app signals it has finished starting, so
// Kubernetes does not route traffic to a pod that is still migrating or seeding. A real
// service calls MarkReady() after that work completes; the sample has none, so it signals
// immediately. Omitting this call leaves /health/ready at 503 forever.
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.MapOrderEndpoints();

// /openapi/v1.json and the Scalar reference at /scalar in Development; outside it, nothing is mapped.
app.MapSharedKernelOpenApi();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
