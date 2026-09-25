using BillingApi.Api;
using BillingApi.Application;
using BillingApi.Infrastructure;
using BillingApi.Security;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Extensions;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Persistence;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
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

// 05.Application — MediatR with the platform pipeline. TransactionBehavior runs every command in one retry-safe
// transaction; AuditingBehavior records Succeeded inside it and Failed after a rollback.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddSharedKernelApplication();
builder.Services.AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()
    .AddAuthorizationBehavior()
    .AddTransactionBehavior()
    .AddAuditingBehavior()
    .Build();

builder.Services.AddScoped<ICustomerDirectory, CustomerDirectory>();

// Readiness: not ready until migrations and seeders finished, the key ring is loaded and the sealer keeps up.
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<BillingDbContext>()
    .AddSharedKernelReadiness(); // every probe the providers registered: field-encryption keys, audit sealing, ...
builder.Services.AddHostedService<StartupGateRelease>();

// 14.Presentation — RFC 9457 ProblemDetails for everything that escapes a handler (e.g. a concurrency conflict).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

var app = builder.Build();

// First in the pipeline: the request's X-Correlation-Id and its request context, so every log line, audit record
// and outbound call of the request carries one correlation id and one caller.
app.UseSharedKernelRequestContext();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultHealthCheckEndpoints();
app.MapBillingEndpoints();

await app.RunAsync();

/// <summary>Exposed so the tests can host the service with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
