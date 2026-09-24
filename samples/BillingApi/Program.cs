using BillingApi.Api;
using BillingApi.Application;
using BillingApi.Infrastructure;
using BillingApi.Security;
using SharedKernel.Application;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry, health endpoints, the startup gate.
builder.AddServiceDefaults();

// Who is calling: an authentication scheme produces IUserContext (here the development-only header scheme;
// in production AddOidcAuthentication), and AddSharedKernelRequestContext() turns it into the one IRequestContext
// that authorization, audit columns, tenant filters, row-level security and the audit ledger all read.
builder.Services.AddDemoAuthentication();
builder.Services.AddSharedKernelRequestContext();

// 01.Core cryptography: the audit ledger's HMAC signer. The envelope provider wraps the per-tenant data keys of
// field encryption — a local master key here, Azure Key Vault in production.
builder.Services.AddSharedKernelCryptography(builder.Configuration);
builder.Services.AddOptions<LocalMasterKeyOptions>()
    .Bind(builder.Configuration.GetSection(LocalMasterKeyOptions.SectionName))
    .Validate(o => o.Material.Length > 0, $"{LocalMasterKeyOptions.SectionName}:Material is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<IEnvelopeEncryptionProvider, LocalMasterKeyEnvelopeProvider>();

// The service's root key provider. 06.Persistence seals every entity version (the ETag) with a subkey it derives from
// it, so an ETag never shows PostgreSQL's xmin. Here: the root keys field encryption reads — every capability derives
// its own subkey, so sharing the root key is safe. In production, a KMS: 13.ServiceDefaults' AddSharedKernelKeyVaultKeyProvider().
builder.Services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp =>
{
    var keys = sp.GetRequiredService<IOptions<EncryptionOptions>>().Value.Keys;
    return new StaticEncryptionKeyProvider(
        keys.CurrentKeyId!, keys.Keys.Select(key => new CryptographicKey(key.Key, Convert.FromBase64String(key.Value))));
});

// 06.Persistence — the whole stack in one registration. Reads ConnectionStrings:billing and
// SharedKernel:Persistence:billing (migration role, cross-tenant role, row-level security settings).
// The capabilities live in BillingDatabase.Configure, shared with the design-time factory that `dotnet ef` uses.
builder.AddSharedKernelPostgres<BillingDbContext>(BillingDatabase.ConnectionName, p => BillingDatabase.Configure(p)
    .UseServiceName("billing-api")
    .MigrateOnStartup()                                               // as app_migrator, under an advisory lock
    .AddSeeder<TaxRateSeeder>());

// Hand-written SQL over the same data source, joining the same unit of work.
builder.Services.AddSharedKernelDapper(builder.Configuration);

// The audit sealer writes chain links as its own role (app_audit_sealer), so the application role cannot forge them.
builder.Services.AddSharedKernelNpgsql(builder.Configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer");

// 05.Application — MediatR with the platform pipeline, in one call: the handlers and validators of this assembly,
// [RequirePermission] on every command and query, one retry-safe transaction per command, and an audit record —
// Succeeded inside the transaction, Failed after a rollback.
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization()
    .WithTransactions()
    .WithAuditing());

builder.Services.AddScoped<ICustomerDirectory, CustomerDirectory>();

// Readiness: not ready until migrations and seeders finished, the key ring is loaded and the sealer keeps up.
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<BillingDbContext>()
    .AddFieldEncryptionReadinessCheck()
    .AddAuditSealingReadinessCheck();
builder.Services.AddHostedService<StartupGateRelease>();

// 14.Presentation — the HTTP boundary in one call (SharedKernel:Presentation:WebApi): every error — a failed Result,
// an exception that escapes a handler, a rejected caller — is an RFC 9457 problem; authorization policies over
// IUserContext for RequirePermission(); correlation ids, security headers and request limits.
builder.AddSharedKernelWebApi();

var app = builder.Build();

// Before any endpoint: correlation id, security headers, the exception handler, routing, authentication (the demo
// scheme above) and authorization, in that order.
app.UseSharedKernelWebApi();

app.MapDefaultHealthCheckEndpoints();
app.MapBillingEndpoints();

await app.RunAsync();

/// <summary>Exposed so the tests can host the service with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
