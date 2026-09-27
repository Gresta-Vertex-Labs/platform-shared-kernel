using InventoryApi;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry and health endpoints.
builder.AddServiceDefaults();
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

// 12.Security — every caller presents an API key (X-Api-Key); the key is Inventory:ApiKey, from a secret store.
builder.Services.AddApiKeyAuthentication<ConfiguredApiKeyValidator>();
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();

// 05.Application — the queries and commands of Features/, behind the kernel's ISender.
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.Services.AddSingleton<InventoryStore>();

// 14.Presentation — errors become RFC 9457 problems over REST and rich statuses over gRPC, with the same codes.
builder.AddSharedKernelWebApi();
builder.AddSharedKernelGrpc();

var app = builder.Build();

// The caller's correlation id, tenant and actor — sent by CheckoutApi's clients — become this request's context.
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();

app.MapDefaultHealthCheckEndpoints();
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.MapEndpoints();
app.MapGrpcService<InventoryGrpcService>().RequireAuthorization();

await app.RunAsync();
