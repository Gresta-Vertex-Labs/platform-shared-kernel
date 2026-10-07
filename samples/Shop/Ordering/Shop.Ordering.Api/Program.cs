using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Presentation.SignalR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Security.Totp;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;
using Shop.Ordering.Api;
using Shop.Ordering.Application;
using Shop.Ordering.Infrastructure;
using Shop.Ordering.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WithApplicationTelemetry();
builder.WithPersistenceTelemetry();
builder.WithMessagingTelemetry();
builder.WithWorkflowTelemetry();
builder.WithCommunicationTelemetry();
builder.WithCachingTelemetry();

// The caller: a Keycloak token, its tenant from the signed claim (13.MultiTenancy, Claim only).
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o)
);

// Every adapter: PostgreSQL (RLS, audit ledger, field encryption), RabbitMQ with the outbox, idempotency stores,
// Temporal, the Inventory gRPC client.
builder.AddOrderingInfrastructure();

// 01.Core cryptography (registered by Infrastructure: the audit ledger's HMAC signer, Argon2id), plus authenticator
// step-up (12.Security.Totp, a Host package) with its step-up state in Redis.
builder
    .AddOrderingCryptography()
    .AddTotpStepUp<RedisTotpStepUpStore, NoRecoveryCodeStore>(o =>
        o.FreshnessWindow = TimeSpan.FromMinutes(5)
    );

builder.Services.AddSingleton<IOrderStatusNotifier, SignalROrderStatusNotifier>();

// 05.Application: idempotent submissions (Redis, request purpose), one transaction per command (the outbox writes
// join it), and an audit record for every auditable command.
builder.Services.AddSharedKernelApplication(
    typeof(PlaceOrderCommand).Assembly,
    app => app.UseMediatR().WithIdempotency().WithTransactions().WithAuditing()
);

builder
    .Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<OrderingDbContext>()
    .AddSharedKernelReadiness();

builder.AddSharedKernelWebApi();
builder.AddSharedKernelSignalR();

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())
);

app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();
app.MapHub<OrdersHub>(OrdersHub.Path);

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
