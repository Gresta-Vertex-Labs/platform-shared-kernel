using DocumentsApi;
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

// Readiness fails while any store's bucket is unreachable: every AddStore/AddTenantStore registered a probe
// (storage-assets, storage-documents, storage-archive), and AddSharedKernelReadiness maps them all.
builder.Services.AddHealthChecks()
    .AddSharedKernelReadiness();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultHealthCheckEndpoints();
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.MapFileEndpoints();
app.MapLinkEndpoints();

await app.RunAsync();

/// <summary>Exposed so the sample can be driven from a test host.</summary>
public partial class Program;
