using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Security.Mtls;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;
using Shop.Inventory.Api;
using Shop.Inventory.Api.Reconciliation;
using Shop.Inventory.Api.Security;
using Shop.Inventory.Api.Stock;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WithApplicationTelemetry();
builder.WithPersistenceTelemetry();
builder.WithCachingTelemetry();
builder.WithSchedulingTelemetry();

// Two kinds of caller. Merchants with a Keycloak token (12.Security.Oidc); the Ordering service with a client
// certificate issued by the Shop's CA (12.Security.Mtls), asked for during the TLS handshake but not required, so
// REST callers and health probes connect without one. The default scheme forwards on the certificate, so a service
// caller is known before its tenant is resolved.
const string CallerScheme = "ShopCaller";
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddMtlsAuthentication<ShopServiceCertificateValidator>(mtls =>
{
    mtls.ChainTrustValidationMode = System
        .Security
        .Cryptography
        .X509Certificates
        .X509ChainTrustMode
        .CustomRootTrust;
    mtls.CustomTrustStore.AddRange(ShopCertificateAuthority.Load(builder.Configuration));
    mtls.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
});
builder
    .Services.AddAuthentication(CallerScheme)
    .AddPolicyScheme(
        CallerScheme,
        "Bearer token or client certificate",
        policy =>
            policy.ForwardDefaultSelector = context =>
                context.Connection.ClientCertificate is null
                    ? "Bearer"
                    : MtlsAuthenticationDefaults.AuthenticationScheme
    );
builder.AddMtlsClientCertificate(ClientCertificateMode.AllowCertificate);
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        "ShopService",
        policy =>
            policy
                .AddAuthenticationSchemes(MtlsAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
    );

// The caller's tenant: the signed claim for merchants, the X-Tenant-Id header only for certificate callers.
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o)
);
builder.Services.AddScoped<ITenantResolutionStrategy, ServiceHeaderTenantResolutionStrategy>();

// 06.Persistence without EF Core: one data source (and its migration and cross-tenant siblings), Dapper sessions that
// bind the caller's tenant for row-level security, and the schema bootstrap — registered first, so it runs first.
builder.Services.AddHostedService<InventorySchema>();
builder.Services.AddSharedKernelNpgsql(builder.Configuration, InventorySchema.ConnectionName);
builder.Services.AddSharedKernelDapper(builder.Configuration);

// 02.Caching: the shared Redis connection, stock levels in hashes, one lock per SKU.
builder
    .Services.AddRedisConnection(builder.Configuration)
    .AddTypedHashStore(InventoryJsonContext.Default.StockLevel)
    .AddRedisDistributedLocking();
builder.Services.AddSingleton<StockLevelCache>();
builder.Services.AddSingleton<SkuLocks>();

// 19.Scheduling: reconciliation every two seconds on every replica, run once per occurrence across them.
builder.Services.AddSingleton<ReplicaIdentity>();
InventoryJobs.Register(
    builder.Services.AddSharedKernelScheduling(),
    builder.Configuration["Inventory:Reconciliation:Cron"]
);

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.Services.AddHealthChecks().AddSharedKernelReadiness();

builder.AddSharedKernelWebApi();
builder.AddSharedKernelGrpc(options => options.ErrorDomain = "inventory.shop.example");

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())
);

app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();
app.MapGrpcService<InventoryGrpcService>().RequireAuthorization("ShopService");

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
