# SharedKernel.ServiceDefaults

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **The composition base every service starts from: one call wires OpenTelemetry, a startup gate and the health-check
> infrastructure; one more maps every dependency's readiness probe onto `/health/ready`.**

| You get | So that |
| --- | --- |
| `builder.AddServiceDefaults()` | Traces, metrics and logs over OTLP, plus the `startup` readiness check, in one call |
| `WithXTelemetry()` for each kernel domain | A domain's `ActivitySource` and `Meter` are exported only when the service uses it — no extra dependency |
| `AddSharedKernelReadiness()` | Every `IReadinessProbe` a provider registered becomes a `ready` check, with no per-dependency wiring |
| `MapDefaultHealthCheckEndpoints()` | `/health/live` and `/health/ready` for Kubernetes probes |
| `StartupGate` | Traffic waits until migrations and warm-up finish |
| Refused inbound baggage + two-key log enrichment | A caller cannot inject `TenantId` or anything else into your logs or downstream calls |
| `AddSharedKernelRateLimiting()` | A per-IP global limiter and an `authentication` policy with conservative defaults |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | `SharedKernel.Primitives` only (kernel side), ASP.NET Core, OpenTelemetry |
| Namespaces | `SharedKernel.ServiceDefaults.Extensions`, `.HealthChecks`, `.Probes`, `.Telemetry`, `.RateLimiting` |

## Quick start

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                       // first: OpenTelemetry + the "startup" check
builder.WithMessagingTelemetry();                   // only the domains this service uses

builder.Services.AddHealthChecks()
    .AddSharedKernelReadiness();                    // one "ready" check per registered IReadinessProbe

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();               // /health/live, /health/ready

// Once start-up work such as migrations is done, open /health/ready:
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.Run();
```

The OTLP exporter reads the standard `OTEL_EXPORTER_OTLP_*` environment variables; the service name is the entry
assembly's name.

## How it works

- **Telemetry.** `AddServiceDefaults()` registers tracing (ASP.NET Core, HttpClient, EF Core), metrics (ASP.NET Core,
  runtime) and logging (scopes and formatted messages), all exported over OTLP. Every `WithXTelemetry()` adds its
  domain's instruments **by name** and references nothing, so none adds a dependency; each is idempotent.
  `WithApplicationTelemetry()` also registers a bucket view for `sharedkernel.application.request.duration`, recorded
  in **seconds** — without it every request would land in the first default bucket.
- **Readiness.** `AddSharedKernelReadiness()` maps each `IReadinessProbe` (`SharedKernel.Primitives.Health`) to a
  health check named after the probe and tagged `ready`:

  | Probe report | Health status |
  | --- | --- |
  | `ReadinessStatus.Healthy` / `Degraded` / `Unhealthy` | `Healthy` / `Degraded` / `Unhealthy` |
  | The probe throws | `Unhealthy`, exception type only (EventId 13005) |

  The report's latency is added as `LatencyMilliseconds`. Registration order does not matter: probes are read when
  health checks are first resolved. Two probes with the same name fail resolution with an exception naming the duplicate.
- **Endpoints.** `/health/live` runs `live`-tagged checks only; `/health/ready` runs `ready`-tagged checks only
  (`startup`, every probe, the database checks). They are unauthenticated unless you pass `requireAuthorization: true`.
- **Log enrichment.** `BaggageLogRecordProcessor` copies two `Activity` baggage items, `correlation.id` and `TenantId`,
  onto each exported log record (an attribute already on the record wins; values with control characters or Unicode
  line separators are never copied). `UseSharedKernelRequestContext()` writes the correlation id and
  `TenantResolutionMiddleware` the tenant, so both appear on every log line without a call site passing them.
- **Inbound baggage is refused.** OpenTelemetry's `Baggage.Current` is never filled from a caller's `baggage` header,
  so HttpClient and gRPC instrumentation cannot forward a caller's items downstream; trace context is still read.
  Baggage your service sets itself still leaves with outgoing calls. A propagator set with
  `Sdk.SetDefaultTextMapPropagator` before the host starts is wrapped, not replaced. Clearing the caller's items from
  the request's `Activity` is done by `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`.
- **Rate limiting.** `AddSharedKernelRateLimiting()` sets a global fixed-window limiter partitioned by remote IP (100
  requests per minute, no queue) and a fixed-window policy named `authentication` (10 per minute), with status 429.
  It leaves `OnRejected` unset and takes no reference to the Presentation packages; with `UseSharedKernelWebApi()` the 429 is
  the platform's RFC 9457 problem (`rate_limit.exceeded`, `Retry-After`) and `UseRateLimiter()` is added for you.

## Recipes

### 1. Tune or trim readiness

```csharp
builder.Services.AddHealthChecks().AddSharedKernelReadiness(o =>
{
    o.Timeout = TimeSpan.FromSeconds(5);   // per check; default: no limit beyond the request's own
    o.Exclude("cache");                    // keep one probe off /health/ready
});
```

### 2. Add a readiness probe for your own dependency

Implement `IReadinessProbe` (keep the constructor cheap; resolve clients inside `ProbeAsync`) and register it with
`services.AddReadinessProbe<T>()` from `SharedKernel.Primitives.Health`. `AddSharedKernelReadiness()` picks it up.

### 3. Rate-limit a login endpoint

```csharp
using SharedKernel.ServiceDefaults.RateLimiting;

