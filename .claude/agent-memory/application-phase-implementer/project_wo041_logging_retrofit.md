---
name: project-wo041-logging-retrofit
description: WO-041 (P-253) [LoggerMessage] authoring retrofit for SharedKernel.Application.Behaviors — key fixes and pitfalls
metadata:
  type: project
---

> WO-086 (2026-09): SharedKernel.Application.Behaviors is SharedKernel.Application.Pipeline; the FireAndForget and StreamLoggingBehavior files were deleted (P-544), so the DropOldest quirk is gone. The IsEnabled, internal-type ILogger<T> and Mono.Cecil lessons still hold.

Completed 2026-07-10 in one session (Design through Tests, D-66..D-71/S-19/C-69..C-74/T-60..T-66 all ●).

## Stale-state lesson (important, recurring pattern in this repo)

A prior session's prose ("Design locked") did NOT match the actual task table (still `○`). Always
verify task-table state directly — do not trust "Design locked"/"phase complete" prose in
CLAUDE.md or Active Work notes without cross-checking the phase's own task rows. Also cross-check
the Overall Progress *totals* — they can silently undercount when new tasks are added to a phase's
task table but the Total/Done numbers in the summary table aren't updated to match (found `65/65`
claimed-complete when 71 actual rows existed, 6 still `○`).

## [LoggerMessage] + NSubstitute pitfall (extends the known LoggerMessage.Define pitfall)

`[LoggerMessage]`-generated partial methods check `ILogger.IsEnabled(LogLevel)` before calling
`Log(...)` — exactly like hand-written `LoggerMessage.Define` delegates. NSubstitute's
`ILogger<T>` mock defaults `IsEnabled()` to `false`, silently no-oping every log assertion after
converting direct `logger.LogInformation(...)` calls to `[LoggerMessage]` methods. Fix: add
`logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);` right after `Substitute.For<ILogger<T>>()`
at every affected call site. This is NOT a workaround for a bug — it's required because the
authoring-mechanism swap changes when `IsEnabled` gets called.

## Testing an internal type's ILogger<T> in another assembly

`ChannelFireAndForgetDispatcher` is `internal`. The test project cannot name
`ILogger<ChannelFireAndForgetDispatcher>` at compile time. Solution: register the OPEN generic
`ILogger<>` → a public generic `RecordingLogger<T>` (defined in the test project, never naming the
internal type) that writes into a shared static sink keyed by `typeof(T).Name` (captured from
inside the generic class body itself — no compile-time reference to the internal type needed). DI
resolves `ILogger<ChannelFireAndForgetDispatcher>` by constructing the closed generic at runtime via
ordinary generic-service resolution — CLR accessibility restricts compile-time references, not
runtime generic instantiation.

## Mono.Cecil IL-shape test for "no direct ILogger.Log calls" must exclude generator output

The `[LoggerMessage]` source generator's own emitted code legitimately calls
`Microsoft.Extensions.Logging.LoggerMessage.Define<...>(...)` — from the type's implicit static
constructor (`.cctor`), assigning the result into a `[GeneratedCodeAttribute]`-marked cache field
(`__LogXxxCallback`). Verified empirically via a temporary Mono.Cecil attribute dump (dump every
field's/method's CustomAttributes) — do this BEFORE writing an IL-shape assertion for logging
authoring style; don't assume which generated-code marker is present where. The generated
`LogXxx` partial method itself (with `[LoggerMessageAttribute]` + `[GeneratedCodeAttribute]`) does
NOT call `ILogger.Log` directly in this SDK's generator version — it invokes the cached delegate's
`.Invoke(...)`. So an IL-shape test checking for `ILogger.Log`/`LoggerExtensions.Log*` calls can
simply exclude methods carrying `[GeneratedCodeAttribute]`/`[CompilerGeneratedAttribute]`; but a
test checking for `LoggerMessage.Define*` calls must specifically pair each `Call` instruction with
the immediately-following `Stsfld` and check if THAT field carries `[GeneratedCodeAttribute]` —
excluding the whole `.cctor` method is too broad (a genuinely hand-rolled field initializer in the
same `.cctor` would be missed) but excluding on generated-field-target is precise and correct.

