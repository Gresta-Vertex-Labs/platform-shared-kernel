# SharedKernel.ServiceDefaults

Host composition layer for Platform.SharedKernel microservices. One call wires OpenTelemetry, health checks, and the platform's startup conventions; everything beyond that is opt-in.

This is the composition root — the only package permitted to reference concrete providers from lower layers in order to assemble them.

## Included

### Entry point

**`AddServiceDefaults()`** — OpenTelemetry (traces, metrics, logs with ambient TenantId/CorrelationId enrichment) plus base health-check wiring.

**`MapDefaultHealthCheckEndpoints()`** — maps the liveness and readiness endpoints. Takes a `requireAuthorization` parameter; see the security note below.

### Readiness probes — opt in per dependency

Each wraps the probe primitive owned by that capability domain. `13.ServiceDefaults` supplies the `IHealthCheck` wiring; the domains supply the probes.

| Method | Probes |
|---|---|
| `AddDatabaseReadinessCheck<TContext>()` | EF Core connectivity |
| `AddDapperDatabaseReadinessCheck()` | `IDbConnectionFactory` connectivity |
| `AddCacheReadinessCheck()` / `AddRedisHealthCheck()` | Cache / Redis |
| `AddMessagingReadinessCheck()` | Message bus, against the real configured bus |
| `AddStorageReadinessCheck()` | Object storage |
| `AddSearchReadinessCheck()` | Search index |
| `AddVectorStoreReadinessCheck()` | Vector collection |
| `AddWorkflowReadinessCheck()` | Temporal workflow service |

### Telemetry activation — opt in per domain

`WithApplicationTelemetry` · `WithCachingTelemetry` · `WithCommunicationTelemetry` · `WithIntegrationTelemetry` · `WithIntelligenceTelemetry` · `WithMessagingTelemetry` · `WithPersistenceTelemetry` · `WithSearchTelemetry` · `WithWorkflowTelemetry`

Each registers that domain's `ActivitySource` and/or `Meter` with the host providers.

### Other opt-ins

| Method | Purpose |
|---|---|
| `AddSharedKernelRateLimiting()` | BCL `Microsoft.AspNetCore.RateLimiting` with conservative defaults |
| `AddSharedKernelKeyVaultConfiguration()` | Azure Key Vault as an `IConfiguration` source |
| `AddMtlsClientCertificate()` | Kestrel client-certificate negotiation |
| `AddMtlsForwardedHeaderCertificate()` | Forwarded mTLS certificate header, restricted to trusted networks |

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults()
       .WithPersistenceTelemetry()
       .WithMessagingTelemetry();

builder.Services.AddSharedKernelHealthChecks()
       .AddDatabaseReadinessCheck<AppDbContext>()
       .AddMessagingReadinessCheck();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
app.Run();
```

## Security note — health endpoints

`MapDefaultHealthCheckEndpoints()` maps unauthenticated endpoints by default, because Kubernetes probes cannot present credentials. Readiness output can disclose dependency topology.

Pass `requireAuthorization: true`, or keep these endpoints off your public ingress and restrict them with a `NetworkPolicy`. Do not expose readiness publicly on an internet-facing service.

## Rate limiting and ProblemDetails

`AddSharedKernelRateLimiting()` leaves `OnRejected` at the BCL bare-429 default and takes **no** reference to `14.Presentation`. A service that wants an RFC 9457 body attaches its own handler via the `configure` parameter and calls `14.Presentation`'s `RateLimitRejectionProblemDetails.Create(...)`. That keeps the two packages independently referenceable.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [13.ServiceDefaults README](../README.md) for the full host-composition layer, including `SharedKernel.MultiTenancy`.
