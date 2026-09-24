using SharedKernel.Application.Context;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;
using ShippingApi;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry and health endpoints; messaging spans and metrics.
builder.AddServiceDefaults();
builder.WithMessagingTelemetry();

// 01.Core — IClock is the only sanctioned time source; analyzer SK0001 forbids DateTime.UtcNow.
builder.Services.AddSingleton<IClock, SystemClock>();

// The sample's own state and its stand-in for an identity provider. The request context is
// registered BEFORE the bus: WithInboundRequestContext() shadows whatever IRequestContext is
// already registered, and the container resolves the last one registered.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ShipmentProjection>();
builder.Services.AddSingleton<FaultLog>();
builder.Services.AddScoped<IRequestContext, HeaderRequestContext>();

// The idempotency store WithIdempotency() requires. In this process only — see the type's remarks.
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

// 07.Messaging — one chain from configuration to a running bus.
//   ServiceName comes from SharedKernel:Messaging, and prefixes every queue this service declares.
//   The connection string is ConnectionStrings:rabbitmq, as any other resource would be.
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")
        ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not configured."))
    // Retries happen in-process before the message is faulted; the fault consumer sees what is left.
    .WithRetry(r =>
    {
        r.Attempts = 2;
        r.InitialInterval = TimeSpan.FromMilliseconds(200);
    })
    // Deferral is the broker's, through the delayed-message exchange — it survives this process
    // restarting, which an in-process timer would not.
    .WithDelayedDelivery()
    // At-most-once consumption per MessageId.
    .WithIdempotency()
    // The publisher's tenant and actor travel with the message and are rebuilt on the consumer.
    .WithInboundRequestContext()
    // Correlation id flows from the ambient Activity with no per-publish code.
    .WithAmbientCorrelationPropagation()
    .AddConsumer<ShipmentDispatchedConsumer>()
    .AddConsumer<HoldShipmentConsumer>()
    .AddConsumer<ChaseShipmentConsumer>()
    .AddConsumer<FailingShipmentCheckConsumer>()
    .AddFaultConsumer<FailingShipmentCheck, ShipmentCheckFaultConsumer>()
    // Commands are sent to a named queue rather than broadcast: exactly one consumer holds a shipment.
    .WithSendEndpointRoute<HoldShipment>(Queues.Hold)
    .Build();

// Readiness fails while the bus is not connected, so a replica is not sent traffic it cannot serve.
builder.Services.AddHealthChecks().AddMessagingReadinessCheck();

// 14.Presentation — the HTTP boundary in one call (SharedKernel:Presentation:WebApi): every failed Result and every
// exception becomes an RFC 9457 problem (messaging.unavailable a 503); correlation ids, security headers, limits.
builder.AddSharedKernelWebApi();

var app = builder.Build();

// Before any endpoint: correlation id, security headers, the exception handler, problem bodies and routing.
app.UseSharedKernelWebApi();

app.MapDefaultHealthCheckEndpoints();
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.MapShipmentEndpoints();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
