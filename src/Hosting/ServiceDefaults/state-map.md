# 13.ServiceDefaults — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.ServiceDefaults` | Host | ● | Composition base, references Foundation-tier packages only (`CompositionBaseIsolationTests`): `AddServiceDefaults` (exports every `SharedKernel.*` source and meter, HttpClient metrics, one resource with the informational `service.version`), the third-party `With{X}Telemetry()` (Communication, Messaging, Persistence, Workflow), `StartupGate`, `/health/live` + `/health/ready`, `AddSharedKernelReadiness()` (every `IReadinessProbe` → a `ready` check), `AddSharedKernelRateLimiting`. |
| `SharedKernel.ServiceDefaults.Security` | Host | ● | `AddSharedKernelRequestContext()` (`IRequestContext` over `IUserContext`) and `app.UseSharedKernelRequestContext()` — correlation id, the request's `RequestContextScope`, inbound-baggage refusal (`TrustInboundBaggage`); registered first. |
| `SharedKernel.ServiceDefaults.Persistence` | Host | ● | `AddDatabaseReadinessCheck<T>`, `AddPersistenceStartupReadinessCheck` — the only per-dependency readiness registrations left. |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | ● | Kestrel and trusted forwarded-header client certificates (`AddMtlsClientCertificate`), acceptance delegated to `Security.Mtls`'s `IMtlsCertificateValidator`. |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | ● | Key Vault as an `IConfiguration` source (`AddSharedKernelKeyVaultConfiguration()`). |
| `SharedKernel.ServiceDefaults.Localization` | Host | ● | `AddSharedKernelLocalization()` — user preference claim → tenant default culture → `Accept-Language`. |
| `SharedKernel.MultiTenancy` | Host | ● | `TenantResolutionMiddleware` (Claim → Header → Database, returning `TenantId?`, inner scope replaces only the tenant), `ITenantCatalog`/`TenantDescriptor`, `DatabaseTenantCatalog`/`CachedTenantCatalog`, `ITenantStatusValidator`/`CatalogTenantStatusValidator`. |
| `SharedKernel.ServiceDefaults.{AI, Caching, Caching.Redis, Messaging, Scheduling, Search, Storage, Workflows.Temporal, Cryptography.KeyVault}` | — | ⊘ | Created by WO-084, deleted by WO-086 (P-569): providers now register their own `IReadinessProbe`. |

