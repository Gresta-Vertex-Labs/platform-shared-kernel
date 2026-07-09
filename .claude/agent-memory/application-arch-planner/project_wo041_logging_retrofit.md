---
name: project_wo041_logging_retrofit
description: WO-041 P-253 logging-authoring retrofit for 05.Application — EventId allocation table, exhaustive file list, cross-domain blockers
metadata:
  type: project
---

WO-041 P-253 ("Application: Logging Retrofit to the Platform `[LoggerMessage]` Standard") was dispatched
2026-07-09, Design locked (D-66..D-71), Core/Tests/Docs/Published all `○` Pending in `05.Application/state-map.md`.

**Why:** root `CLAUDE.md`'s WO-041 "Logging Conventions" section (added 2026-07-08) mandates `[LoggerMessage]`-only
authoring platform-wide, enforced by `00.Governance` SK0020/SK0021 (P-250) and `LoggingEventIdIntegrityAssertion`,
against `01.Core`'s new `SharedKernel.Primitives.Logging.LoggingEventIdRanges` registry (P-249, domain base
`{folder number}*1000`, `Application` = 5000).

**Exhaustive file list needing retrofit (confirmed by grep, 2026-07-09) — all four live in
`SharedKernel.Application.Behaviors`, none in `SharedKernel.Application` itself (zero `ILogger` usage there):**
- `Logging/LoggingBehavior.cs` — 4 direct `ILogger.LogInformation/LogWarning/LogError` calls
- `FireAndForget/FireAndForgetBackgroundConsumer.cs` — 1 direct `LogError` call
- `FireAndForget/ChannelFireAndForgetDispatcher.cs` — 1 direct `LogWarning` call
- `Streaming/StreamLoggingBehavior.cs` — 4 hand-written `LoggerMessage.Define<>()` static delegate fields at
  ad hoc `EventId(1..4, "Name")`, outside any reserved range

**Locked EventId allocation (sub-block convention: 100-wide per package in domain declaration order within
the domain's 1000-wide block):**
- `SharedKernel.Application` = 5000-5099 (reserved, unused today)
- `SharedKernel.Application.Behaviors` = 5100-5199
  - `LoggingBehavior` 5100-5109: 5100 LogHandling(Info), 5101 LogHandledSuccess(Info), 5102 LogHandledFailure(Warning), 5103 LogHandlingFailed(Error)
  - `FireAndForget` 5110-5119: 5110 LogChannelFull(Warning, in `ChannelFireAndForgetDispatcher`), 5111 LogCommandFaulted(Error, in `FireAndForgetBackgroundConsumer`)
  - `StreamLoggingBehavior` 5120-5129: 5120 StreamStarted(Info), 5121 FirstItem(Debug), 5122 StreamCompleted(Info), 5123 StreamFaulted(Warning) — renumbered off the old ad hoc 1-4
- New constants class: `Shared/ApplicationBehaviorsLoggingEventIds.cs` (`internal static class`, ten `const int`
  fields = `LoggingEventIdRanges.Application + offset`)

**Explicit non-goal:** message templates/levels/named placeholders and the `ILoggableRequest<TResponse>`
`BeginScope` opt-in redaction mechanism (WO-040, P-246) are unchanged — authoring-mechanism-only retrofit.

**Cross-domain blockers for Core/Tests (check status before continuing this phase):**
- `01.Core` P-249 (`SK.01.P249` in `01.Core/state-map.md`) — `LoggingEventIdRanges` implementation. Was `○` 0/4
  as of 2026-07-09 (though `01.Core/CLAUDE.md` already fully documents the shipped-shape design — the actual
  code/tests are what's pending).
- `00.Governance` P-250 (`SK.00.LoggingStandardEnforcement` in `00.Governance/state-map.md`) —
  `LoggingAuthoringStyleAnalyzer` (SK0020/SK0021) + `LoggingEventIdIntegrityAssertion`. Was `○` 0/9 as of
  2026-07-09.
- The `SharedKernel.Application.Behaviors.Tests` → `00.Governance/SharedKernel.ArchitectureTests`
  test-project-only `ProjectReference` already exists (S-17, from WO-039 P-241) — no new project reference
  needed for T-65's real-assembly `LoggingEventIdIntegrityAssertion` invocation, only the upstream
  implementation.

See [[pattern_local_seam_bridging]] for this domain's other established cross-domain-dependency-tracking
convention (mark Design tasks completable now against documented shapes; Core/Tests genuinely wait).
