using OrderApi.Api;
using OrderApi.Application;
using OrderApi.Infrastructure;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Middleware;
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

// Who is calling. The sample has no authentication, so every caller is anonymous; a real service registers
// AddOidcAuthentication(configuration) (SharedKernel.Security.Oidc) instead and nothing below changes.
// AddSharedKernelRequestContext() turns the caller into the one IRequestContext every behavior and adapter reads.
builder.Services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);
builder.Services.AddSharedKernelRequestContext();

// The service's own layers. Each project registers what it owns; the Api only calls them.
builder.Services.AddOrderApplication();      // validators
builder.Services.AddOrderInfrastructure();   // repository, its readiness probe, the FluentValidation bridge

// The application pipeline: MediatR behind the kernel's ISender (the only MediatR reference is this adapter),
// and the zero-prerequisite preset — tracing, logging, metrics, validation. Build() registers them in the fixed
// pipeline order. The behaviors that need a seam (authorization over IRequestContext, idempotency over
// IIdempotencyStore, transaction over IUnitOfWork, auditing over IAuditTrailWriter, caching) are explicit
// opt-ins, and Build() throws if the seam is missing.
builder.Services.AddSharedKernelMediatR(typeof(PlaceOrderCommand).Assembly);
builder.Services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

// Readiness maps every IReadinessProbe the adapters registered (here: "order-store").
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

// RFC 9457 ProblemDetails for everything that escapes a handler.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

var app = builder.Build();

// The canonical middleware order. The request context goes FIRST, so the X-Correlation-Id and the caller are in
// scope for every later middleware and on every response, error responses included.
app.UseSharedKernelRequestContext();
app.UseSharedKernelSecurityHeaders();
app.UseExceptionHandler();
// app.UseAuthentication();   // a service with authentication adds these two here,
// app.UseAuthorization();    // after the exception handler and before the endpoints.

// Kubernetes liveness/readiness endpoints.
app.MapDefaultHealthCheckEndpoints();
app.MapOrderEndpoints();

// Readiness reports 503 until the app signals it has finished starting, so Kubernetes does not route traffic
// to a pod that is still migrating or seeding. The sample has no startup work, so it signals immediately.
// Omitting this call leaves /health/ready at 503 forever.
app.Services.GetRequiredService<StartupGate>().MarkReady();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
