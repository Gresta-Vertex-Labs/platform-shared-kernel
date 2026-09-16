using FluentValidation;
using MediatR;
using OrderApi.Application;
using OrderApi.Infrastructure;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Extensions;
using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Primitives.Clocks;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry (traces/metrics/logs) plus base health-check wiring.
builder.AddServiceDefaults();

// 01.Core — IClock is the only sanctioned time source; analyzer SK0001 forbids DateTime.UtcNow.
builder.Services.AddSingleton<IClock, SystemClock>();

// 05.Application — MediatR handler discovery, the domain-event dispatcher, and the
// zero-prerequisite behavior preset (tracing, logging, metrics, validation). Build() registers
// them in the fixed pipeline order; nothing is registered without it. The behaviors that need
// an infrastructure seam (authorization over IRequestContext, idempotency over
// IRequestIdempotencyStore, transaction over IUnitOfWork, auditing over IAuditTrailWriter, and
// caching from SharedKernel.Application.Behaviors.Caching) are deliberately not in the preset —
// each is an explicit opt-in, and Build() throws if its seam is not registered.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PlaceOrderCommand).Assembly));
builder.Services.AddSharedKernelApplication();
builder.Services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderCommandValidator>();
builder.Services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

// 14.Presentation — RFC 9457 ProblemDetails for unhandled exceptions.
builder.Services.AddProblemDetails();

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

var app = builder.Build();

app.UseExceptionHandler();

// K8s liveness/readiness endpoints from 13.ServiceDefaults.
app.MapDefaultHealthCheckEndpoints();

// Readiness deliberately reports 503 until the app signals it has finished starting, so
// Kubernetes does not route traffic to a pod that is still migrating or seeding. A real
// service calls MarkReady() after that work completes; the sample has none, so it signals
// immediately. Omitting this call leaves /health/ready at 503 forever.
app.Services.GetRequiredService<StartupGate>().MarkReady();

// Result<T> -> HTTP is a single call. The endpoint never inspects IsSuccess and never
// chooses a status code: Error.Validation becomes 400, Error.NotFound becomes 404, and
// the body is RFC 9457 ProblemDetails in every failure case.
app.MapPost("/orders", async (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(command, ct);
    return result.ToProblemDetailsResult(id => Results.Created($"/orders/{id}", new { id }));
});

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new GetOrderQuery(id), ct);
    return result.ToProblemDetailsResult();
});

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
