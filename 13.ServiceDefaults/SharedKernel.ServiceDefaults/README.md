# SharedKernel.ServiceDefaults

The composition base every Platform.SharedKernel microservice starts from: one call wires OpenTelemetry,
the startup readiness gate, and the health-check endpoints.

**It references no other SharedKernel package.** Dependency-specific readiness checks, mutual TLS, Azure
Key Vault, and localization each live in their own `SharedKernel.ServiceDefaults.*` integration package,
so a service restores only the integrations it actually uses.

## Rules

| Rule | Why |
| --- | --- |
| Call `builder.AddServiceDefaults()` first in `Program.cs` | It wires telemetry and the base health-check infrastructure every later call builds on. |
| Chain readiness checks onto `builder.Services.AddHealthChecks()` | **Not** `AddSharedKernelHealthChecks()`: `AddServiceDefaults()` already calls it, and a second call registers the `"startup"` check twice — the application then throws `ArgumentException: Duplicate health checks were registered with the name(s): startup` at startup. |
| Add an integration package only for a dependency the service really has | That is the point of the split: each package brings its dependency's client libraries with it. |
| Keep `/health/ready` off public ingress, or pass `requireAuthorization: true` | Readiness output can disclose your dependency topology. See below. |
| Never add a `ProjectReference` to this package | Every service restores whatever this package references. Two tests lock it — see "Why the base references nothing". |

## Which package do I add?

| Your service uses | Add | Gives you |
| --- | --- | --- |
| EF Core or a Dapper connection factory | `SharedKernel.ServiceDefaults.Persistence` | `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()` |
| A cache through `ICacheService` | `SharedKernel.ServiceDefaults.Caching` | `AddCacheReadinessCheck()` |
| Redis | `SharedKernel.ServiceDefaults.Caching.Redis` | `AddRedisHealthCheck(connectionString)` |
| A message bus | `SharedKernel.ServiceDefaults.Messaging` | `AddMessagingReadinessCheck()` |
| Object storage (S3, MinIO, OBS) | `SharedKernel.ServiceDefaults.Storage` | `AddStorageReadinessCheck(bucket)` |
| A search index (Meilisearch, Elasticsearch) | `SharedKernel.ServiceDefaults.Search` | `AddSearchReadinessCheck(indexName)` |
| A vector store | `SharedKernel.ServiceDefaults.AI` | `AddVectorStoreReadinessCheck(collectionName)` |
| Temporal workflows | `SharedKernel.ServiceDefaults.Workflows.Temporal` | `AddWorkflowReadinessCheck()` |
| Scheduled jobs | `SharedKernel.ServiceDefaults.Scheduling` | `AddSchedulerReadinessCheck()` |
| Mutual TLS | `SharedKernel.ServiceDefaults.Security.Mtls` | `AddMtlsClientCertificate()`, `AddMtlsForwardedHeaderCertificate()` |
| Key Vault **keys** as the encryption-key provider | `SharedKernel.ServiceDefaults.Cryptography.KeyVault` | `AddSharedKernelKeyVaultKeyProvider()`, `AddKeyVaultKeyProviderReadinessCheck()` |
| Key Vault **secrets** as configuration | `SharedKernel.ServiceDefaults.Configuration.KeyVault` | `AddSharedKernelKeyVaultConfiguration(vaultUri)` |
| Per-request culture resolution | `SharedKernel.ServiceDefaults.Localization` | `AddSharedKernelLocalization()` |

Each integration package's README covers its behaviour, tags, and failure status.

## Quick start

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Messaging" />
```

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults()
       .WithMessagingTelemetry();

builder.Services.AddHealthChecks()
       .AddMessagingReadinessCheck();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();

// Once start-up work such as migrations is done, open /health/ready:
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.Run();
```

## What is in this package

| Member | Purpose |
| --- | --- |
| `AddServiceDefaults()` | OpenTelemetry traces, metrics, and logs — with ambient `TenantId`/`CorrelationId` log enrichment — plus the base health checks |
| `MapDefaultHealthCheckEndpoints(requireAuthorization)` | Maps `/health/live` (`live`-tagged checks only) and `/health/ready` (`ready`-tagged checks only) |
| `StartupGate` | Keeps `/health/ready` unhealthy until you call `MarkReady()` |
| `HealthCheckNames`, `HealthCheckTags` | The shared names and tags every integration package uses |
| `HealthCheckRegistrationLogging` | Logs a readiness check's registration (EventId `13002`) — use it in a check of your own |
| `AddSharedKernelRateLimiting()` | ASP.NET Core rate limiting with conservative defaults |
| `WithApplicationTelemetry()` · `WithCachingTelemetry()` · `WithCommunicationTelemetry()` · `WithIntegrationTelemetry()` · `WithIntelligenceTelemetry()` · `WithMessagingTelemetry()` · `WithPersistenceTelemetry()` · `WithSchedulingTelemetry()` · `WithSearchTelemetry()` · `WithWorkflowTelemetry()` | Registers a domain's `ActivitySource` and `Meter` with the host |

Every `WithXTelemetry()` lives here rather than in an integration package because each wires its domain's
instruments **by name** and references nothing — so none of them adds a dependency.

`WithApplicationTelemetry()` additionally registers an explicit bucket view for
`sharedkernel.application.request.duration`, which `05.Application` records **in seconds**: the SDK's
default buckets assume milliseconds, so without the view every request would land in the first bucket.
Calling it twice exports one metric stream, not two. A dashboard or alert built against the earlier
millisecond values needs retuning.

## Migrating from before the split

Before WO-084 this package contained every integration. The types have not moved namespace — only
package — so migrating means adding a `PackageReference`, never editing source. If your build reports an
unknown `AddXReadinessCheck`, `AddMtls…`, `AddSharedKernelKeyVault…`, or `AddSharedKernelLocalization`, add
the package from the table above.

## Security note — health endpoints

`MapDefaultHealthCheckEndpoints()` maps unauthenticated endpoints by default, because Kubernetes probes
cannot present credentials, and readiness output can disclose your dependency topology. Pass
`requireAuthorization: true`, or keep these endpoints off public ingress and restrict them with a
`NetworkPolicy`. Never expose readiness on an internet-facing service.

## Rate limiting and ProblemDetails

`AddSharedKernelRateLimiting()` leaves `OnRejected` at the ASP.NET Core bare-429 default and takes **no**
reference to `14.Presentation`. For an RFC 9457 body, attach your own handler through the `configure`
parameter and call `14.Presentation`'s `RateLimitRejectionProblemDetails.Create(...)`. That keeps the two
packages independently referenceable.

## Why the base references nothing

Before WO-084, a project referencing this package alone restored **25 SharedKernel projects and 73 NuGet
packages** — MassTransit, Azure Service Bus, Microsoft.Identity.Web, EF Core, Temporalio, Quartz, and
StackExchange.Redis among them. Two of its fourteen references were used by no code at all. Measured the
same way afterwards: **1 project and 10 packages, all OpenTelemetry.**

`SharedKernel.ServiceDefaults.Tests` locks that in two layers. One test reads the compiled assembly's
references, catching integration code that creeps back in. The other reads this project file, catching a
reference nothing uses — which leaves no trace in the assembly yet still lands in every consumer's restore.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[13.ServiceDefaults README](../README.md) for the whole host-composition layer, including
`SharedKernel.MultiTenancy`.
