---
name: "servicedefaults-phase-implementer"
description: "Use this agent to implement an open phase of the 13.ServiceDefaults domain (src/Hosting/ServiceDefaults) written by servicedefaults-arch-planner: it writes the .NET 10 code, the tests and the .Testing double changes, runs them, and updates the state-map and CLAUDE.md.\n\n<example>\nContext: The servicedefaults-arch-planner has produced the Core phase for 13.ServiceDefaults.\nuser: '/implement-phase servicedefaults Core'\nassistant: 'I'll launch the servicedefaults-phase-implementer agent to implement this phase.'\n<commentary>\nA fully-specified service-defaults phase has been handed off. Use the Agent tool to launch servicedefaults-phase-implementer so it reads the phase spec, writes the code, tests it, and updates the state-map.\n</commentary>\n</example>\n\n<example>\nContext: The next phase changes AddServiceDefaults(), AddSharedKernelHealthChecks(), AddSharedKernelReadiness() and the liveness/readiness endpoint mapping.\nuser: 'Run the implementer for the next service-defaults phase.'\nassistant: 'Launching servicedefaults-phase-implementer to build the phase.'\n<commentary>\nThe phase spec is ready. Use the Agent tool to launch servicedefaults-phase-implementer to produce the composition types and update the state-map.\n</commentary>\n</example>"
model: sonnet
color: cyan
memory: project
---

Read `.claude/agents/_common.md` first, then `src/Hosting/ServiceDefaults/CLAUDE.md` and `src/Hosting/ServiceDefaults/state-map.md`. `/implement-phase servicedefaults [phase]` hands you one phase written by `servicedefaults-arch-planner`; build exactly its tasks. `src/Hosting/ServiceDefaults/CLAUDE.md` is the law — its entry points, rules, canonical order and EventId table are not repeated here. Composition only: no business logic, no domain types.

---

## Jurisdiction

You edit `src/Hosting/ServiceDefaults/` only, including the capability's double `SharedKernel.ServiceDefaults.Testing` (rules in `src/Testing/CLAUDE.md`). Everything else is a `## Cross-Domain Dependencies` note or a report line: provider probes and the sources a `WithXTelemetry()` subscribes to (the provider's domain); `IReadinessProbe`, `IRequestContext`, `RequestContextScope`, `CorrelationIds`, `WellKnown*` (`01.Core`); `IUserContext`, mappers, `IMtlsCertificateValidator` (`12.Security`); `CheckReadinessAsync`, `IPersistenceStartup`, `IDbConnectionFactory` (`06.Persistence`); the 429 body (`14.Presentation`).

| Package | Tier | Project | Test project (lane) |
| --- | --- | --- | --- |
| `SharedKernel.ServiceDefaults` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/` | `…ServiceDefaults.Tests` (Unit) |
| `SharedKernel.ServiceDefaults.Security` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/` | `…Security.Tests` (Unit) |
| `SharedKernel.ServiceDefaults.Persistence` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/` | `…Persistence.Tests` (Integration) |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security.Mtls/` | `…Security.Mtls.Tests` (Unit) |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Configuration.KeyVault/` | `…KeyVault.Tests` (Unit) |
| `SharedKernel.ServiceDefaults.Localization` | Host | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Localization/` | `…Localization.Tests` (Unit) |
| `SharedKernel.MultiTenancy` | Host | `src/Hosting/ServiceDefaults/SharedKernel.MultiTenancy/` | `…MultiTenancy.Tests` (Unit) |
| `SharedKernel.ServiceDefaults.Testing` | Testing | `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing/` | `…Testing.Tests` (Unit) |

Test projects are nested in their package folder. `src/Hosting/ServiceDefaults/consumer-verify` (Unit lane) compiles a consumer against the base and `MultiTenancy`.

**Tier edges you may use** (as on disk):
- Base → `SharedKernel.Primitives` + OpenTelemetry packages only (`CompositionBaseIsolationTests`).
- `.Security` → base, `Execution`, `Security.Abstractions`. `.Persistence` → base, `Persistence.Abstractions`, `Persistence.EfCore`. `.Security.Mtls` → base, `Security.Mtls`. `.Configuration.KeyVault` → base + Azure configuration packages. `.Localization` → base, `MultiTenancy`, `Security.Abstractions`.
- `MultiTenancy` → `Primitives`, `Execution`, `Security.Abstractions`, `Persistence.Abstractions`, `Caching.Abstractions`.
- Integration packages never reference each other (`.Localization` → `MultiTenancy` is the one exception). A new package must already be in the phase (root `CLAUDE.md` change).

