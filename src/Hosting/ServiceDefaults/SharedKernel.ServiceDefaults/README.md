# SharedKernel.ServiceDefaults

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The composition base every service starts from: one call wires OpenTelemetry, a startup gate and the health-check
> infrastructure; one more maps every dependency's readiness probe onto `/health/ready`.**

| You get | So that |
| --- | --- |
| `builder.AddServiceDefaults()` | Traces, metrics and logs over OTLP, plus the `startup` readiness check, in one call |
| Every `SharedKernel.*` source and meter, HttpClient metrics and the SignalR server source, exported with no call | A capability's telemetry can never be dropped by a forgotten registration or a stale hand-copied name |
| Four `WithXTelemetry()` extensions | Third-party instrumentation (MassTransit, Npgsql, Polly with gRPC client, Temporal) is exported only when the service uses it — no extra dependency |
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
- [Configuration](#configuration)
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
builder.WithMessagingTelemetry();                   // only third-party instrumentation (MassTransit); SharedKernel.* needs no call

builder.Services.AddHealthChecks()
    .AddSharedKernelReadiness();                    // one "ready" check per registered IReadinessProbe

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();               // /health/live, /health/ready

// Once start-up work such as migrations is done, open /health/ready:
app.Services.GetRequiredService<StartupGate>().MarkReady();

app.Run();
```

The OTLP exporter is configured by the standard `OTEL_EXPORTER_OTLP_*` variables, read from the host configuration (environment variables included; see [Configuration](#configuration)); the service name is the entry
assembly's name.

## How it works

- **Telemetry.** `AddServiceDefaults()` registers tracing (ASP.NET Core, HttpClient, EF Core, the SignalR server
  source), metrics (ASP.NET Core, HttpClient, runtime) and logging (scopes and formatted messages), all exported over
  OTLP. **Every `SharedKernel.*` source and meter is exported** through one wildcard, including ones created after the
  host started; a name outside that prefix is not. Only third-party instrumentation needs a `WithXTelemetry()` call
  (see [Telemetry](#telemetry)); each is idempotent and references nothing.
- **One resource.** The three signals share one resource: `service.name` is the entry assembly's name (or the name you
  pass to `AddSharedKernelTelemetry`), `service.version` is the entry assembly's informational version with build
  metadata (`1.4.0+3f2a9c1`), `0.0.0` when there is no entry assembly. `OTEL_RESOURCE_ATTRIBUTES` adds attributes.
- **Duration buckets.** A `Histogram<double>` with unit `s` in a `SharedKernel.*` meter gets the second-scale
  boundaries (`0.005 … 10`); without them every request would land in the first default bucket. Any other histogram keeps
  the SDK defaults.
- **Readiness.** `AddSharedKernelReadiness()` maps each `IReadinessProbe` (`SharedKernel.Primitives.Health`) to a
  health check named after the probe and tagged `ready`:

  | Probe report | Health status |
  | --- | --- |
  | `ReadinessStatus.Healthy` / `Degraded` / `Unhealthy` | `Healthy` / `Degraded` / `Unhealthy` |
  | The probe throws | `Unhealthy`, exception type only in the report; the exception object is logged (EventId 13001) |

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

## Configuration

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:ServiceDefaults:Telemetry:OtlpExporter` (environment: `SharedKernel__ServiceDefaults__Telemetry__OtlpExporter`) | `Always` · `WhenEndpointConfigured` | `Always` | `Always` registers the OTLP exporter for traces, metrics and logs whether or not an endpoint is set, exactly as 1.0.0 did. `WhenEndpointConfigured` registers it only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so a host with no collector exports nothing and shuts down without waiting for the exporter timeout. `AddServiceDefaults(o => o.OtlpExporter = …)` overrides the key. An undefined value fails host start-up |

The OTLP endpoint is the standard `OTEL_EXPORTER_OTLP_ENDPOINT` variable (with `OTEL_EXPORTER_OTLP_PROTOCOL` and the other
`OTEL_EXPORTER_OTLP_*` settings the exporter reads). It counts as set when the host configuration (environment variables,
`appsettings` or the command line) holds an absolute URI; a blank value or a value that is not a URI counts as unset, as it
does for the exporter itself. The per-signal variants (`OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` and its metrics and logs
siblings) are not read by the per-signal exporter registration this package uses, so they do not count. The mode and the
endpoint are read when `AddServiceDefaults()` runs, from the configuration as it is then; a configuration source added
afterwards is not seen.

With `Always` and no endpoint the host logs one warning at start-up (event 13002) that names the setting. To silence it,
point `OTEL_EXPORTER_OTLP_ENDPOINT` at your collector, or choose `WhenEndpointConfigured` for hosts that run without one. A host
launched by .NET Aspire has the endpoint set by the AppHost and behaves the same in either mode.

```csharp
using SharedKernel.ServiceDefaults.Telemetry;

builder.AddServiceDefaults(o => o.OtlpExporter = OtlpExporterMode.WhenEndpointConfigured);
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IHostApplicationBuilder.AddServiceDefaults()` | `AddSharedKernelTelemetry(serviceName)` + `AddSharedKernelHealthChecks()` |
| `IHostApplicationBuilder.AddServiceDefaults(Action<SharedKernelTelemetryOptions>?)` | The same, with the telemetry options (the OTLP exporter mode) set in code |
| `IHostApplicationBuilder.AddSharedKernelTelemetry(serviceName, Action<SharedKernelTelemetryOptions>?)` | The same, with the telemetry options set in code |
| `IHostApplicationBuilder.AddSharedKernelTelemetry(serviceName)` | OpenTelemetry tracing, metrics and logs (called by `AddServiceDefaults`) |
| `IServiceCollection.AddSharedKernelHealthChecks()` | `StartupGate` and the `startup` check (called by `AddServiceDefaults` — never call it again) |
| `IHealthChecksBuilder.AddSharedKernelReadiness(Action<ReadinessHealthCheckOptions>?)` | One `ready` check per `IReadinessProbe` |
| `IEndpointRouteBuilder.MapDefaultHealthCheckEndpoints(bool requireAuthorization = false)` | `/health/live`, `/health/ready` |
| `IHostApplicationBuilder.AddSharedKernelRateLimiting(Action<RateLimiterOptions>?)` | ASP.NET Core rate limiting |
| `WithCommunicationTelemetry()` · `WithMessagingTelemetry()` · `WithPersistenceTelemetry()` · `WithWorkflowTelemetry()` | Third-party instrumentation, see [Telemetry](#telemetry) |

### Types

| Type | Purpose |
| --- | --- |
| `StartupGate` | `MarkReady()` / `IsReady`; the `startup` check is unhealthy until marked |
| `ReadinessHealthCheckOptions` | `Timeout`, `Exclude(probeName)` |
| `HealthCheckNames` | `database`, `database-dapper`, `persistence-startup`, `startup` |
| `HealthCheckTags` | `live`, `ready`, `db` |
| `RateLimitPolicyNames.Authentication` | `"authentication"` |
| `HealthCheckRegistrationLogging.LogRegistration(services, name, tags)` | Logs a health check's registration (EventId 13000) from a check of your own; needs logging services in the host |
| `BaggageLogRecordProcessor` | The log-record enricher `AddServiceDefaults()` installs |

### Telemetry

Exported by `AddServiceDefaults()` with no further call:

| Signal | What |
| --- | --- |
| Traces | Every `ActivitySource` named `SharedKernel.*`; `Microsoft.AspNetCore.SignalR.Server` (hub connect, invocation, disconnect); ASP.NET Core, HttpClient and EF Core instrumentation |
| Metrics | Every `Meter` named `SharedKernel.*`; HttpClient (`http.client.request.duration` and friends); ASP.NET Core and runtime instrumentation |

A `SharedKernel.*` name matches `SharedKernel.Messaging` and `SharedKernel.Persistence.EfCore.Auditing`, but not
`SharedKernel` or `OtherVendor.SharedKernel.X`. The four extensions subscribe third-party names that the owning
library must keep byte-identical:

| Extension | Subscribes | Owner |
| --- | --- | --- |
| `WithMessagingTelemetry()` | `MassTransit` source and meter | MassTransit |
| `WithPersistenceTelemetry()` | `Npgsql` source and meter (emits once the service enables Npgsql's OpenTelemetry support) | Npgsql |
| `WithCommunicationTelemetry()` | gRPC client instrumentation (`rpc.*` span tags) and the `Polly` meter | OpenTelemetry gRPC client instrumentation, Polly |
| `WithWorkflowTelemetry()` | `Temporalio.Extensions.OpenTelemetry.*`: the client, workflow, activity and Nexus sources of Temporal's `TracingInterceptor` | Temporal SDK |

```csharp
builder.AddServiceDefaults();             // every SharedKernel.* source and meter, HttpClient metrics
builder.WithMessagingTelemetry();         // MassTransit's own spans and metrics
builder.WithWorkflowTelemetry();          // Temporal client, workflow and activity spans
```

### Resilience logs

The standard resilience handler (`AddStandardResilienceHandler`) logs every failed attempt under the `Polly` logger
category with the outcome's exception message as the `{Result}` property: a host, a URL, a key. It offers no switch to
format exceptions and sits outside every kernel logger, so `AddServiceDefaults()` adds a `Polly` category filter at
`LogLevel.None`. The `Polly` meter (`WithCommunicationTelemetry()`) is unaffected. A service that wants those records back
calls `builder.Logging.AddFilter("Polly", LogLevel.Warning)` after `AddServiceDefaults()` and accepts what they carry; a
host composed without `AddServiceDefaults()` needs the same filter. `service.version` is the entry assembly's
informational version (build metadata kept, for example `1.4.0+3f2a9c1`), `0.0.0` when there is no entry assembly.

### Health

Kernel probe names: `messaging`, `redis`, `cache`, `encryption-key-provider`, `field-encryption`, `audit-sealing`,
`storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`,
`gotenberg`. Database checks come from
[`SharedKernel.ServiceDefaults.Persistence`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/README.md).

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13000 | Information | Registered the health check `'{HealthCheckName}'` with the tags `'{Tags}'` |
| 13001 | Error | The readiness probe `'{ProbeName}'` threw an exception instead of reporting its state (the exception is passed to the logger) |
| 13002 | Warning | No OTLP endpoint is configured, so the OTLP exporters send to the exporter's local default address: set `'OTEL_EXPORTER_OTLP_ENDPOINT'` to your collector, or set `'SharedKernel:ServiceDefaults:Telemetry:OtlpExporter'` to `'WhenEndpointConfigured'` to export nothing without one (once at start-up, only while an exporter is registered) |

This package owns the sub-block 13000-13099 of the ServiceDefaults EventId block (13000-13999); every `[LoggerMessage]` of the domain stays inside its package's sub-block. Loggers are required dependencies: resolving health checks in a host without logging services throws.

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