## Pre-existing (not-my-bug) latent quirk found in ChannelFireAndForgetDispatcher

`AddFireAndForgetDispatch()`'s channel construction: `FullMode = DropAndLog ? DropOldest : Wait`.
Under `BoundedChannelFullMode.DropOldest`, `TryWrite` never returns `false` when full — it drops
the oldest item and succeeds instead. This means the `DropAndLog` policy's own `LogChannelFull`
warning branch in `ChannelFireAndForgetDispatcher.EnqueueAsync` (`if (writer.TryWrite(command))
return; <log>`) is UNREACHABLE through the real, documented wiring as shipped. Confirmed by writing
a test through the real `AddFireAndForgetDispatch()` path and observing zero log records. Worked
around in the test by overriding the `Channel<IFireAndForgetCommand>` singleton (last registration
wins) with `BoundedChannelFullMode.Wait` to exercise the log call site directly — did NOT fix the
production wiring, since this WO-041 phase is an authoring-mechanism retrofit only, not a
behavioral fix. Flagged in `05.Application/CLAUDE.md` and state-map.md as a candidate follow-up
functional-bug work order for a future session/arch-lead review.

## Docs phase (DO-22..DO-24, completed 2026-07-10, follow-up session)

Private `[LoggerMessage]`-attributed partial methods don't need XML docs to satisfy the
`GenerateDocumentationFile`/`TreatWarningsAsErrors` CS1591 gate (CS1591 only fires on
publicly-visible members) — a clean build does NOT by itself prove "100% XML doc coverage" on
these methods if the phase spec explicitly calls them out. Read the actual task wording: if it
names specific private members, add doc comments to them even though the compiler wouldn't force
it — the build-gate argument only covers what's actually enforced.

Also re-confirmed the stale-prose pattern from the D-66..D-71 correction: this file's own retrofit
NOTE blocks in `CLAUDE.md` (`IFireAndForgetDispatcher`, `FireAndForgetBackgroundConsumer`,
`StreamLoggingBehavior`) carried a "shipped 2026-07-10" tag but the prose body still used
forward/present-tense phrasing ("becomes a [LoggerMessage]...", "is replaced with...") — read as
design-intent language despite the shipped-date callout. Fixed to definite past tense ("is now a
[LoggerMessage]...", "have been replaced with..."). Lesson: a "shipped" tag on a paragraph doesn't
guarantee the paragraph's own verb tense agrees with it — grep for the tag AND read the surrounding
sentence when sweeping for stale "design-only" language.

## Files touched this session

- `05.Application/SharedKernel.Application.Behaviors/Shared/ApplicationBehaviorsLoggingEventIds.cs` (new)
- `Logging/LoggingBehavior.cs`, `FireAndForget/FireAndForgetBackgroundConsumer.cs`,
  `FireAndForget/ChannelFireAndForgetDispatcher.cs`, `Streaming/StreamLoggingBehavior.cs` (converted to partial + [LoggerMessage])
- New tests: `Logging/LoggingBehaviorEventIdTests.cs`, `FireAndForget/FireAndForgetLoggingEventIdTests.cs`,
  `Governance/LoggingAuthoringStyleShapeTests.cs`, `Governance/LoggingEventIdIntegrityRealAssemblyTests.cs`
- Existing tests patched: `Logging/LoggingBehaviorTests.cs`, `LoggingBehaviorFailureLevelTests.cs`,
  `LoggingBehaviorLoggableRequestTests.cs` (IsEnabled fix), `Streaming/StreamLoggingBehaviorTests.cs`
  (RecordingLogger now captures EventId, new assertions for 5120-5123)