Test doubles: `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog` in `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing`. Verified by `consumer-verify`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.13.OtlpExporterMode` | OTLP exporter mode in service defaults (default `Always`, opt-in `WhenEndpointConfigured`) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.13.OtlpExporterMode — OTLP exporter mode in service defaults `○`

**Work order:** WO-090 / P-634 · **Depends on:** None

`AddSharedKernelTelemetry()` registers `AddOtlpExporter()` per signal (traces, metrics, logs) unconditionally, so a host with no collector exports to the exporter's implicit local default and its shutdown waits out the exporter timeout. The phase adds an explicit exporter mode in `SharedKernel.ServiceDefaults` (Foundation-only; no new package): `Always` (the 1.1.0 default, identical to 1.0.0 registrations) and `WhenEndpointConfigured` (opt-in; a signal's exporter is registered only when an OTLP endpoint is configured for it). The per-signal `AddOtlpExporter()` wiring is kept (not `UseOtlpExporter()`), so 1.0.0 registrations, processor order and per-signal endpoint variables stay as they are. "Configured" means `IConfiguration` (environment variables included) holds a non-blank `OTEL_EXPORTER_OTLP_ENDPOINT` (exactly the check Aspire's own service defaults make) or the signal's own `OTEL_EXPORTER_OTLP_{TRACES|METRICS|LOGS}_ENDPOINT` (a superset, so a host that sets only a per-signal endpoint, which the exporter honours, never loses that signal). When the exporter is on and no endpoint is configured for any signal, the host logs one startup warning (EventId 13002) naming the setting and the remedy. Flipping the default to `WhenEndpointConfigured` is recorded as a 2.0.0 candidate (`CLAUDE.md` Decisions and Known Limitations).

Placement reshape: the proposal asks for the validated options pattern, but `AddValidatedOptions`/`ISectionBoundOptions` live in `SharedKernel.Configuration`, which the base may not reference (rule 1). The options type therefore binds with `Microsoft.Extensions.Options` directly (`AddOptions<T>().BindConfiguration(section).Validate(...).ValidateOnStart()`) and declares its section path as a public constant; no new reference. Recorded as a Decision.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify: option `SharedKernelTelemetryOptions.OtlpExporter` (enum `OtlpExporterMode { Always = 0, WhenEndpointConfigured = 1 }`, `Always` is the zero value), section `SharedKernel:ServiceDefaults:Telemetry`, env form `SharedKernel__ServiceDefaults__Telemetry__OtlpExporter`; validator rejects undefined enum values. Resolve the mode at provider build (`ConfigureOpenTelemetryTracerProvider`/`MeterProvider`/`LoggerProvider` with `IServiceProvider`) so the configure delegate and late configuration sources count; keep `BaggageLogRecordProcessor` ahead of the log exporter (processor order is observable). Confirm against the pinned OpenTelemetry exporter package that it reads the endpoint from `IConfiguration` as well as the environment, and that a blank value counts as not configured | SharedKernel.ServiceDefaults | `○` |
| C-01 | `OtlpExporterMode`, `SharedKernelTelemetryOptions` (sealed, `public const string SectionName`, XML docs), `OtlpEndpointDetector` (internal, pure: generic key or the signal's key non-blank), options registration with `ValidateOnStart` (no `services.Configure`) | SharedKernel.ServiceDefaults | `○` |
| C-02 | Additive overloads `AddSharedKernelTelemetry(serviceName, Action<SharedKernelTelemetryOptions>)` and `AddServiceDefaults(Action<SharedKernelTelemetryOptions>)`; existing overloads keep their signatures and behaviour. Exporter registration per signal moves behind the mode; `Always` registers all three exactly as 1.0.0 | SharedKernel.ServiceDefaults | `○` |
| C-03 | Startup warning: `internal` hosted service registered once (idempotent on a second `AddSharedKernelTelemetry`), category `ServiceDefaultsLogCategory`, `[LoggerMessage]` EventId 13002 (`ServiceDefaultsLoggingEventIds.OtlpEndpointNotConfigured`), Warning, no placeholder carrying a host or URL; fires only when an exporter is registered and no endpoint is configured for any signal; silent in `WhenEndpointConfigured` | SharedKernel.ServiceDefaults | `○` |
| T-01 | `WhenEndpointConfigured` + no endpoint: no OTLP exporter registered for traces, metrics or logs (structural assertion on the built providers, not a timing assertion; no collector needed) and the host disposes without waiting on the exporter timeout; with the generic endpoint, and with only one per-signal endpoint, exactly the configured signals export | SharedKernel.ServiceDefaults.Tests | `○` |
| T-02 | Default mode: registrations equal 1.0.0's (all three signals, baggage processor first in logs) with and without an endpoint; exactly one 13002 warning when no endpoint is configured, none when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, one (not two) after a second `AddSharedKernelTelemetry`; log convention asserted (`ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock`, category). Tests join `NonParallelTelemetryCollection` and use in-memory configuration; the assembly-wide `OTEL_EXPORTER_OTLP_TIMEOUT` initializer stays | SharedKernel.ServiceDefaults.Tests | `○` |
| T-03 | Options: section binding, the env-var form, an undefined enum value fails `ValidateOnStart`, the configure delegate overrides configuration; an Aspire-style host (generic endpoint set) is unchanged. `EventIdUniquenessTests` and `CompositionBaseIsolationTests` stay green and able to fail | SharedKernel.ServiceDefaults.Tests | `○` |
| DO-01 | Base README: configuration table row (`SharedKernel:ServiceDefaults:Telemetry:OtlpExporter`, values, default `Always`), the endpoint variables it recognises, the warning and its remedy, the Aspire note; fix the "reads the standard `OTEL_EXPORTER_OTLP_*` variables" sentence; `PublicAPI.Unshipped.txt`; `consumer-verify/` compiles the new overloads and options; domain `CLAUDE.md` rule 33, Decisions, Known Limitations and (when shipped) Logging table and Next free 13003 | SharedKernel.ServiceDefaults | `○` |

**Acceptance:**
- [ ] With `OtlpExporter = WhenEndpointConfigured` and no endpoint present, no OTLP exporter is registered for traces, metrics or logs and host shutdown does not wait on a collector; with an endpoint present (generic, or a signal's own for that signal) it exports as before.
- [ ] With the default mode, registrations are identical to 1.0.0 (three per-signal OTLP exporters, same processor order), plus exactly one startup warning (EventId 13002) when no endpoint is configured; a test pins both, and a second `AddSharedKernelTelemetry` call does not double the warning.
- [ ] The setting binds from `SharedKernel:ServiceDefaults:Telemetry:OtlpExporter` through `Microsoft.Extensions.Options` with `ValidateOnStart` (the base cannot reference `SharedKernel.Configuration`, rule 1), is in the base README configuration table, and an Aspire-launched host (generic endpoint set by the AppHost) behaves exactly as before.
- [ ] All public additions are additive (new enum, options class, overloads); `PublicAPI.Unshipped.txt` lists them; no existing signature or default changes.
- [ ] Release-note text, carried verbatim: "Added: `SharedKernel:ServiceDefaults:Telemetry:OtlpExporter` (`Always` or `WhenEndpointConfigured`), also settable through `AddServiceDefaults(o => o.OtlpExporter = ...)`. The default remains `Always`, so the OTLP exporter is registered for traces, metrics and logs exactly as in 1.0.0. Changed: a host that exports with `Always` and has no OTLP endpoint configured (`OTEL_EXPORTER_OTLP_ENDPOINT`, or the traces, metrics or logs variant) now logs one startup warning, event 13002, that names the setting. Remedy: set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector, or set `SharedKernel:ServiceDefaults:Telemetry:OtlpExporter` to `WhenEndpointConfigured` so a host without a collector exports nothing and shuts down without waiting for the exporter timeout. A later major release may make `WhenEndpointConfigured` the default."

## Blocked

None.

## Cross-Domain Dependencies

- **Third-party names stay byte-identical (owners):** `MassTransit` (07), `Npgsql` (06), `Polly` and gRPC client instrumentation (11), the Temporal tracing sources (17, owner of the tracing interceptor). The four `With{X}Telemetry()` extensions subscribe them by name.
- **00.Governance:** the telemetry-name test is the guard that makes the `SharedKernel.*` wildcard safe: every kernel source and meter name must match `^SharedKernel\.[A-Z]\w+$`.
