---
name: baggage_log_record_processor
description: Test pattern and implementation notes for BaggageLogRecordProcessor and AddSharedKernelTelemetry's .WithLogging(...) registration (WO-041/P-251)
metadata:
  type: project
---

> WO-086 (2026-09): `TenantBaggageKeys` was deleted (key: `WellKnownBaggageKeys.TenantId`); the inbound correlation id is now set by `app.UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`), not a `14.Presentation` middleware. The processor and test pattern are unchanged.

`BaggageLogRecordProcessor` (`SharedKernel.ServiceDefaults/Telemetry/BaggageLogRecordProcessor.cs`)
is a sealed `OpenTelemetry.BaseProcessor<OpenTelemetry.Logs.LogRecord>` that copies every
`System.Diagnostics.Activity.Current?.Baggage` entry onto `LogRecord.Attributes` at `OnEnd` time,
skipping any key already present (explicit attributes always win). It carries no hardcoded key
names — this is what lets it generically surface both `14.Presentation`'s CorrelationId baggage
(WO-031) and `SharedKernel.MultiTenancy`'s `WellKnownBaggageKeys.TenantId` baggage with zero
cross-domain `ProjectReference`.

`AddSharedKernelTelemetry` wires it via `.WithLogging(logging => logging.AddProcessor<BaggageLogRecordProcessor>().AddOtlpExporter(), options => { options.IncludeScopes = true; options.IncludeFormattedMessage = true; })`
— additive inside the existing method signature, no new public extension method.

**Test pattern (no test double package needed — plain BCL + OTel SDK):** build a real
`LoggerFactory.Create(builder => builder.AddOpenTelemetry(options => options.AddProcessor(new BaggageLogRecordProcessor()).AddProcessor(new CapturingProcessor(sink))))`.
The `CapturingProcessor` is a second custom `BaseProcessor<LogRecord>` that copies
`data.Attributes` into a side `List<...>` **at `OnEnd` time** — never retain the `LogRecord`
reference itself, since the OTel SDK may pool/reset `LogRecord` instances after the pipeline
finishes. `Activity.Current` doesn't need an `ActivityListener`/`ActivitySource` — `new Activity(name).Start()`
(the legacy Activity API) sets `Activity.Current` and supports `SetBaggage`/`Baggage` regardless of
sampling/listener state.

**Testing `AddSharedKernelTelemetry`'s logging options:** resolve
`IOptions<OpenTelemetryLoggerOptions>` from the built `IServiceProvider` and assert
`.Value.IncludeScopes`/`.IncludeFormattedMessage`. Assert the processor is registered exactly once
via `provider.GetServices<BaggageLogRecordProcessor>().Count()` (TryAddSingleton-backed
`AddProcessor<T>()` registration).

**Test-only cross-package reference precedent:** `SharedKernel.ServiceDefaults.Tests` took a
test-only `ProjectReference` to the sibling `SharedKernel.MultiTenancy.csproj` (added to
`AmbientLoggingEnrichmentAcceptanceTests`'s `.csproj`) to run the real `TenantResolutionMiddleware`
end-to-end alongside a BCL-simulated `CorrelationId` baggage entry, proving both compose on one
`LogRecord` without collision. This is acceptable — same domain, test-only, never shipped — and
mirrors how the CLAUDE.md rules explicitly allow simulating `14.Presentation`'s mechanism directly
via BCL calls rather than taking a real reference to that package.

See [[otel_wiring_pattern]] for the sibling `WithMessagingTelemetry`/`WithCachingTelemetry`
string-name wiring convention (a related but distinct mechanism — those wire pre-existing
ActivitySource/Meter names by string; this processor works on `Activity.Baggage`, not sources).