builder.AddSharedKernelRateLimiting(o => { /* adjust RateLimiterOptions */ });
app.MapPost("/login", ...).RequireRateLimiting(RateLimitPolicyNames.Authentication);
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IHostApplicationBuilder.AddServiceDefaults()` | `AddSharedKernelTelemetry(serviceName)` + `AddSharedKernelHealthChecks()` |
| `IHostApplicationBuilder.AddSharedKernelTelemetry(serviceName)` | OpenTelemetry tracing, metrics and logs (called by `AddServiceDefaults`) |
| `IServiceCollection.AddSharedKernelHealthChecks()` | `StartupGate` and the `startup` check (called by `AddServiceDefaults` — never call it again) |
| `IHealthChecksBuilder.AddSharedKernelReadiness(Action<ReadinessHealthCheckOptions>?)` | One `ready` check per `IReadinessProbe` |
| `IEndpointRouteBuilder.MapDefaultHealthCheckEndpoints(bool requireAuthorization = false)` | `/health/live`, `/health/ready` |
| `IHostApplicationBuilder.AddSharedKernelRateLimiting(Action<RateLimiterOptions>?)` | ASP.NET Core rate limiting |
| `WithApplicationTelemetry()` · `WithCachingTelemetry()` · `WithCommunicationTelemetry()` · `WithIntegrationTelemetry()` · `WithIntelligenceTelemetry()` · `WithMessagingTelemetry()` · `WithPersistenceTelemetry()` · `WithReportingTelemetry()` · `WithSchedulingTelemetry()` · `WithSearchTelemetry()` · `WithStorageTelemetry()` · `WithWorkflowTelemetry()` | A domain's `ActivitySource` and `Meter` |

### Types

| Type | Purpose |
| --- | --- |
| `StartupGate` | `MarkReady()` / `IsReady`; the `startup` check is unhealthy until marked |
| `ReadinessHealthCheckOptions` | `Timeout`, `Exclude(probeName)` |
| `HealthCheckNames` | `database`, `database-dapper`, `persistence-startup`, `startup` |
| `HealthCheckTags` | `live`, `ready`, `db` |
| `RateLimitPolicyNames.Authentication` | `"authentication"` |
| `HealthCheckRegistrationLogging.LogRegistration(services, categoryName, name, tags)` | Logs a readiness check's registration from a check of your own |
| `BaggageLogRecordProcessor` | The log-record enricher `AddServiceDefaults()` installs |

### Health

Kernel probe names: `messaging`, `redis`, `cache`, `encryption-key-provider`, `field-encryption`, `audit-sealing`,
`storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`,
`gotenberg`. Database checks come from
[`SharedKernel.ServiceDefaults.Persistence`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/README.md).

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13002 | Information | Health check registered (name: `{HealthCheckName}`, tags: `{Tags}`) |
| 13005 | Warning | Readiness probe `{ProbeName}` threw `{ExceptionType}` instead of reporting unhealthy |

## Testing

Reference [`SharedKernel.ServiceDefaults.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing/README.md)
(namespace `SharedKernel.Testing.ServiceDefaults`): `registration.ShouldBeTaggedReady()` and
`ShouldNotBeTaggedLive()` assert a `HealthCheckRegistration` lands on the right endpoint. For an end-to-end check,
host the service with `WebApplicationFactory<Program>` and request `/health/ready`; call `StartupGate.MarkReady()`
in the test host when your service does so after migrations.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Call `AddSharedKernelHealthChecks()` after `AddServiceDefaults()` | Chain onto `builder.Services.AddHealthChecks()` | A second call registers `startup` twice: `ArgumentException: Duplicate health checks were registered with the name(s): startup` |
| Forget `StartupGate.MarkReady()` | Call it once start-up work is done | `/health/ready` stays unhealthy and the pod never receives traffic |
| Expose `/health/ready` on public ingress | Pass `requireAuthorization: true`, or restrict it with a `NetworkPolicy` | Readiness output discloses your dependency topology |
| Put a `live` tag on a dependency check | Tag dependency checks `ready` only | A failing database would make Kubernetes restart healthy pods |
| Pass `CorrelationId`/`TenantId` as log-template placeholders | Rely on the baggage enrichment | They are added to every record already |
| Add a SharedKernel `ProjectReference` to this package | Put host integrations in a `SharedKernel.ServiceDefaults.{Capability}` package | Every service restores what the base references (`CompositionBaseIsolationTests`) |

## Design decisions

**Why a probe contract instead of `Add*HealthCheck` per provider?** A provider registers its `IReadinessProbe` when it
is configured, so readiness follows what the service actually uses, and this package needs no reference to any
provider.

**Why refuse inbound baggage?** W3C `baggage` is a request header an anonymous caller controls; trusting it would let
`baggage: TenantId=<another tenant>` reach logs and downstream services.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
