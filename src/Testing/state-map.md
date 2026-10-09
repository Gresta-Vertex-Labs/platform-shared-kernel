# 16.Testing — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

Every package is Testing tier and referenced only by test projects (`TestingNeverReferencedByProduction`). Each `SharedKernel.{Capability}.Testing` references only its capability's contracts. Split out of the former monolithic `SharedKernel.Testing` by WO-086 (P-571).

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Testing` | Testing | ● | Lightweight packable core (Foundation + Model references only): `FakeClock`, in-memory `ILogger`/`ILoggerFactory`, `TestRequestContext`, fakers, assertions, `CultureScope`, `ActivityRecorder`. Telemetry capture and assertions planned (`SK.16.TelemetryCapture`). |
| `SharedKernel.Testing.Internal` | Testing | ● | Not packable. Testcontainers fixtures (PostgreSQL, Redis, RabbitMQ, MinIO, Meilisearch, Elasticsearch, Qdrant), EF Core/Npgsql/audit helpers, MassTransit `TestHarnessFactory` — this repo's own tests only. |
| `SharedKernel.AI.Testing` | Testing | ● | In-memory embedding generator, vector collection and kernel doubles. |
| `SharedKernel.Application.Testing` | Testing | ● | `ApplicationPipelineTestHarness.Build()` and local-seam doubles for the pipeline. |
| `SharedKernel.Caching.Testing` | Testing | ● | `FakeCacheService`, `FakeTenantCacheService`, fake lock service, `AddFakeCachingServices()`. |
| `SharedKernel.Caching.Redis.Testing` | Testing | ● | Fake Redis channel/hash/typed-hash services, `AddFakeRedisServices()`. |
| `SharedKernel.Communication.Testing` | Testing | ● | `StubHttpMessageHandler` + `UseStubHttpMessageHandler(clientName, stub)`, `GrpcCalls`. |
| `SharedKernel.Cryptography.Testing` | Testing | ● | Fake symmetric (AAD-enforcing), async key provider, envelope provider, remote key provider, hasher, TOTP doubles; `AddFakeCryptography()`. |
| `SharedKernel.FeatureManagement.Testing` | Testing | ● | `FakeFeatureClient`. |
| `SharedKernel.Idempotency.Testing` | Testing | ● | `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)`. |
| `SharedKernel.Integration.Testing` | Testing | ● | `InMemoryWebhookDispatcher`, `InMemoryNotificationSender`. |
| `SharedKernel.Messaging.Testing` | Testing | ● | `InMemoryMessageBus`, `InMemoryEventPublisher` and their DI extension. |
| `SharedKernel.Persistence.Testing` | Testing | ● | `AddFakeRepository<T,TId>()`, `AddFakeUnitOfWork()` (`TransientFailures`), `AddFakeAuditTrailWriter()`, `AddFakeCrossTenantScope()`, `FakeDbConnectionFactory`, `PostgresTestServer`/`PostgresTestDatabase`. |
| `SharedKernel.Presentation.Testing` | Testing | ● | HTTP, gRPC and GraphQL test helpers. |
| `SharedKernel.Reporting.Testing` | Testing | ● | `InMemoryReportExporter`/`InMemoryReportExporterFactory`/`InMemoryHtmlToPdfConverter` + `AddInMemoryReporting()`. |
| `SharedKernel.Scheduling.Testing` | Testing | ● | `InMemoryScheduledJobRegistry`. |
| `SharedKernel.Search.Testing` | Testing | ● | `InMemorySearchIndex<T>` (incl. bulk-write throttle), provisioner and descriptor doubles. |
| `SharedKernel.Security.Testing` | Testing | ● | `FakeUserContext`, `SecurityTestContextBuilder`, test certificates, DPoP proofs. |
| `SharedKernel.ServiceDefaults.Testing` | Testing | ● | `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog`. |
| `SharedKernel.Storage.Testing` | Testing | ● | `InMemoryFileStorage` via `AddInMemoryStore(name)`. |
| `SharedKernel.Workflows.Testing` | Testing | ● | `InMemoryWorkflowDispatcher`. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.16.TelemetryCapture` | Telemetry capture and assertion helpers in the core: spans, metrics, log conventions (WO-087 / P-581) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.16.TelemetryCapture — Telemetry capture and assertion helpers `○`

