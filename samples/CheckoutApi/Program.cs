using CheckoutApi.Clients;
using InventoryApi.Grpc;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Communication;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry (with outbound HTTP/gRPC spans and resilience metrics) and health endpoints.
builder.AddServiceDefaults();
builder.WithCommunicationTelemetry();
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

// 11.Communication — two clients of InventoryApi, everything else in configuration (appsettings.json):
//   inventory       REST, http://inventory       — Idempotency-Key on POSTs, so the reservation is retried safely
//   inventory-grpc  gRPC, http://_grpc.inventory — the service's endpoint named "grpc"
// Both hosts resolve through the Services section (service discovery), and both send InventoryApi's API key.
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory")
    .AddGrpcClient<Inventory.InventoryClient>("inventory-grpc");

// The request context: the sample has no authentication, so its callers are anonymous; their correlation id is
// what the outbound calls carry.
builder.Services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);
builder.Services.AddSharedKernelRequestContext();

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.AddSharedKernelWebApi();

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();

app.MapDefaultHealthCheckEndpoints();
app.Services.GetRequiredService<StartupGate>().MarkReady();
app.MapEndpoints();

await app.RunAsync();
