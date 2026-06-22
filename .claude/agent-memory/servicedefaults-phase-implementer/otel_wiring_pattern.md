---
name: otel_wiring_pattern
description: How WithMessagingTelemetry/WithCachingTelemetry wire pre-existing ActivitySource/Meter names into the host without a project reference to the owning domain's concrete type
metadata:
  type: project
---

`WithMessagingTelemetry()` and `WithCachingTelemetry()` (both in `SharedKernel.ServiceDefaults/Telemetry/`)
wire already-existing `ActivitySource`/`Meter` instruments owned by `07.Messaging` and `02.Caching`
respectively into the host's `TracerProvider`/`MeterProvider` — by **string name only**, via
`WithTracing(t => t.AddSource("SourceName"))` and `WithMetrics(m => m.AddMeter("MeterName"))`.

**Why this matters:** these source/meter names belong to classes that are `internal` to their owning
assembly (e.g. `SharedKernel.Messaging.MassTransit.Diagnostics.MessagingDiagnostics` is `internal static`,
no `InternalsVisibleTo` grant to `SharedKernel.ServiceDefaults`). Even when a `ProjectReference` to the
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