**Work order:** WO-087 / P-581 · **Depends on:** SK.01.ObservabilityHelpers (P-580)

Gives every WO-087 Phase 1/2 entry in 18 domains one way to assert telemetry structurally: a span recorder (name, kind, tags, status, events), a metric recorder (instrument name, unit, kind, tags, values), and log assertions for EventId, template, exception parameter and the L3/L4/L5 conventions. Everything is additive to the lightweight core `SharedKernel.Testing` (BCL `System.Diagnostics`, `Microsoft.Extensions.Logging.Abstractions` already referenced; no new package, no new dependency, no test framework). The failure-shape assertions encode exactly what `ActivityFailureExtensions` produces (P-580), so a recorder regression in 01 fails the same assertion every domain uses. The existing `ActivityRecorder` (unshipped, so movable) joins the new types in `SharedKernel.Testing.Telemetry`. Audit of what exists: `ActivityRecorder` (stopped spans of one source, no assertions), `InMemoryLogger` + `LoggerAssertions` (EventId, level, property, count; no template, exception or convention checks), and hand-rolled listeners in about 15 test projects (FusionCache `TelemetryCapture`, Pipeline tracing/metrics, Search, Webhooks, Scheduling, FeatureManagement, Messaging OTel, EfCore) plus `ApplicationPipelineTestHarness`'s private capture.

**Public API shape (what every adopter calls).** Namespace `SharedKernel.Testing.Telemetry`; every assertion throws `InvalidOperationException` with a readable message, returns its subject for chaining, and is named `Should*` like `LoggerAssertions`.