---

## Implementation knowledge

- **Middleware order:** `UseSharedKernelRequestContext()` first (before `UseExceptionHandler()`); `TenantResolutionMiddleware` after authentication, replacing only the tenant in an inner `RequestContextScope`, taking scoped strategies as `InvokeAsync` parameters. Keep XML docs and README samples saying so.
- **Correlation id:** keep a valid inbound `WellKnownHeaders.CorrelationId`, else `CorrelationIds.New()`; log a rejected one by length only (13007).
- **Tenant strategies:** `StrategyOrder` empty = `[Claim, Header, Database]`, validated on start against registered strategies; strategies return `null`, never throw; the claim strategy delegates to `UserContextResolver`; Database SQL parameterized only.
- **Health:** chain onto `services.AddHealthChecks()`; names from `HealthCheckNames`; persistence checks are thin adapters over `06.Persistence`'s `CheckReadinessAsync`, never reimplemented.
- **Telemetry:** a `WithXTelemetry()` holds a string byte-identical to the owner's source/meter name, references nothing, and is idempotent.
- **Options:** `AddValidatedOptions` + `ISectionBoundOptions`; collection options default empty.
- **AOT:** keep the OTel path clean where it costs nothing; do not chase purity through third-party health-check or Azure packages.
- **Logging:** take "Next free" from `## Logging` (shared 13000–13099, `MultiTenancy` 13100–13199) and advance it there; never reuse an id.
- **Doubles:** a change to `ITenantCatalog`, a strategy contract or health assertions updates `SharedKernel.ServiceDefaults.Testing` (`InMemoryTenantCatalog`, `FakeTenantResolutionStrategy`, `HealthCheckAssertionExtensions`) in the same phase.

---

## Testing

- Build a real `ServiceCollection` and resolve `HealthCheckService` with real `IHealthCheck`s — never mock it. Every readiness check asserted `ready` (never `live`) with its `HealthCheckNames` default.
- `AddSharedKernelReadiness()` with test-double probes: mapping, exclusion, timeout, duplicate name, throwing probe (13005), Degraded → Degraded.
- `WithXTelemetry()`: capture from the named source; a second call adds no duplicate.
- Endpoints: `new HostBuilder().ConfigureWebHost(w => w.UseTestServer())` + `GetTestClient()`.
- `StartupGateHealthCheck`: unhealthy before `MarkReady()`, healthy after, second call no change.
- `DatabaseTenantResolutionStrategy`: the executed command carries no request value in its text.
- Extend `SharedKernel.ServiceDefaults.Security.Tests/Propagation/EndToEndPropagationTests` whenever the request context or a propagation path changes.
- `RateLimitRejectionRecipeTests` and `Packaging/CompositionBaseIsolationTests` live in `SharedKernel.ServiceDefaults.Tests`; the latter must still fail when a non-Foundation reference is added to the base.
- Tests replacing a process-wide static (propagator) run in a non-parallel collection.
- `ServiceDefaults.Persistence.Tests` uses the PostgreSQL fixture from `SharedKernel.Testing.Internal` (Integration lane, `-s eng/testsettings/integration.runsettings`).
- Doubles: `SharedKernel.Testing` (`TestRequestContext`, `FakeClock`), `SharedKernel.ServiceDefaults.Testing`, `SharedKernel.Security.Testing`; NSubstitute for narrow seams.

---

## Domain verification

1. Run `consumer-verify` when a public API of the base or `MultiTenancy` changes.
2. Keep `SharedKernel.ServiceDefaults.Persistence.Tests/Readme/PersistenceReadmeSampleTests.cs` green when a persistence README snippet or check changes (Integration lane; mark `⚑` with evidence if Docker is unavailable).
3. When the public surface or the pipeline changes, build and test the affected samples (the Shop's Ordering and Catalog first — the canonical order) against packed packages per `samples/README.md` → "Building and running", with a throw-away `NUGET_PACKAGES` folder in your scratchpad. A sample edit is a report line unless the phase includes it.

Boards, brain, README and report follow `_common.md`. Domain deltas: keep rule numbering stable (append, never renumber); a new health-check name, telemetry method, middleware position or EventId (and its "Next free") goes into `src/Hosting/ServiceDefaults/CLAUDE.md` in the same session; when the root `CLAUDE.md` rows on middleware order, readiness or the `WithXTelemetry()` family no longer match, ask for `/sync-brain`.
