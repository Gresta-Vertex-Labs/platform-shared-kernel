# SharedKernel.ServiceDefaults

The composition base every Platform.SharedKernel microservice starts from: one call wires OpenTelemetry, the startup
readiness gate and the health-check endpoints, and one more maps every dependency's readiness probe.

**Tier: Host.** It references `SharedKernel.Primitives` (Foundation) and OpenTelemetry only, so a service restores
nothing it does not use. `CompositionBaseIsolationTests` locks that in.

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
```

The version comes from your single `SharedKernelVersion` property (see the root README, "Consuming the kernel").

## Quick start

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults()                    // first call: OpenTelemetry + the "startup" check
       .WithMessagingTelemetry();               // only the domains this service uses

builder.Services.AddHealthChecks()
       .AddSharedKernelReadiness();             // one "ready" check per registered IReadinessProbe

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();           // /health/live, /health/ready

// Once start-up work such as migrations is done, open /health/ready:
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.Run();
```

## Rules

| Rule | Why |
| --- | --- |
| Call `builder.AddServiceDefaults()` first | It wires telemetry and the base health-check infrastructure every later call builds on. |
| Chain checks onto `builder.Services.AddHealthChecks()` | **Not** `AddSharedKernelHealthChecks()`: `AddServiceDefaults()` already calls it, and a second call registers `"startup"` twice — the host then throws `ArgumentException: Duplicate health checks were registered with the name(s): startup`. |
| Call `AddSharedKernelReadiness()` once | Every provider registers its own probe when you register the provider. Registration order does not matter; probes are read when health checks are first resolved. |
| Keep `/health/ready` off public ingress, or pass `requireAuthorization: true` | Readiness output can disclose your dependency topology. See below. |
| Never add a SharedKernel `ProjectReference` to this package | Every service restores whatever it references. Host integrations that need another kernel package live in a `SharedKernel.ServiceDefaults.*` package. |

## Readiness

`AddSharedKernelReadiness()` maps each `IReadinessProbe` (`SharedKernel.Primitives.Health`) to a health check named
after the probe and tagged `ready`:

| Probe report | Health status |
| --- | --- |
| `ReadinessStatus.Healthy` | `Healthy` |
| `ReadinessStatus.Degraded` | `Degraded` |
| `ReadinessStatus.Unhealthy` | `Unhealthy` |
| the probe throws | `Unhealthy`, exception type only (EventId `13005`) |

The report's latency is added to the check data as `LatencyMilliseconds`. Two probes with the same name make
health-check resolution fail with an exception naming the duplicate.

```csharp
builder.Services.AddHealthChecks().AddSharedKernelReadiness(o =>
{
    o.Timeout = TimeSpan.FromSeconds(5);        // per check; default: no limit beyond the request's own
    o.Exclude("cache");                         // keep one probe off /health/ready
});
```

Probe names registered by the kernel: `messaging`, `redis`, `cache`, `encryption-key-provider`, `field-encryption`,
`audit-sealing`, `storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`,
`scheduler`. For a dependency of your own, implement `IReadinessProbe` (keep its constructor cheap; resolve clients
inside `ProbeAsync`) and register it with `services.AddReadinessProbe<T>()`.

Database checks for EF Core and Dapper are in
[`SharedKernel.ServiceDefaults.Persistence`](../SharedKernel.ServiceDefaults.Persistence/README.md).

## What is in this package

| Member | Purpose |
| --- | --- |
| `AddServiceDefaults()` | OpenTelemetry traces, metrics and logs (OTLP, configured by the standard `OTEL_EXPORTER_OTLP_*` variables) with ambient `correlation.id`/`TenantId` log enrichment, plus the base health checks. A caller's `baggage` header never fills OpenTelemetry's baggage store, and only the platform's two keys reach log records (see below) |
| `AddSharedKernelReadiness(configure?)` | Maps every registered `IReadinessProbe` to a `ready` check |
| `MapDefaultHealthCheckEndpoints(requireAuthorization)` | Maps `/health/live` (`live`-tagged checks only) and `/health/ready` (`ready`-tagged checks only) |
| `StartupGate` | Keeps `/health/ready` unhealthy until you call `MarkReady()` |
| `HealthCheckNames`, `HealthCheckTags` | The shared names and tags |
| `HealthCheckRegistrationLogging` | Logs a readiness check's registration (EventId `13002`) — use it in a check of your own |
| `AddSharedKernelRateLimiting()` | ASP.NET Core rate limiting with conservative defaults |
| `WithApplicationTelemetry()` · `WithCachingTelemetry()` · `WithCommunicationTelemetry()` · `WithIntegrationTelemetry()` · `WithIntelligenceTelemetry()` · `WithMessagingTelemetry()` · `WithPersistenceTelemetry()` · `WithReportingTelemetry()` · `WithSchedulingTelemetry()` · `WithSearchTelemetry()` · `WithStorageTelemetry()` · `WithWorkflowTelemetry()` | Registers a domain's `ActivitySource` and `Meter` with the host |