- `ActivityRecorder` (moved from `.Communication`, `IDisposable`, one process-scoped `ActivityListener` per instance, sampling `AllDataAndRecorded`):
  - `StartRecording(string sourceName)` (existing), `StartRecording(params string[] sourceNames)`, `StartRecordingPrefix(string sourceNamePrefix)` (for example `"SharedKernel."`, the T2 wildcard).
  - `RecordedActivities` (stopped, stop order; the existing doc claimed start order), `StartedActivities` (start order, includes spans never stopped), `Clear()`, `ShouldHaveStoppedAll()` (leak check), `ForTrace(ActivityTraceId)` (isolate one test's spans from parallel tests on a static source).
  - `Dispose()` is idempotent; snapshots stay readable after dispose; nothing is recorded after dispose.
- `ActivityAssertions` (extensions on `IEnumerable<Activity>` / `Activity`):
  - Select: `ShouldHaveSpan(string name)` (first), `ShouldHaveSingleSpan(string name)`, `ShouldNotHaveSpan(string name)`, `ShouldHaveSpanCount(string name, int count)`.
  - Shape: `ShouldHaveKind(ActivityKind)`, `ShouldHaveSource(string sourceName)`, `ShouldHaveTag(string key, object? value)` (`Equals` on `GetTagItem`, message shows actual type), `ShouldHaveTagKeys(params string[] keys)` (exact set, order-free), `ShouldNotHaveTag(string key)`, `ShouldHaveEvent(string name)`, `ShouldHaveParent(Activity)`.
  - Status: `ShouldHaveStatus(ActivityStatusCode, string? description = null)`, `ShouldBeUnset()`.
  - The P-580 shapes: `ShouldBeRecordedFailure(Error error)` (status `Error`, description = `Error.Code`, `error.code` = `Error.Code`, `error.type` = `ErrorType` name, `"_OTHER"` for an undefined value, no exception event); `ShouldBeRecordedException(Type exceptionType, string? errorCode = null)` (status `Error`, description = exception full name and never the message, `error.type` = full name, `error.code` = the code when given and absent otherwise, at least one `exception` event carrying `exception.type`/`exception.message`/`exception.stacktrace`); `ShouldBeRecordedCancellation()` (status `Unset`, no event, no `error.*` tag); `ShouldBeRecordedSuccess()` (status `Unset`, never `Ok`, no `error.*` tag); `ShouldHaveOutcome(string outcome)` (the `outcome` tag, constrained to `WellKnownOutcomes`).
  - Hygiene (L5): `ShouldNotLeak(params string[] forbiddenValues)` over tag values, baggage, status description and event tag values except the `exception` event (which carries the message by design, decision 4 of WO-087).
  - Conventions (T1/T3): `ShouldFollowSpanConvention()` (source `^SharedKernel\.[A-Z]\w+$`, span name lowercase `{capability} {operation}`, kind is Client/Producer/Consumer/Internal, every tag key is lowercase dotted snake case).
- `MetricRecorder` (`IDisposable`, one `MeterListener` per instance, enables every instrument of the matched meters, handles `byte/short/int/long/float/double/decimal`):
  - `StartRecording(params string[] meterNames)`, `StartRecordingPrefix(string meterNamePrefix)`.
  - `Instruments` (`RecordedInstrument`: meter name, name, unit, description, kind, value type; includes instruments that never measured), `Measurements` (`RecordedMeasurement`: instrument name, meter name, `double Value`, tags as an ordered immutable snapshot), `Measurements(string instrument)`, `Collect()` (calls `RecordObservableInstruments`), `Clear()`, `Dispose()` idempotent, snapshots readable after dispose.
  - `Sum(string instrument, params KeyValuePair<string, object?>[] tags)` and `Count(...)`, filtered by tag subset.
- `MetricAssertions` (extensions on `MetricRecorder`): `ShouldHaveInstrument(string name, string? unit = null, InstrumentKind? kind = null)`, `ShouldHaveMeasurement(string instrument, double? value = null, params KeyValuePair<string, object?>[] tags)`, `ShouldHaveTagSet(string instrument, params string[] keys)` (every measurement carries exactly this key set), `ShouldNotHaveTagKey(string instrument, string key)`, `ShouldHaveSum(string instrument, double expected, params KeyValuePair<string, object?>[] tags)`, `ShouldUseOutcomeVocabulary(string instrument)` (every `outcome` tag value is one of `WellKnownOutcomes`), `ShouldNotLeak(params string[] forbiddenValues)`, and conventions (T1/T6): `ShouldFollowMetricConvention()` (meter `^SharedKernel\.[A-Z]\w+$`, instrument `sharedkernel.{capability}.*` lowercase, every instrument declares a non-empty unit, duration histograms use `s`, tag keys are constants-shaped lowercase).
- Log capture stays `InMemoryLogger`/`InMemoryLoggerFactory`; additions in `SharedKernel.Testing.Logging`: `LogRecord.Template` (the `{OriginalFormat}` property, null when absent); `LoggerAssertions`: `ShouldHaveLoggedTemplate(EventId, string template)`, `ShouldHaveLoggedWithException(EventId, Type exceptionType)`, `ShouldHaveLoggedWithoutException(EventId)`, `ShouldNotLeak(params string[] forbiddenValues)` (message, template-state values, exception message), `ShouldFollowLogConvention()` (L3 on every record's template, L4 on every record at `Error` level or above and on every record that carries an exception-typed state, L5 placeholder names), `ShouldOnlyLogEventIdsInSubBlock(int domainBase, int subBlockIndex)`; `LoggerFactoryAssertions` on `InMemoryLoggerFactory`: `ShouldHaveCategory(Type kernelType)` (the category is the type's full name, L7), `ShouldOnlyHaveCategoriesStartingWith(string prefix)`.

A test then reads, in a few lines:

```csharp
using var spans = ActivityRecorder.StartRecording(MessagingDiagnostics.ActivitySourceName);
using var metrics = MetricRecorder.StartRecording(MessagingDiagnostics.MeterName);
await sut.PublishAsync(message, ct);
spans.RecordedActivities.ShouldHaveSingleSpan("messaging publish")
    .ShouldHaveKind(ActivityKind.Producer).ShouldHaveTag(MessagingTagKeys.Destination, "orders")
    .ShouldBeRecordedFailure(MessagingErrors.Unavailable());
metrics.ShouldHaveMeasurement("sharedkernel.messaging.publish.count", 1,
    new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure));
```

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify home and namespace. Everything in the core (Foundation/Model + BCL only; rule 2 holds, no new package or `PackageReference`; `System.Diagnostics.DiagnosticSource` is in the shared framework, verify the `.nuspec` gains no dependency). New namespace `SharedKernel.Testing.Telemetry`; `ActivityRecorder` moves into it (it is in `PublicAPI.Unshipped.txt` only, so the move is free; update the one README mention in `Communication.Testing` as an outbound note, not an edit). Record that `AmbientActivityTestHelper` stays in `.Communication` (an ambient-context setter, not a recorder) | `SharedKernel.Testing` | `○` |
| D-02 | Ratify the listener model and its limits. One listener per recorder, process-scoped, filtered by exact name or name prefix, `AllDataAndRecorded` sampling; snapshots, never live collections; static `ActivitySource`/`Meter` instances are shared across parallel tests, so the helpers offer `ForTrace` and tag-subset filters and the README tells tests on a static source to filter or keep one collection (rule 8 covers the recorder, not the shared source). `Meter`s created through `IMeterFactory` per test provider are isolated by construction. Correct the `RecordedActivities` doc (stop order) and add `StartedActivities` | `SharedKernel.Testing` | `○` |
| D-03 | Ratify the failure-shape assertions against `SK.01.ObservabilityHelpers` D-01/D-02: status `Error` with description = `Error.Code` or exception full name (never a message), `error.type` = `ErrorType` name or exception full name, `error.code` from `Error.Code` / `IHasError`, `AddException` event with the three standard tags, cancellation and success leave status `Unset` and add nothing, `Ok` is never set. Expected `error.type` for an undefined `ErrorType` is `"_OTHER"`. The helper does not call the Primitives recorder to compute expectations (that would make the check circular); it states the shape literally via `WellKnownTagKeys.ErrorCode`/`ErrorType` (exist today) and `WellKnownOutcomes` (P-580) | `SharedKernel.Testing` | `○` |
| D-04 | Ratify the convention checks. T1/T3/T6 regexes as listed above; L3: template is one sentence ending in a period, placeholders PascalCase, no `{Error}`/`{Reason}`/`{ErrorMessage}`/`{Detail}`, no em-dash, no `key=value` parenthetical, and a placeholder whose name ends in `Name` or `Key` is wrapped in single quotes (the only heuristic for "identifier values quoted"; extend the suffix list only with a test); L4: a record at `Error` level or above, or whose state holds an exception-typed value, must carry `Exception`; L5: placeholder names `TenantId`, `CorrelationId`, `TraceId`, `SpanId`, `ActorId`, `ClientId`, `UserId`, `IpAddress`, `Url`, `Host`, `IdempotencyKey`, `ErrorMessage` are rejected. All checks return every violation in one exception message so a conformance test lists the whole backlog. These are test-side mirrors of the report-only governance tests of WO-087 Phase 0; they never replace `00.Governance` | `SharedKernel.Testing` | `○` |
| D-05 | Ratify the log additions. No new logger type: `LogRecord.Template` reads `{OriginalFormat}`; assertions live on `LoggerAssertions` and a new `LoggerFactoryAssertions`. `ShouldOnlyLogEventIdsInSubBlock` delegates to `LoggingEventIdRanges.IsInSubBlock` (P-580 C-06) instead of re-deriving the arithmetic. The category rule L7 is checked on the factory's `Loggers` keys | `SharedKernel.Testing` | `○` |
| D-06 | Blast radius. The only in-repo users of `Testing.Communication.ActivityRecorder` are its own tests and the core README; the FusionCache `ActivityRecorder` is a separate internal class. Grep `ActivityRecorder`, `MeterListener`, `ActivityListener` across `**/*.Tests/` and `samples/**/*.Tests/` and list the hand-rolled listeners as outbound notes (migration is each domain's Phase 1/2 work, not this phase) | `SharedKernel.Testing` | `○` |
| C-01 | Move `ActivityRecorder` to `.Telemetry`; add the multi-name and prefix starters, `StartedActivities`, `Clear`, `ShouldHaveStoppedAll`, `ForTrace`; idempotent `Dispose`; `ActivityStarted` and `ActivityStopped` both enqueue | `SharedKernel.Testing` | `○` |
| C-02 | `ActivityAssertions` selection, shape, status, event and parent members | `SharedKernel.Testing` | `○` |
| C-03 | `ActivityAssertions` P-580 shapes (`ShouldBeRecordedFailure/Exception/Cancellation/Success`, `ShouldHaveOutcome`) and `ShouldNotLeak`, `ShouldFollowSpanConvention` | `SharedKernel.Testing` | `○` |
| C-04 | `MetricRecorder`, `RecordedInstrument`, `RecordedMeasurement` (all seven numeric value types, tag snapshot, observable `Collect`, idempotent `Dispose`) | `SharedKernel.Testing` | `○` |
| C-05 | `MetricAssertions` (instrument, measurement, tag set, sum, outcome vocabulary, leak, convention) | `SharedKernel.Testing` | `○` |
| C-06 | `LogRecord.Template`; `LoggerAssertions` template, exception, leak members | `SharedKernel.Testing` | `○` |
| C-07 | `LogConventions` internal checker (L3/L4/L5) behind `LoggerAssertions.ShouldFollowLogConvention`; `ShouldOnlyLogEventIdsInSubBlock`; `LoggerFactoryAssertions` | `SharedKernel.Testing` | `○` |
| T-01 | Span capture: several sources at once (exact names and a prefix), a source that is not listened to stays unrecorded, nested spans and parent links, a span never stopped appears only in `StartedActivities` and trips `ShouldHaveStoppedAll`, `ForTrace` isolates two interleaved traces | `SharedKernel.Testing.Tests` | `○` |
| T-02 | Disposed listeners: spans started after `Dispose` are not recorded, earlier snapshots keep tags/status/events, double `Dispose` does not throw, a second recorder on the same source is unaffected by the first one's disposal; same four checks for `MetricRecorder` | `SharedKernel.Testing.Tests` | `○` |
| T-03 | Concurrent recording, deterministic counts and no sleeps: `Parallel.For` over many spans and many measurements (both recorders alive, several tag combinations), assert the exact totals, per-tag sums and that no snapshot throws while writers run | `SharedKernel.Testing.Tests` | `○` |
| T-04 | Every `Should*` of `ActivityAssertions` passes on a matching hand-built span and throws a readable `InvalidOperationException` on each mismatch (wrong name/kind/tag value/extra tag/status/description equal to the exception message/`Ok` status/missing event/leaked value); the P-580 shapes are exercised against hand-built correct and deliberately wrong spans | `SharedKernel.Testing.Tests` | `○` |
| T-05 | Conformance against the real producer once P-580 ships: run `RecordResult`, `RecordException` (plain, `SharedKernelException`, `AggregateException`, `TaskCanceledException`), success, and every `ErrorType` member (enumerate the enum, so a new member fails until the assertion maps it) through `ActivityRecorder` and assert with the P-580 assertions | `SharedKernel.Testing.Tests` | `⚑` |
| T-06 | `MetricRecorder` over counter, up-down counter, histogram, gauge and an observable gauge (`Collect`), `int`/`long`/`double`/`decimal` values, tag order and null tag values, instruments with and without unit; `MetricAssertions` pass/fail pairs including `ShouldFollowMetricConvention` listing every violation and `ShouldUseOutcomeVocabulary` | `SharedKernel.Testing.Tests` | `○` |
| T-07 | Log additions: `Template` for a `[LoggerMessage]` source-generated method and for a plain `LogInformation` call, `ShouldHaveLoggedTemplate`, exception present/absent, `ShouldFollowLogConvention` on compliant and on each violating template (missing period, `{Error}`, lowercase placeholder, em-dash, unquoted `{IndexName}`, forbidden L5 name), `ShouldNotLeak`, `ShouldOnlyLogEventIdsInSubBlock` (boundaries), `LoggerFactoryAssertions` | `SharedKernel.Testing.Tests` | `○` |
| T-08 | The acceptance snippet: one test asserts a span's name, kind, tags, status and exception event, and a metric's name, unit and tag set, in the few-line shape shown above, against a local `ActivitySource` and `Meter` | `SharedKernel.Testing.Tests` | `○` |
| DO-01 | `PublicAPI.Unshipped.txt` for the core (new types and members; `ActivityRecorder` entries re-namespaced) | `SharedKernel.Testing` | `○` |
| DO-02 | Core `README.md`: a "Assert telemetry" recipe (span, metric, log, the P-580 shapes, the static-source caveat), Reference rows for the new namespace and members, Common mistakes rows; fix recipe 10's `ActivityRecorder` usings; `.csproj` `Description`/`PackageTags` mention telemetry; snippets compile against the public API | `SharedKernel.Testing` | `○` |
| DO-03 | Catalogue row, rules 14-15 and decisions in `src/Testing/CLAUDE.md` move from *planned* to shipped; Public Entry Points gains a "Telemetry" bullet; ask for `/sync-brain` to add a root "What Goes Where" row (*Assert spans, metrics and log conventions in a test* → `ActivityRecorder`, `MetricRecorder`, `*Assertions`, `SharedKernel.Testing`, 16) | `src/Testing` | `○` |

**Acceptance:**
- [ ] A test asserts a span's name, kind, tags, status and exception event, and a metric's name, unit and tag set, in a few lines (T-08).
- [ ] The P-580 shapes (Result failure, exception, cancellation, success, outcome tag, every `ErrorType`) are asserted by name, and a regression in `ActivityFailureExtensions` fails them (T-04, T-05).
- [ ] Self-tests cover several sources, disposed listeners and concurrent recording for both recorders, with no sleeps or wall-clock reads.
- [ ] Log assertions cover EventId (existing), template, exception parameter, the L3/L4/L5 conventions, sub-block membership and category.
- [ ] `SharedKernel.Testing` gains no `PackageReference` and no test framework; `CoreTestingPackage_DependsOnlyOnFoundationAndModelPackages` and the tier check pass.
- [ ] `PublicAPI.Unshipped.txt`, the core README and `src/Testing/CLAUDE.md` updated; the Unit lane is green.

## Blocked

- **SK.16.TelemetryCapture T-05 and the outcome/sub-block members** (`ShouldHaveOutcome`, `ShouldUseOutcomeVocabulary`, `ShouldOnlyLogEventIdsInSubBlock`) wait on `SK.01.ObservabilityHelpers`: `WellKnownOutcomes`, `WellKnownTagKeys.Outcome`, `ActivityFailureExtensions`, `IHasError` and `LoggingEventIdRanges.IsInSubBlock` do not exist on disk yet (`grep` over `src/**/*.cs` finds none of them). The generic capture, selection, status, tag, event, metric and log-template work does not depend on them and proceeds; only these members and T-05 are marked `⚑` until P-580 ships.

## Cross-Domain Dependencies

Inbound:
- **01.Core (SK.01.ObservabilityHelpers / P-580):** the shapes in D-03 are its contract; any change to the recorder's status, tag or event shape updates the assertions in the same change.

Outbound notes (not tasks of this domain):
- **All 18 WO-087 Phase 1/2 domains:** assert telemetry through `ActivityRecorder`/`MetricRecorder`/the `*Assertions` and the log conventions; hand-rolled `ActivityListener`/`MeterListener` code in `.Tests` projects is theirs to delete.
- **02.Caching:** `SharedKernel.Caching.FusionCache.Tests` `Telemetry/TelemetryCapture.cs` (`MetricRecorder`, internal `ActivityRecorder`, `CapturingLoggerProvider`) duplicates this phase; migrate in its Phase 1/2.
- **05.Application:** `ApplicationPipelineTestHarness.WithActivityCapture()` keeps a private listener; the 05 implementer may expose its recorded spans and measurements through the shared recorders. `Pipeline.Tests` tracing/metrics suites migrate in its Phase 2.
- **11.Communication:** `SharedKernel.Communication.Testing` README names `ActivityRecorder`; update the namespace after D-01 ships.
- **00.Governance:** the convention checks mirror the report-only telemetry/log governance tests; P-618 remains the enforcement point.
