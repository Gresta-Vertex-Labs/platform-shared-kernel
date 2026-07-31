---
name: domain_facts
description: Stable facts about the 13.ServiceDefaults domain's two packages, test counts, and file layout — refresh before trusting if it's been a while
metadata:
  type: project
---

**Packages:** `SharedKernel.ServiceDefaults` (host composition: OTel, health checks, StartupGate) and
`SharedKernel.MultiTenancy` (tenant resolution strategies, middleware, ambient provider). Both live under
`13.ServiceDefaults/`, each with a nested `.Tests` project — never a top-level `tests/` folder.

**README convention for this domain:** a single `13.ServiceDefaults/README.md` covers both packages —
there is no per-package README. Confirmed by `Glob` returning exactly one `README.md` match under
`13.ServiceDefaults/**`. If a future phase needs README updates, edit that one file.

**This domain's one layering exception:** it may reference concrete provider packages directly
(`SharedKernel.Caching.Redis*`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`/
`.PostgreSQL`/`.Dapper`, `SharedKernel.Security.Oidc`) in addition to abstractions — because it *is* the
composition root. No other domain gets this exception. `SharedKernel.ServiceDefaults.csproj` already
carries `ProjectReference`s to `SharedKernel.Messaging.Abstractions` AND `SharedKernel.Messaging.MassTransit`
(the concrete package) — the concrete reference was added for the RabbitMQ/ASB health checks (C-16/C-17),
which need `RabbitMqBusOptions`/`AzureServiceBusOptions`. This pre-existing reference is why
`WithMessagingTelemetry` (C-19) needed zero new `ProjectReference` work — it just had to confirm the
reference was already there.

**Folder structure inside `SharedKernel.ServiceDefaults`:** `Extensions/` (composition entry points),
`HealthChecks/` (tag-aware adapters), `Telemetry/` (OTel wiring — `TelemetryExtensions.cs`,
`CachingTelemetryExtensions.cs`, `MessagingTelemetryExtensions.cs`), `Probes/` (`StartupGate`,
`StartupGateHealthCheck`).

**Folder structure inside `SharedKernel.MultiTenancy`:** `Resolution/` (the three
`ITenantResolutionStrategy` implementations + `TenantResolutionOptions` + `TenantResolutionStrategyNames`),
`Middleware/` (`TenantResolutionMiddleware`, `AmbientTenantProvider`), `Extensions/`
(`AddSharedKernelMultiTenancy`).

**Test counts as of 2026-06-22 (SK.13.Core + SK.13.Tests + SK.13.Docs all fully complete, 28/28 + 22/22 + 2/2):**
37 `SharedKernel.ServiceDefaults.Tests` passing, 26 `SharedKernel.MultiTenancy.Tests` passing — Docs phase
added zero new tests (pure documentation pass). These numbers will drift once Published-phase or later
feature phases land — always re-run `dotnet test` rather than trusting this count once it's more than a
session or two old.

**Test counts as of 2026-07-27, end of session (SK.13.Core/Tests/Docs all closed `●` — C-39/C-41/C-42/C-43 +
T-34/T-36–38 + DO-08/09 implemented):** 86 `SharedKernel.ServiceDefaults.Tests` passing (+23 from the prior
63), 30 `SharedKernel.MultiTenancy.Tests` passing (unchanged). All six `SK.13.*` phase keys are now `●`/`—`
(only C-40/T-35, the permanently-retracted `AddOrchestrationReadinessCheck`, are `—`) — the domain is fully
closed out again, this time with VectorStore and Workflow readiness checks/telemetry genuinely implemented
(not just design-locked). `SharedKernel.ServiceDefaults.csproj` carries `ProjectReference`s to all of:
`SharedKernel.Primitives`, `SharedKernel.Caching.Abstractions`, `SharedKernel.Persistence.Abstractions`,
`SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit`,
`SharedKernel.Storage.Abstractions`, `SharedKernel.Search.Abstractions`, `SharedKernel.AI.Abstractions`
(10.Intelligence), and `SharedKernel.Workflows.Temporal` (17.Workflows — WO-047-scoped, guarded by an inline
`.csproj` comment restating the `IWorkflowServiceProbe`/`WorkflowServiceHealth`-only boundary).
`VectorStoreReadinessHealthCheck`/`WorkflowReadinessHealthCheck` and
`IntelligenceTelemetryExtensions`/`WorkflowTelemetryExtensions` were implemented as straight mirrors of the
already-shipped `SearchReadinessHealthCheck`/`SearchTelemetryExtensions` pattern — zero design deviation
needed once the upstream contracts were confirmed shipped. **Lesson for next time a phase's CLAUDE.md prose
needs a post-implementation pass:** grep for "design-locked", "blocked", "Core implementation pending" across
the whole file, not just the sections the phase spec calls out — this session found and fixed two already-
stale `AddSearchReadinessCheck`/`WithSearchTelemetry` mentions in AOT Compatibility/Test Rules that a PRIOR
session's WO-044 closeout had missed (they still said "design-locked, blocked" despite that check having
shipped back on 2026-07-24). A single targeted grep after finishing the "current" edits catches this class of
leftover drift cheaply.

**Test counts as of 2026-07-29, end of session (SK.13.Core/Tests/Docs closed `●` a third time — C-44/T-39/DO-10,
`WithCachingTelemetry`'s tracing addition, WO-050/P-305):** 88 `SharedKernel.ServiceDefaults.Tests` passing
(+2 from the prior 86), 30 `SharedKernel.MultiTenancy.Tests` passing (unchanged). All six `With*Telemetry`
siblings (`WithMessagingTelemetry`/`WithCachingTelemetry`/`WithApplicationTelemetry`/`WithSearchTelemetry`/
`WithIntelligenceTelemetry`/`WithWorkflowTelemetry`) now uniformly wire both a tracing source and a meter —
`WithCachingTelemetry` was the last one still metrics-only (a historical artifact of shipping first, at
P-010/WO-003, before the "wire both" convention existed). See [[otel_wiring_pattern]] for the new
`BaseProcessor<Activity>`-based genuine span-capture test technique this session introduced to this domain's
`Telemetry/` test suite. No root `CLAUDE.md` sync was needed — this was a pure "implement an already-locked
contract once the upstream blocker cleared" pass, identical in kind to the WO-043/WO-044/WO-045/WO-046
precedents, which also never touched root `CLAUDE.md`.

**Root state-map-phase quirk specific to this domain — read before calling `/state-map-phase` for a
sub-phase promotion:** the root `state-map.md` Domain Summary Board's "Current Phase" column for domain 13
already sits at `Published`/`●` (reached long ago, multiple times — this domain cycles reopen→Published as
each new WO adds then closes VectorStore/Workflow-shaped tasks). When a sub-map phase key (e.g.
`SK.13.Scaffold`) completes in isolation while other phase keys (Core/Tests/Docs) still have pending tasks,
do **not** mechanically overwrite root's Current Phase/State to the smaller phase name (e.g. "Scaffold") —
that would read as a regression since the domain's packages are genuinely already published. Established
precedent (root changelog, 2026-07-24 SK.13.Design entry, and repeated 2026-07-27 for SK.13.Scaffold): leave
Current Phase/State at `Published`/`●` unchanged, and only refresh the Summary: Done/Summary: Next cells to
narrow down what's actually still pending. Still append the root changelog line and still run Step S8a's
Phase Backlog check (usually N/A here — standard lifecycle phase names have no `Root Backlog ID`).

**Health check tag taxonomy (load-bearing, never violate):** `"live"` = process-alive only, zero
dependency coupling. `"ready"` = may depend on DB/cache/broker; gates load-balancer rotation, never
restarts the pod. Every dependency-specific check carries `"ready"` plus a dependency tag (`"db"`,
`"redis"`, `"cache"`, `"messaging"`). `AddCacheReadinessCheck` reports `Degraded` (never `Unhealthy`) on
probe failure — FusionCache's L1 fail-safe rationale. `HealthCheckNames` and `HealthCheckTags` are sibling
constants classes (names vs. tags) — both exist specifically to eliminate bare string literals; check both
before adding a new bare literal anywhere in this domain.

**Both csproj files already had `GenerateDocumentationFile=true` since Scaffold.** When doing a Docs-phase
XML-doc audit, don't assume you need to add this — check first (`grep GenerateDocumentationFile` on both
`.csproj`), then just run a clean Release build and look for CS1591 warnings to find real gaps mechanically
instead of eyeballing every file. As of the Docs-phase audit (2026-06-22), only one gap existed across both
packages' entire public surface: `AzureServiceBusHealthCheck`'s public constructor (internal sealed class,
but its constructor is public) lacked a doc comment — everything else had been documented to a high
standard during Core/Tests implementation already. Don't expect a large backlog of missing docs in this
domain; the implementation discipline here has been consistently thorough.

**Published phase (2026-06-22) — packaging gotchas specific to this domain's csproj shape, both fixed
via `NoWarn` with an inline comment explaining why, never by deleting `TreatWarningsAsErrors`:**

- `SharedKernel.ServiceDefaults.csproj` already had `FrameworkReference Microsoft.AspNetCore.App` (needed
  for `MapHealthChecks`/`IEndpointRouteBuilder` since this is a plain `Microsoft.NET.Sdk` class library, not
  `Microsoft.NET.Sdk.Web`). Once `TreatWarningsAsErrors=true` was added for the packaging convention, NU1510
  (pruned package warning) fired on the *explicit* `PackageReference`s to `Microsoft.Extensions.Diagnostics.HealthChecks`
  and `Microsoft.Extensions.Hosting` — both already ship inside `Microsoft.AspNetCore.App`. Fix: removed
  both explicit `PackageReference`s (not suppressed — they were genuinely redundant).
- Same csproj also hit NU5104 ("stable package must not depend on a prerelease package") because of
  `OpenTelemetry.Instrumentation.EntityFrameworkCore` pinned at `1.11.0-beta.2` — this package has never
  shipped a stable release upstream as of this writing, so there is no version to bump to. Suppressed via
  `<NoWarn>$(NoWarn);NU5104</NoWarn>` with a comment to re-evaluate once OTel ships stable.
- `SharedKernel.MultiTenancy.csproj` hit NU1903 (transitive high-severity advisory on
  `System.Security.Cryptography.Xml` 9.0.0) purely because it references `SharedKernel.Security.Oidc`. This
  is a `12.Security` dependency-tree issue, not something `13.ServiceDefaults` owns or can fix locally —
  suppressed via `NoWarn` with a comment pointing at the upstream package. `12.Security.Oidc.csproj` itself
  has no `TreatWarningsAsErrors`, so it never had to confront this; only domains that add the flag downstream
  of Oidc will hit it.
- General lesson: adding `TreatWarningsAsErrors=true` retroactively to an already-built, already-tested
  package can surface pre-existing NU1xxx/NU5xxx warnings that were always there but silent. Always run a
  full `dotnet build -c Release` immediately after adding the flag, before attempting `dotnet pack` — pack
  surfaces a *different* warning set (NU5104 only shows at pack time, not build time) than build does.

**P-03 consumer-verify pattern for this domain:** mirrors `04.Contracts/consumer-verify` exactly (console
`Exe`, `IsPackable=false`, top-level statements, a `Verify(bool, string)` assertion helper, no test
framework). Lives at `13.ServiceDefaults/consumer-verify/` (sibling to both packages, not nested inside
either). Needs `FrameworkReference Microsoft.AspNetCore.App` itself (to use `WebApplication.CreateBuilder()`
and resolve `RequestDelegate`/`Microsoft.AspNetCore.Http` types) since `AddServiceDefaults()` is an
`IHostApplicationBuilder` extension exercised most realistically via a real `WebApplicationBuilder`.
`ITenantProvider` lives in namespace `SharedKernel.Security.Abstractions.Abstractions` (note the doubled
segment — confirm with `Grep "namespace"` before assuming `SharedKernel.Security.Abstractions` alone
resolves it). Composing `AddSharedKernelMultiTenancy()` requires registering a fake `IDbConnectionFactory`
in the harness first, or `DatabaseTenantResolutionStrategy`'s constructor-injection throws a DI resolution
exception at `app.Services.CreateScope()` time — this is expected/correct behavior (any real consumer using
the Database strategy must register a real factory too), not a bug to work around in the package itself.

**Test counts as of 2026-07-30, end of session (SK.13.Core/Tests/Docs closed `●` a fourth time — C-45/T-40/DO-11,
`WithPersistenceTelemetry`, WO-051/P-326):** 91 `SharedKernel.ServiceDefaults.Tests` passing (+3 from the prior
88), 30 `SharedKernel.MultiTenancy.Tests` passing (unchanged). `WithPersistenceTelemetry` is the **first**
genuinely tracing-only member of the `With*Telemetry` family (all six prior siblings wire both a tracing
source and a meter) — `06.Persistence`'s P-319 phase ships only `PersistenceActivitySource`
(`internal static class`, `ActivitySource("SharedKernel.Persistence", "1.0")`) plus `PersistenceTagKeys`, no
companion `Meter`, so this method makes only a `WithTracing(t => t.AddSource(...))` call, no
`WithMetrics(...)` — do not add one unless `06.Persistence` ships a meter in a future phase. Verified the
blocker-clearing evidence directly: `06.Persistence/state-map.md`'s C-110 confirmed `●` and the real
`internal static class PersistenceActivitySource { public static readonly ActivitySource Source = new("SharedKernel.Persistence", "1.0"); }`
read from `06.Persistence/SharedKernel.Persistence.EfCore/Diagnostics/PersistenceActivitySource.cs` on disk —
matches [[crossdomain_blocking_pattern]]'s established re-verify discipline exactly. Sanity-checked the new
span-capture test genuinely discriminates by temporarily replacing the `AddSource(...)` call with a no-op
lambda, re-running just that test (it failed with an empty captured collection), then restoring — same
technique [[otel_wiring_pattern]] documents for `WithCachingTelemetry`'s T-39 precedent. Root `CLAUDE.md`
*was* touched this time (unlike the WO-043/044/045/046/050 precedents) — one "What Goes Where" row
("Persistence-layer OpenTelemetry trace/metric wiring at host composition") had a stale
"design-locked, queued P-326/WO-051" qualifier that needed removing now that the method shipped; this is the
same qualifier-cleanup pattern `core-phase-implementer` uses for its own WO-049 rows (P-292/P-293/etc.), just
applied here for the first time by this agent. When calling the `sync-brain` skill for a root-level edit like
this, do **not** include a `domain:` field in the args — the skill's own mode detection routes to Sub-domain
mode whenever `domain:` is present, and there is no way to force Root mode through that field; if a root edit
is genuinely needed, just make it directly (Read → Grep the target line → Edit → append changelog) rather than
relying on the skill's dispatch to reach Root mode.
