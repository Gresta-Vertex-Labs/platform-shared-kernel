---
name: feedback-logging-retrofit-test-patterns
description: How to test a [LoggerMessage] retrofit's EventId/Level and verify SK0020/SK0021 zero-diagnostics against real assemblies, discovered during 11.Communication WO-041 T-27/T-28
metadata:
  type: feedback
---

When asked to write regression tests proving a `[LoggerMessage]` retrofit preserved EventId/Level and to run
00.Governance's SK0020/SK0021 analyzer + `LoggingEventIdIntegrityAssertion` against real (not contrived)
assemblies, use these techniques rather than inventing new ones — they are the established cross-domain
precedent (already used by `SharedKernel.Application.Pipeline.Tests` and `SharedKernel.Messaging.MassTransit.Tests`).

**Why:** re-derived from scratch during 11.Communication T-27/T-28 by reading those two domains' actual test
files via a research subagent — do not skip that step next time; grep first for `LoggingEventIdIntegrityAssertion`
usages outside `00.Governance` before assuming a pattern needs to be invented.

**How to apply:**

1. **EventId/Level regression (T-27-shaped tasks):** reflection over the compiled type is the deterministic,
   maintainable check — `type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static).GetCustomAttribute<LoggerMessageAttribute>()`,
   then assert `.EventId`/`.Level`. Do NOT rely solely on forcing the real exception/log-call path at runtime —
   several call sites (DNS-failure fallback, stale-cache paths, SRV/A-record-lookup-failed) may not be
   reachable through the test project's own fixtures (e.g. a pass-through service-discovery provider that
   always succeeds) without a fault-injectable dependency. Combine reflection (covers every site, always
   passes/fails deterministically) with a runtime capturing-`ILogger<T>`/`TestLogSink` assertion for whichever
   call sites ARE reachable, for the strongest possible regression net.

2. **Real-assembly `LoggingEventIdIntegrityAssertion` (T-28-shaped tasks):** add a **test-project-only**
   `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` (never the production `.csproj`).
   Call `LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(new Dictionary<Assembly,(int,int)> { [typeof(SomeRealType).Assembly] = (RangeMin, RangeMax) })`
   with the range sourced from `SharedKernel.Primitives.Logging.LoggingEventIdRanges`. Bump `FluentAssertions`
   to whatever `SharedKernel.ArchitectureTests` itself pins (8.10.0 as of WO-041) — a lower pin triggers NU1605.
   **Direction trap:** if two sibling packages in the domain must be checked together for cross-assembly
   collisions, anchor the combined test in whichever package's test project is ALREADY permitted to reference
   the other in production (check the declared adapter edges first — here `Rest`/`Grpc` → `Communication.Internal`,
   never the reverse) — never add the reverse test-only reference, even though "it's just a test."
   **Internal-type trap:** if the type you need `.Assembly` from is `internal`, anchor on a public sibling type
   in the same assembly instead of trying to gain `InternalsVisibleTo` access from a foreign test project.

3. **SK0020/SK0021 zero-diagnostics verification:** do NOT try to wire `SharedKernel.Analyzers` as a permanent
   analyzer reference on the production `.csproj` — it's a **build-then-revert** verification step. Temporarily
   add `<ProjectReference Include="...SharedKernel.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
   to the production `.csproj`, build with `dotnet build ... -p:TreatWarningsAsErrors=false` (the domain's own
   `TreatWarningsAsErrors=true` would otherwise turn ANY analyzer diagnostic — including unrelated pre-existing
   ones like SK0001/SK0011 — into a build-breaking error before you can even see the SK0020/SK0021 result), grep
   the warning list for `SK0020`/`SK0021`, then revert the `.csproj` edit and confirm a normal rebuild is clean
   (0 warnings/0 errors). Pre-existing, unrelated analyzer findings surfaced during this process are out of
   scope for a logging-authoring retrofit — flag them as a candidate follow-up, do not fix them inline.
