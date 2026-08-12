---
name: decompile_verification_technique
description: How to empirically verify a third-party package's telemetry surface (ActivitySource/Meter names, DI idempotency) via ilspycmd decompilation instead of trusting docs or design notes
metadata:
  type: project
---

This domain's standing "verify against compiled/decompiled source, never assumed" discipline has a
concrete, repeatable technique — used for the first time in this domain on `WithCommunicationTelemetry`
(C-47, WO-056/P-365, 2026-08-12), mirroring `11.Communication`'s P-359 `CallOptionsActions` discovery.

**Tooling:** `ilspycmd` (a `dotnet tool`, confirmed available globally at
`C:\Users\dincd\.dotnet\tools\ilspycmd` in this environment — check `dotnet tool list -g` first).
Usage:
- `ilspycmd -l c "<path-to-dll>"` — lists every class in the assembly (grep for a suspected type name).
- `ilspycmd -t "Full.Type.Name" "<path-to-dll>"` — decompiles one specific type to readable C#.
- NuGet packages live at `~/.nuget/packages/{id-lowercase}/{version}/lib/{tfm}/{Assembly}.dll` — find
  the exact pinned version by reading the top-level package's `.nuspec` (`<dependency id="..." version="..."/>`)
  and walking the dependency chain by hand (e.g. `Microsoft.Extensions.Http.Resilience` →
  `Microsoft.Extensions.Resilience` → `Polly.Extensions` → `Polly.Core`) rather than assuming the
  top-level pin applies to a transitive package.
- A cheap first pass before full decompilation: `grep -a -o 'Keyword[A-Za-z._]*' file.dll | sort -u` —
  IL string-heap literals are extractable as readable ASCII substrings directly from the binary; a
  bare-word match like `"Polly"` appearing as its own standalone string strongly signals it is used as
  an instrument name/logger category, worth confirming with a full decompile of the surrounding type.
- To check "does this assembly reference `ActivitySource`/`Activity` at all" across a whole dependency
  chain: `grep -a -c "ActivitySource" file.dll` / `grep -a -o 'Activity[A-Za-z]*' file.dll` per file — a
  reliable, fast way to falsify an assumed tracing integration before writing any wiring code for it.

**Applied finding (Polly v8.4.2 telemetry shape):** `Polly.Extensions.dll`'s internal
`Polly.Telemetry.TelemetryListenerImpl` creates exactly one instrument,
`internal static readonly Meter Meter = new Meter("Polly", "1.0")`, plus an `ILogger` category also
named `"Polly"` (`options.LoggerFactory.CreateLogger("Polly")`). It creates **no `ActivitySource`** —
confirmed by zero `"ActivitySource"`/`"Activity"` string matches across the complete pinned chain
(`Polly.Core.dll`, `Polly.Extensions.dll`, `Polly.RateLimiting.dll`, `Microsoft.Extensions.Resilience.dll`,
`Microsoft.Extensions.Http.Resilience.dll`, `Microsoft.Extensions.Http.Diagnostics.dll`). Retry/circuit-
breaker/timeout events are reported via metrics + logs only; the actual HTTP/gRPC request span still
comes from `OpenTelemetry.Instrumentation.Http`/`.GrpcNetClient`. **Do not assume Polly emits a
`"Polly"`-named `ActivitySource` just because it emits a `"Polly"`-named `Meter`/logger category** — this
was the exact wrong assumption the servicedefaults-arch-planner's original D-18 design made, corrected
in Core under the phase's own explicit reconfirm-before-shipping gate. If a future Polly major version
adds tracing, re-verify before adding `WithTracing(AddSource("Polly"))` — do not add it speculatively.

**Applied finding (`.AddGrpcClientInstrumentation()` idempotency, `OpenTelemetry.Instrumentation.GrpcNetClient
1.15.1-beta.1`):** unlike a bare `AddSource(string)`/`AddMeter(string)` call, this is an instrumentation-
factory registration and does NOT automatically inherit the OTel SDK's by-name dedup. Confirmed
dedup-safe anyway by decompiling both the instrumentation package and
`OpenTelemetry.Api.ProviderBuilderExtensions` (1.16.0): the extension calls
`services.TryAddSingleton<GrpcClientInstrumentation>()` (DI-deduped to one instance regardless of call
count); that singleton's constructor performs the one side effect,
`DiagnosticSourceSubscriber.Subscribe()` to the `"Grpc.Net.Client"` `DiagnosticListener`, which is itself
internally guarded (`if (allSourcesSubscription == null)`); per-call span enrichment
(`GrpcClientDiagnosticListener`) tags `Activity.Current` rather than starting its own `Activity`, so with
only one subscriber ever attached there is no duplicate/double-tagged span possible; and
`DiagnosticSourceSubscriber.Dispose()` is separately idempotent (`Interlocked.CompareExchange`-guarded)
for the residual case of the same singleton being registered more than once in the
`TracerProviderBuilder`'s bookkeeping list. **Generalizable technique:** before assuming a custom
static-flag idempotency guard is needed for a `With*Telemetry`-family method wiring a genuine
instrumentation-factory extension (not a bare `AddSource`/`AddMeter` string call), decompile the actual
installed package first to check whether its DI registration uses `TryAddSingleton`/is otherwise
self-guarding — many OTel instrumentation packages are, by convention, since they're designed to be
composed by multiple callers in a DI graph.

See also [[otel_wiring_pattern]] for the sibling string-name-only wiring pattern this technique
complements (used when the source IS SharedKernel-owned and internal), and
[[crossdomain_blocking_pattern]] for the parallel "verify against ground truth, not the dispatch note"
discipline applied to cross-domain contracts rather than third-party packages.
