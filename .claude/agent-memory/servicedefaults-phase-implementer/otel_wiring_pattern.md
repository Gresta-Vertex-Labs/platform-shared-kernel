---
name: otel_wiring_pattern
description: How WithMessagingTelemetry/WithCachingTelemetry wire pre-existing ActivitySource/Meter names into the host without a project reference to the owning domain's concrete type
metadata:
  type: project
---

> WO-086 (2026-09): the base `SharedKernel.ServiceDefaults` now references Foundation-tier packages only, so the concrete-package `ProjectReference`s mentioned below (MassTransit, RabbitMQ options) no longer exist — string-name wiring is now the only possible approach, not just the correct one. The family has grown to eleven `With*Telemetry()` methods.

`WithMessagingTelemetry()` and `WithCachingTelemetry()` (both in `SharedKernel.ServiceDefaults/Telemetry/`)
wire already-existing `ActivitySource`/`Meter` instruments owned by `07.Messaging` and `02.Caching`
respectively into the host's `TracerProvider`/`MeterProvider` — by **string name only**, via
`WithTracing(t => t.AddSource("SourceName"))` and `WithMetrics(m => m.AddMeter("MeterName"))`.

**Why this matters:** these source/meter names belong to classes that are `internal` to their owning
assembly (e.g. `SharedKernel.Messaging.MassTransit.Diagnostics.MessagingDiagnostics` is `internal static`,
no `InternalsVisibleTo` grant to `SharedKernel.ServiceDefaults`). Even if a `ProjectReference` to the
owning concrete package already exists in `SharedKernel.ServiceDefaults.csproj` (for unrelated reasons —
e.g. the RabbitMQ health check needs `RabbitMqBusOptions`), the OTel wiring method still cannot and must
not reference the internal diagnostics type directly. String-name wiring via `AddSource`/`AddMeter` is not
a workaround — it is the only architecturally correct approach, and it is what makes "13.ServiceDefaults
never creates an ActivitySource/Meter on behalf of another domain" actually enforceable: there is no
compile-time coupling to the instrument's declaration, only to its public string name (which is itself
documented in both domains' CLAUDE.md interface contracts as the stable cross-domain handshake).

**How to apply:** when a future phase asks to wire a new domain's `ActivitySource`/`Meter` into the host
(e.g. a hypothetical `WithSearchTelemetry()` for `09.Search`), do not add an `InternalsVisibleTo` grant and
do not reference the owning domain's diagnostics type even if public. Confirm the source/meter *name*
(string) is documented in the owning domain's CLAUDE.md, then wire by name only, mirroring
`CachingTelemetryExtensions.cs`/`MessagingTelemetryExtensions.cs` structurally (private const for the
name(s), idempotency note in XML doc, `ArgumentNullException.ThrowIfNull(builder)` guard, fluent return).

Idempotency for both methods relies on the OTel SDK's own de-duplication of repeated
`AddSource`/`AddMeter` calls with the same name — verified in tests by asserting
`provider.GetServices<TracerProviderBuilder>()`/`GetServices<MeterProviderBuilder>()` count `<= 1` after
calling the extension twice on the same `IHostApplicationBuilder`, not by inspecting instrument-level
state. See [[crossdomain_blocking_pattern]] for how to confirm an upstream domain's instrument has
actually landed before implementing the wiring side.

**Genuine span-capture test pattern (added 2026-07-29, `WithCachingTelemetry`/C-44/T-39):** none of the
five `With*Telemetry` sibling test files (`MessagingTelemetryExtensionsTests`, `SearchTelemetryExtensionsTests`,
`ApplicationTelemetryExtensionsTests`, `IntelligenceTelemetryExtensionsTests`, `WorkflowTelemetryExtensionsTests`)
actually contain an `ActivityListener`-based capture test — despite `state-map.md`/`CLAUDE.md` prose repeatedly
claiming a phase should "mirror" that pattern. They only assert `TracerProviderBuilder`/`MeterProviderBuilder`
DI-registration counts (`<= 1`), never real span capture. Always verify this kind of "mirror the sibling
pattern" claim by reading the actual sibling `.cs` file before writing a new test — the claim was stale/wrong
here. When a phase genuinely requires proving a span is captured end-to-end (not just that `AddSource` was
called), do **not** register a bare `ActivityListener` with a matching `ShouldListenTo` predicate directly in
the test — that captures the span regardless of whether the extension under test wired anything at all,
proving nothing (verified by deliberately breaking the production wiring and confirming a naive
`ActivityListener`-in-test approach would still pass — it would). Instead mirror this same test project's own
`BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` idiom, applied to tracing:
1. Call the `With*Telemetry()` extension on a `WebApplication.CreateBuilder()`.
2. Attach a capturing processor via `builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(new CapturingProcessor(sink)))` — a private nested `sealed class CapturingProcessor(List<Activity> sink) : OpenTelemetry.BaseProcessor<Activity> { public override void OnEnd(Activity data) => sink.Add(data); }`.
3. Resolve `provider.GetRequiredService<TracerProvider>()` — this is required and non-obvious: `BuildServiceProvider()` alone never builds the OTel pipeline (that normally happens inside `TelemetryHostedService` at `IHost.StartAsync()` time, which a unit test never runs); resolving `TracerProvider` directly forces the SDK to build it and attach its listener to the runtime.
4. Create a locally-scoped `new ActivitySource("<the exact production name>", "<version>")` and start/stop an activity from it — no `ProjectReference` to the owning domain needed, since .NET's Activity system matches by source name/version, not instance identity.
5. Assert the sink contains the expected `OperationName`.
Validated this test genuinely discriminates (not trivially green) by temporarily removing the `AddSource` call
from production code and re-running — the test failed with an empty captured collection, confirming it fails
without the fix. Apply this same five-step pattern to any future `With*Telemetry` sibling phase that asks for
a "confirm a span is captured" acceptance criterion.
