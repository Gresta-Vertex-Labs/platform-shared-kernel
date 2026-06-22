---
name: wo028-servicedefaults-audit
description: Gold-standard audit of 13.ServiceDefaults found a broken tenant-strategy extensibility mechanism, a blocking sync DB call, magic strings, and a documented-but-unimplemented health check gap
metadata:
  type: project
---

WO-028 (P-175–P-178) is a hardening pass against `13.ServiceDefaults`'s already-`◐`-dispatched WO-027 Core implementation (C-01–C-18 landed, 41 tests green) — requested directly by the user as a "find bad practices, eliminate magic strings, verify multitenancy" audit, not a new-capability request.

**Why this matters going forward:** this is the first time in the project's history that an audit of *already-shipped, tested* code (not a new-feature request) produced new phases. The lesson: passing tests do not prove an abstraction is correct — see the false-confidence test finding below.

**Findings, in severity order:**

1. **Broken strategy extensibility (P-175).** `TenantResolutionMiddleware.InvokeAsync` identified each `ITenantResolutionStrategy` by switching on `s.GetType().Name` against three hardcoded literals (`"Header"`/`"Claim"`/`"Database"`), then rebuilt a fresh `Dictionary` on every single HTTP request. Consequence: any custom strategy a consuming team registers (the explicit extensibility point the interface exists for) is permanently unreachable from `StrategyOrder` unless its CLR type name happens to match one of three hardcoded strings — no compile error, no runtime signal. The domain's own brain (`13.ServiceDefaults/CLAUDE.md:267`) already documented this as a known test limitation rather than flagging it as a bug. The smoking gun: `TenantResolutionMiddlewareTests.InvokeAsync_StrategyOmittedFromOrder_IsNeverInvoked` "passes" only because its `RecordingStrategy` double's type name can never match any `StrategyOrder` entry regardless of configuration — a false-confidence test that exercises the bug, not the documented behavior. **Lesson for future audits:** when a test class's own doc comment explains why a double was built to be deliberately unreachable, that is a flag to read the production code it's testing, not a reassurance.

2. **Blocking sync call inside async tenant resolution (P-176).** `DatabaseTenantResolutionStrategy.TryResolveAsync` is `async`, accepts a `CancellationToken`, but calls synchronous `IDbCommand.ExecuteScalar()` and never threads the token into the DB call. Thread-pool-blocking on every DB-isolation-strategy request, platform-wide.

3. **Magic strings (P-177), two generations of the same fix needed.** `HealthCheckTags` (tags) was already done correctly as a constants class — but `HealthCheckNames` (default registration *names*: `"redis"`, `"rabbitmq"`, `"azure-service-bus"`, `"cache"`, `"startup"`) was never extended to match it, across 5 files. Also: `AzureServiceBusHealthCheck`'s `"Endpoint="`/`"SharedAccessKey"` connection-string sniff, and `HeaderTenantResolutionStrategy`'s `"X-Tenant-Id"` default header.

4. **Brain/code drift (P-177).** `13.ServiceDefaults/CLAUDE.md` documents `AddDatabaseReadinessCheck<TContext>` and `AddDapperDatabaseReadinessCheck` in full Interface-Contract detail (wrapping 06.Persistence's real, already-shipped P-150 probe primitives — confirmed on disk) — neither adapter was ever implemented. Documentation promised a capability the code never shipped.

5. **Governance backstop (P-178), additive to already-queued P-173.** P-173 (still `◐ Dispatched`, not yet landed) covers tag-integrity + composition-root exclusivity — a different concern. P-178 is a general-purpose "no bare string literal where a sibling constants class already exists" rule, scoped so it generalizes beyond this one domain's two constants classes.

**What was explicitly NOT touched:** C-01–C-18's actual design decisions (live/ready tag split, opt-in-only discipline, `Degraded`-not-`Unhealthy` cache calibration) are correct and were left alone. The already-`◐ Dispatched` P-169–P-173 phases from WO-027 were not duplicated or re-litigated.

**Tracking note:** both `13.ServiceDefaults` and `00.Governance` were already `●` on the Domain Summary Board at audit time — `state-map-phase` was correctly *not* called for either; P-175–P-178 are backlog-only, picked up by `/dispatch-phase`.

**sync-brain note:** skipped — no new technology, package, abstraction split, or root-level hard rule was introduced. This was a defect-fix/hardening WO against an existing, already-documented domain.

See [[project_phase_numbering]] for the updated P-NNN/WO-NNN counter this WO advanced.
