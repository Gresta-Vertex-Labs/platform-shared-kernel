---
name: masstransit-otel-instrumentation
description: MessagingDiagnostics.ActivitySource/Meter wiring (SK.07.OTel/P-172, SK.07.DiagnosticsCoverage/P-348), full dispatch-verb Activity+Meter coverage, ActivityListener/MeterListener parallel-test-isolation hazard
metadata:
  type: project
---

> WO-086 (2026-09): `RequestAsync` and `ExecuteRoutingSlipAsync` (and their `MessageBus.Request`/`MessageBus.ExecuteRoutingSlip` activities) were removed by P-560; `WithMessagingTelemetry()` lives in the `SharedKernel.ServiceDefaults` base (the `.Messaging` integration package was deleted). Platform pinned to MassTransit 8.5.x.

## MessagingDiagnostics.ActivitySource (SK.07.OTel, P-172)

**Location:** `07.Messaging/SharedKernel.Messaging.MassTransit/Diagnostics/MessagingDiagnostics.cs` — `internal static class` with `public static readonly ActivitySource ActivitySource = new("SharedKernel.Messaging", "1.0.0")`. Also exposes `SourceName`/`SourceVersion` consts so `13.ServiceDefaults.WithMessagingTelemetry()` (P-132, downstream) can reference the literal without re-deriving it.

**Why this is the one sanctioned static field in the domain:** a single process-lifetime `ActivitySource` is the platform-standard .NET diagnostics pattern (same shape as a static logger category or a `Meter`). It carries no mutable business state. This exception is documented explicitly in `07.Messaging/CLAUDE.md` hard-violations list so a future reviewer doesn't flag it as a "no static mutable state" violation.

**Ownership boundary:** `07.Messaging` owns and constructs this source. `13.ServiceDefaults` must never construct an `ActivitySource`/`Meter` on behalf of another domain — it only calls `.AddSource("SharedKernel.Messaging")` on the host's `TracerProviderBuilder`. This was a latent cross-domain violation in the original P-132 spec (which assumed the source already existed) — P-172 was split out specifically to fix that and give P-132 a true dependency instead of a false assumption.

## Instrumentation points

**`ConsumerBase<TMessage>.Consume()`:** `using var activity = MessagingDiagnostics.ActivitySource.StartActivity("Consumer.Consume");` at the very top, tagged `messaging.message_type = typeof(TMessage).Name`. The `using` wraps the entire method body (log scope build + try/catch/rethrow), so the activity is disposed after `ConsumeAsync` completes or throws — exactly the same lifetime as the existing log scope. Log scope additionally gets `messaging.message_type` and (when `context.DestinationAddress` is non-null) `messaging.destination = context.DestinationAddress.AbsolutePath`. These are additive to the existing CorrelationId + `x-sk-*` header scope keys from [[masstransit-header-propagation]].

**`MassTransitEventPublisher`:** the activity must go in the **instance** method `PublishEnvelopeAsync<TEvent>` (which has the closed `TEvent` type available), not in the `static` `BuildPublisher<TEvent>`/`PublishEnvelope<TEvent>` helpers built via the `ConcurrentDictionary<Type, PublishDelegate>` cache (see [[masstransit-idempotency-filter-wiring]] sibling note on that cache pattern) — those helpers exist purely for the `IDomainEvent` constraint bridge and run after the type-erasure boundary. Tag with `messaging.event_type = eventType.Name` **before** the `IDomainEvent` runtime guard throws, so the activity (and its tag) is still correctly populated even on the `InvalidOperationException` throw path for non-domain-event types. `PublishEnvelopeAsync` had to become `async`/`await` (previously returned the inner `Task` directly) so the `using var activity` disposes only after the publish call actually completes, not when the `Task` is merely created.

## ActivityListener parallel-test-isolation hazard (discovered writing OT-05/OT-06)

`ActivitySource.AddActivityListener(listener)` is **process-wide** — not scoped to the test class or method. xUnit runs test classes in parallel by default within a collection. Other test classes in the same assembly that also exercise `ConsumerBase<T>.Consume()` or `MassTransitEventPublisher.PublishAsync<T>()` (e.g. `ConsumerBaseTests`, `HeaderPropagationTests`, `ResilienceTests`, `BatchConsumerTests` — anything touching the consume/publish pipeline) emit their own activities on the same `"SharedKernel.Messaging"` source while your listener is attached, even though they're unrelated to what you're testing.

**Symptom:** `capturedActivities.Should().ContainSingle(a => a.OperationName == "Consumer.Consume")` fails intermittently (or consistently, depending on test execution order/timing) because it captures activities from concurrently-running unrelated tests, not just your own.