Every `WithXTelemetry()` wires its domain's instruments **by name** and references nothing, so none adds a
dependency. Each is idempotent.

`WithApplicationTelemetry()` also registers a bucket view for `sharedkernel.application.request.duration`, which
`SharedKernel.Application.Pipeline` records **in seconds**: the SDK's default buckets assume milliseconds, so without
the view every request would land in the first bucket.

## Log enrichment

`BaggageLogRecordProcessor` copies two `Activity` baggage items, `correlation.id` and `TenantId`, onto each exported
log record; an attribute already on the record wins. `SharedKernel.ServiceDefaults.Security`'s
`UseSharedKernelRequestContext()` puts the correlation id in baggage and `SharedKernel.MultiTenancy`'s
`TenantResolutionMiddleware` the resolved tenant id, so both appear on every log line without a call site passing
them. No other baggage item is copied.

## Baggage a caller sends

W3C `baggage` is a request header like any other: an anonymous caller can send
`baggage: TenantId=<another tenant>,SubjectId=admin`. Since P-562 X2 this package trusts none of it:

- **Log records** get two baggage items only, `correlation.id` and `TenantId`, the ones platform middleware
  writes (and replaces). A value containing a control character or a Unicode line separator is never
  copied. Anything else you want on a log record belongs in the log statement.
- **OpenTelemetry's `Baggage.Current`** is never filled from an incoming request, so the HttpClient and gRPC
  client instrumentations cannot forward a caller's items downstream. Trace context is still read.
  Baggage your service sets itself still leaves with outgoing calls: `Baggage.SetBaggage(...)` items and,
  while `Baggage.Current` is empty, `Activity` baggage such as the correlation id.

The request's `Activity` is cleared of the caller's items at the edge by `SharedKernel.ServiceDefaults.Security`'s
`UseSharedKernelRequestContext()` (`TrustInboundBaggage`, off by default); this package does not read that setting.
Without it, the caller's items stay on the activity — .NET sends them with outgoing HTTP calls, and a caller's
`TenantId` or `correlation.id` item is logged unless tenant resolution or the request-context middleware replaces it.
A propagator you set with `Sdk.SetDefaultTextMapPropagator` before the host starts is wrapped, not replaced; one set
after start removes the protection.

## Security note — health endpoints

`MapDefaultHealthCheckEndpoints()` maps unauthenticated endpoints by default, because Kubernetes probes cannot present
credentials, and readiness output can disclose your dependency topology. Pass `requireAuthorization: true`, or keep
these endpoints off public ingress and restrict them with a `NetworkPolicy`. Never expose readiness on an
internet-facing service.

## Rate limiting and ProblemDetails

`AddSharedKernelRateLimiting()` leaves `OnRejected` unset and takes **no** reference to `14.Presentation`.
On its own, a rejection is ASP.NET Core's bare 429. Together with `14.Presentation`'s
`AddSharedKernelWebApi()`/`UseSharedKernelWebApi()` it is the platform's RFC 9457 body with nothing to write:
429 `application/problem+json` with `errorCode` `rate_limit.exceeded` and `Retry-After`, and
`UseSharedKernelWebApi()` adds `UseRateLimiter()` itself. An `OnRejected` you set through `configure` wins
over both. The two packages stay independently referenceable.

## Related packages

- [`SharedKernel.ServiceDefaults.Security`](../SharedKernel.ServiceDefaults.Security/README.md) — the request context and correlation id
- [`SharedKernel.ServiceDefaults.Persistence`](../SharedKernel.ServiceDefaults.Persistence/README.md) — database readiness checks
- [`SharedKernel.MultiTenancy`](../SharedKernel.MultiTenancy/README.md) — tenant resolution
- [13.ServiceDefaults README](../README.md) — the full composition and middleware order
