using DocumentsApi;
using SharedKernel.Application;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.Storage;

var builder = WebApplication.CreateBuilder(args);

// 13.ServiceDefaults — OpenTelemetry and health endpoints; storage spans and metrics.
builder.AddServiceDefaults();
builder.WithStorageTelemetry();

// 08.Storage — three stores on three connections. Buckets, prefixes, encryption and credentials are
// configuration (SharedKernel:Storage:*); code only names the stores.
//   assets    — shared, S3 connection "Public"  (its own IAM user)
//   documents — tenant-scoped, S3 connection "Private" (another IAM user)
//   archive   — shared, Huawei Cloud OBS
IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration, "Public").AddStore(Stores.Assets);
storage.AddS3(builder.Configuration, "Private").AddTenantStore(Stores.Documents);
storage.AddObs(builder.Configuration).AddStore(Stores.Archive);

// Readiness fails while any store's bucket is unreachable.
builder.Services.AddHealthChecks()
    .AddStorageReadinessCheck(Stores.Assets, "storage-assets")
    .AddStorageReadinessCheck(Stores.Documents, "storage-documents")
    .AddStorageReadinessCheck(Stores.Archive, "storage-archive");

// 05.Application — MediatR with the handlers of this assembly (Features/) and the always-on behaviors (tracing,
// logging, metrics, validation). The endpoints send commands and queries; only the handlers touch the stores.
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly);

// 14.Presentation — the HTTP boundary in one call (SharedKernel:Presentation:WebApi). Every storage.* failure becomes
// an RFC 9457 problem with its code — an outage (storage.unavailable) a 503 — and request bodies are capped at 4 MiB
// except where an endpoint lifts the limit for itself (the upload endpoint, see FileEndpoints.MaxUploadBytes).
builder.AddSharedKernelWebApi();

var app = builder.Build();

// Before any endpoint: correlation id, security headers, the exception handler, problem bodies and routing.
app.UseSharedKernelWebApi();

app.MapDefaultHealthCheckEndpoints();
app.Services.GetRequiredService<StartupGate>().MarkReady();

// Every IEndpointModule of this assembly (FileEndpoints, LinkEndpoints), found at compile time by the generator the
// WebApi package ships.
app.MapEndpoints();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