**Fix:** always filter by the test's own unique tag value in addition to `OperationName` — e.g. `a.OperationName == "Consumer.Consume" && Equals(a.GetTagItem("messaging.message_type"), nameof(MyUniqueTestMessage))`. Never assert global singularity by operation name alone when other tests in the same assembly drive the same instrumented code path. This is documented as a Test Rules bullet in `07.Messaging/CLAUDE.md` now — check there before writing new OTel-adjacent tests in this package.

**Established `ActivityListener` test harness shape** (mirrors the prior art in `02.Caching/SharedKernel.Caching.Redis.PubSub` — `CacheInvalidationIntegrationTests.cs`'s `HandleMessage_CreatesOTelActivity_WithExpectedTags`): subscribe `ShouldListenTo = source => source.Name == SourceName`, `Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded`, capture via `ActivityStopped` (not `ActivityStarted` — stopped guarantees the activity's full tag set and duration are finalized) into a `List<Activity>` + signal a `TaskCompletionSource<bool>` so the test can `await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(5))` instead of guessing a sleep duration.

## Full dispatch-surface coverage + Meter (P-348/WO-054, SK.07.DiagnosticsCoverage)

`MessagingDiagnostics` gained a companion static `Meter("SharedKernel.Messaging","1.0.0")`
alongside `ActivitySource`, under the identical static-instrument exception. Five instruments:
`PublishCounter`/`ConsumeCounter`/`ConsumeDurationHistogram`/`RetryCounter`/`FaultCounter`
(`Counter<long>`/`Histogram<double>`, `System.Diagnostics.Metrics`, BCL).

`MassTransitMessageBus.SendAsync<T>()`/`.RequestAsync<TRequest,TResponse>()`/
`.ExecuteRoutingSlipAsync()` were the only three dispatch verbs with zero `Activity` coverage
before this phase — each now starts/disposes `"MessageBus.Send"`/`"MessageBus.Request"`/
`"MessageBus.ExecuteRoutingSlip"` via the same `using var activity = ...StartActivity(...)`
shape as `Consume`/`EventPublisher.Publish`. `MassTransitMessageBus.PublishAsync<T>` did NOT
gain an `Activity` (only `SendAsync`/`RequestAsync`/`ExecuteRoutingSlipAsync` had the
completeness gap) — it gained only a `Meter` counter increment.

Retry-counter wiring required a real MassTransit-API investigation — see
[[masstransit-retry-observability-gap]] for the full story: `IRetryObserver`/
`IRetryObserverConnector` exist but are unreachable in 9.1.2; `ConsumeContext.GetRetryAttempt()`
is the verified working alternative, checked unconditionally at the top of `Consume()`.

**`MeterListener` test pattern** (parallel to the `ActivityListener` pattern above — same
process-wide hazard, same unique-tag-value mitigation): `listener.InstrumentPublished = (i, l) =>
{ if (i.Meter.Name == "SharedKernel.Messaging") l.EnableMeasurementEvents(i); };
listener.SetMeasurementEventCallback<long>(...); listener.SetMeasurementEventCallback<double>(...);
listener.Start();` — `Start()` replays already-published instruments, so it correctly picks up
the static `Meter`'s instruments even though they were published before the listener existed.
No `ActivityListener`-style `Sample`/`ShouldListenTo` needed — `MeterListener` only needs
`InstrumentPublished` to opt in per-instrument.

**Publish-counter success-only semantics required making previously-fire-and-return methods
`async`:** `MassTransitEventPublisher`'s static `PublishEnvelope<TEvent>` and both
`MassTransitMessageBus.PublishAsync<T>` overloads used to `return publishEndpoint.Publish(...)`
directly (no `await` inside the method). To increment `PublishCounter` only *after* the publish
genuinely succeeds (not merely after the `Task` is constructed), all three became `async Task`
with an internal `await ... .ConfigureAwait(false)` followed by the counter increment.

## Test file location and conventions followed

`07.Messaging/SharedKernel.Messaging.MassTransit/SharedKernel.Messaging.MassTransit.Tests/HarnessTests/OTelInstrumentationTests.cs` — 9 tests (OT-05 consumer activity, OT-06 publisher activity + throw-path activity-still-disposed variant, OT-07 log-scope destination/message_type + null-safety sanity check). Followed existing conventions: `internal sealed` message/consumer types (never `file` — see [[masstransit-testing-patterns]]), `NullLogger.Instance` for consumers that don't need scope assertions, a dedicated capturing `ILogger` + static `ConcurrentBag`-backed store (reset before each test) for scope-key assertions — same shape as `HpCapturingLogger`/`HpLogScopeCaptureStore` in `HeaderPropagationTests.cs`.

**Why:** This was the first `ActivitySource`/`ActivityListener` test in `07.Messaging` — no prior art existed in this package (only in `02.Caching.Redis.PubSub`), so the pattern is now established here for any future instrumentation phase in this domain.
