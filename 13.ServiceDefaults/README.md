# 13.ServiceDefaults

Host composition layer for Platform.SharedKernel microservices.

- **`SharedKernel.ServiceDefaults`** — `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring; health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (database, Redis, cache, RabbitMQ, Azure Service Bus); startup-probe gating.
- **`SharedKernel.MultiTenancy`** — concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, database tenant-directory lookup); `TenantResolutionMiddleware`; `AmbientTenantProvider`.

Both packages are composition-only: they wire abstractions and concrete providers from layers `01`–`12` together. No business logic, no domain types, no new abstractions are defined here.

## Program.cs composition

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                          // OTel + base health endpoints — FIRST call
builder.Services.AddSharedKernelMultiTenancy();        // optional — multi-tenant services only

// Opt-in dependency-specific health checks — only what this service actually uses:
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<MyDbContext>()
    .AddRedisHealthCheck(redisConnectionString)
    .AddRabbitMqMessagingHealthCheck(amqpUri)
    .AddStorageReadinessCheck("my-bucket")
    .AddSearchReadinessCheck("products-index");

builder.WithMessagingTelemetry();                      // optional — services using 07.Messaging
builder.WithCachingTelemetry();                        // optional — services using 02.Caching
builder.WithApplicationTelemetry();                    // optional — services using 05.Application's pipeline behaviors
builder.WithSearchTelemetry();                         // optional — services using 09.Search

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()

app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

### Ordering rules

1. `builder.AddServiceDefaults()` must be the **first** call in `Program.cs`, before any other `SharedKernel.*.Add...` extension. It wires OpenTelemetry and registers only the base health check infrastructure (the always-on `StartupGateHealthCheck` plus the `/health/live` and `/health/ready` endpoint mappings) — it never registers a dependency-specific check.
2. `AddSharedKernelMultiTenancy()` is optional and only needed by multi-tenant services. It registers `TenantResolutionOptions`, `AmbientTenantProvider` (scoped `ITenantProvider`), and the three platform `ITenantResolutionStrategy` implementations — but **not** the middleware itself.
3. Every dependency-specific health check (`AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, `AddStorageReadinessCheck`, `AddSearchReadinessCheck`) is an explicit opt-in call on the `IHealthChecksBuilder` returned by `services.AddHealthChecks()`. A service only registers the checks for dependencies it actually uses. `AddStorageReadinessCheck(bucket)` requires `bucket`, and `AddSearchReadinessCheck(indexName)` requires `indexName`, as explicit arguments — both are deliberately never defaulted from a provider's own options type (`SharedKernel.Storage.S3`/`.Obs`'s `DefaultBucket`, or `SharedKernel.Search.Meilisearch`/`.ElasticSearch`'s index configuration), since that would reintroduce the provider-specific coupling these methods exist to avoid. Each resolves only its neutral abstraction (`IFileStorage`, `ISearchIndexProvisioner`) from DI and works uniformly against whichever provider is registered. `AddSearchReadinessCheck` reports `Unhealthy` — never `Degraded` — unless the probe's `Reachable`, `IndexAddressable`, and `Searchable` all report `true`; a deep `PendingWriteCount` write backlog is surfaced only as informational `HealthCheckResult.Data` and never fails the check, since it means results are stale, not unavailable.
4. `WithMessagingTelemetry()` / `WithCachingTelemetry()` / `WithApplicationTelemetry()` / `WithSearchTelemetry()` are optional and only wire already-existing `ActivitySource`/`Meter` instruments owned by `07.Messaging`, `02.Caching`, `05.Application.Behaviors`, and `09.Search` respectively into this host's `TracerProvider`/`MeterProvider`. All four are idempotent — calling any of them more than once registers no duplicate instrument.
5. `app.UseMiddleware<TenantResolutionMiddleware>()` is **required** whenever `AddSharedKernelMultiTenancy()` is used, and **must** be placed after `app.UseAuthentication()` — `ClaimTenantResolutionStrategy` needs a populated `HttpContext.User`. Without this call, `AmbientTenantProvider.TenantId` stays permanently `Guid.Empty` (a silent, by-design failure mode, not a crash).
6. `app.MapDefaultHealthCheckEndpoints()` maps `/health/live` (only `"live"`-tagged checks — process-alive signal only) and `/health/ready` (only `"ready"`-tagged checks — may depend on DB/cache/broker connectivity, gates load-balancer rotation, never restarts the pod).

### Automatic log export and ambient TenantId/CorrelationId enrichment

`builder.AddServiceDefaults()` (via `AddSharedKernelTelemetry`) automatically exports every
`[LoggerMessage]`-authored log record through the same OTLP pipeline as traces and metrics —
`IncludeScopes` and `IncludeFormattedMessage` are both enabled, and a `BaggageLogRecordProcessor`
copies every `System.Diagnostics.Activity` baggage entry from `Activity.Current` onto each log
record's attributes at export time. No application-code call-site changes are needed to get this.

`BaggageLogRecordProcessor` is a **generic** mechanism — it carries no hardcoded baggage key
names. This is what makes it automatically pick up:

- `14.Presentation`'s correlation-id middleware, which sets its own `Activity` baggage key directly
  against the BCL (WO-031) — with **zero** `ProjectReference` from `13.ServiceDefaults` to
  `14.Presentation`.
- `SharedKernel.MultiTenancy`'s `TenantResolutionMiddleware`, which — when
  `AddSharedKernelMultiTenancy()` is used — sets `TenantBaggageKeys.TenantId` as `Activity` baggage
  immediately after resolving (or confirming `Guid.Empty` for) the current request's tenant. The
  baggage value is set even when no tenant resolves, so log aggregation can distinguish "no tenant
  resolved for this request" from "TenantId enrichment was never wired."

Any future domain that sets its own `Activity` baggage key gets the same free ambient-log
enrichment — no `13.ServiceDefaults` change required.

**Scope boundary:** this enrichment mechanism covers the HTTP-request path only, via whatever sets
`Activity` baggage during that request. A message-consumption-scope equivalent (e.g. a MassTransit
consumer filter setting the same baggage keys from propagated message headers) is **not**
implemented here — it would be a future `07.Messaging`-owned follow-up, outside this domain's
jurisdiction to dispatch.

See `13.ServiceDefaults/CLAUDE.md` for the full interface contracts, tag taxonomy, and implementation rules.
