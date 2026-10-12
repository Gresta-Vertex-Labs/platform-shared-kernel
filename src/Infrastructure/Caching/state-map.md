# 02.Caching — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Caching.Abstractions` | Abstractions | ● | `ICacheService`, `ITenantCacheService` (explicit `TenantId` on every call), `CachePolicy`, `CacheKeyFormat`, `IDistributedLockService` (locks, leases, fencing tokens), `ICacheWarmupStrategy`. The only caching package application code references. |
| `SharedKernel.Caching.FusionCache` | Adapter | ● | The cache: required `ServiceName`, Brotli compression, `AddCacheEncryption()` (cache key as associated data), warmup, `cache` readiness probe. |
| `SharedKernel.Caching.Redis.Core` | Adapter | ● | `AddRedisConnection(configuration)` — the one multiplexer (TLS/mTLS, fail-fast), `redis` readiness probe. |
| `SharedKernel.Caching.Redis` | Adapter | ● | `AddRedisL2()` — distributed L2 and backplane (removals, expirations, tag evictions, clears). Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.DistributedLocking` | Adapter | ● | `IDistributedLockService` over Redis — kept-alive locks, leases, atomic fencing tokens. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.HashStore` | Adapter | ● | `IRedisHashService`, `ITypedHashStore<T>`. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.PubSub` | Adapter | ● | `IRedisChannelService` — loss-tolerant Pub/Sub, never messaging. Edge → Redis.Core. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.02.ObservabilityConformance` | Conform Caching to the observability gold standard (WO-087 / P-601) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

The first public release ships with root P-577 (release train); both phases below land before it.

### SK.02.ObservabilityConformance — Conform Caching to the observability gold standard `○`

**Work order:** WO-087 / P-601 · **Depends on:** SK.02.LockUnavailableError (P-585), SK.01.ObservabilityHelpers (P-580), SK.16.TelemetryCapture (P-581), SK.00.ObservabilityReportOnly (P-583). Coordinates with SK.13.WildcardTelemetry (P-582), which deletes `WithCachingTelemetry` and subscribes `SharedKernel.*` by wildcard; no ordering needed, because this phase keeps the source and meter name `SharedKernel.Caching`.

Applies every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" (L1-L8, T1-T6, E1-E4, C1-C3) to all seven packages. T7 (client-facing error correlation) and T8 (propagation) do not apply: this domain has no inbound edge and no wire propagation. `Caching.Abstractions` does not log and emits nothing. Locks, hash store and pub/sub have no metrics or spans by decision (they stay that way); the telemetry work is the FusionCache package, the logging work is all four logging packages. Event ids are unchanged: this phase renumbers nothing.

**Telemetry (FusionCache package; instruments are defined only here).**
- Public constants `CachingTelemetry.SourceName` and `MeterName`, both `"SharedKernel.Caching"` (T1), versioned with `TelemetryVersion.Of(typeof(…).Assembly)` replacing the hand-written attribute/assembly-version chain. The `ActivitySource` stays static; the `Meter` is created through `IMeterFactory` (DI is available), so instruments become instance fields of `FusionCacheService` and its event handlers instance methods. `AddSharedKernelCaching` ensures the metrics services are registered (`TryAdd`), so the factory is a required dependency, not an optional one.
- Instruments (T6), all with a unit: `sharedkernel.caching.hits` (`{hit}`), `.misses` (`{miss}`), `.errors` (`{error}`), `.evictions` (`{eviction}`), `.factory.duration` (histogram, unit `s`, recorded from `Stopwatch.GetElapsedTime(...).TotalSeconds`; the bucket view is the single `SharedKernel.*` seconds view from `SK.13.WildcardTelemetry`, none here).
- Tag keys only from constants (T4): `CachingTagKeys` (public, `{capability}.{snake_name}`): `caching.key_prefix` (`{service}:{entity}`, never an id or tenant, rule 35), `caching.level` (`l1`/`l2` as constants), `caching.operation` (`get`/`get_or_set`/`set`/`factory`, constants), `caching.eviction_reason`, and `caching.result` (`hit`/`miss` constants) on spans. Outcome and error tags use `WellKnownTagKeys.Outcome`/`ErrorType`/`ErrorCode` and `WellKnownOutcomes`. `cache.error_type` is removed: the error counter carries `error.type` (the exception full name, bounded by CLR type) and `caching.operation`. The factory-duration histogram gains `outcome` (`success`/`exception`/`canceled`).
- Spans (T3, T5): names `caching get`, `caching get_or_set`, `caching set` (lowercase `{capability} {operation}`; kind Client for the L2 dependency stays); failure via `activity.RecordException(ex)` (status = exception full name, never the message; full exception event; cancellation never an error) instead of `SetStatus(…, ex.GetType().Name)`; `Result`-returning paths use the recorder's result overload.

**Logging.**
- L1: one internal `{Package}LoggingEventIds` static class per logging package (`FusionCache` base `Caching + 0`, `Redis.Core` `+100`, `DistributedLocking` `+300`, `PubSub` `+500`; `Redis` `+200` and `HashStore` `+400` stay reserved and get no class), const arithmetic from `LoggingEventIdRanges.Caching`, no bare `Caching + n` literals. Existing ids keep their numbers.
- L2: one `internal static partial class {Package}Log` per package under `Diagnostics/`, replacing the three nested `Log` classes in FusionCache and the nested ones elsewhere; plain static partial methods, `ILogger` first.
- L3/L5 rewrites of every template: one sentence ending in a period, quoted identifier values (`'{Resource}'`, `'{Channel}'`, `'{KeyPrefix}'`, `'{StrategyName}'`), `{ElapsedMilliseconds}`, `{ErrorType}`/`{ErrorCode}` (not `{Reason}`), no em-dash and no parenthesised key=value. Host names are never logged (L5): `+100`, `+101` and `+102` drop `{EndPoint}`; `+102` states that a non-loopback endpoint is configured without TLS.
- L4: add the missing logs on swallowed paths, each passing the exception: `+18` (FusionCache, Warning) when the best-effort evict after a decrypt failure fails; `+19` (FusionCache, Debug) and `+104` (Redis.Core, Debug) when the `cache` and `redis` probes catch (probes poll, so not above Debug). L6 level review recorded per event; L7 category check (`ILogger<KernelType>` only) and L8 (loggers required) verified on disk.

**Exceptions and error codes.** Sweep every `throw` in non-test code against E2/E3 (registration-order guards, `EnsureNotRegistered`, options validators): sentences ending in a period, values quoted, remedy named, standard phrasings ("requires … Call … first."), no inner `Message`, no connection string. Classified failures already derive `SharedKernelException` after `SK.02.LockUnavailableError`; `CachingErrorCodes` is the only public codes class and no further code is anticipated (decrypt failure codes come from `01.Core` Cryptography).

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify telemetry names: constants `CachingTelemetry`, instrument names `sharedkernel.caching.*`, units, `caching.*` tag keys, `caching.result` for hit/miss (the word `outcome` is reserved for success/failure/exception/canceled), span names, which tag keys go to `WellKnownTagKeys` (only cross-domain ones) and which stay in `CachingTagKeys`. List the whole rename table for the PR body | `SharedKernel.Caching.FusionCache` | `○` |
| D-02 | Ratify `IMeterFactory` for the meter (instance instruments) versus keeping a static meter; verify the metrics registration extension and its package version on disk and that `AddSharedKernelCaching` can `TryAdd` it. Update rule 35's "static `Meter` excepted" wording once shipped | `SharedKernel.Caching.FusionCache` | `○` |
| D-03 | Ratify L6 levels event by event (`+3` cancelled warmup, `+101`, `+305`/`+306` Warning with exception, `+307` Error) and the rule that lock `'{Resource}'` and pub/sub `'{Channel}'` are caller-chosen names that may be logged but must never carry a tenant id or personal data; add that sentence to the XML docs of `IDistributedLockService`, `TryAcquireAsync` and `IRedisChannelService` | `SharedKernel.Caching.Abstractions`, `.Redis.PubSub` | `○` |
| D-04 | Ratify the new events `+18`, `+19` (FusionCache) and `+104` (Redis.Core) and their levels; confirm the `+200` and `+400` sub-blocks stay reserved | `SharedKernel.Caching.FusionCache`, `.Redis.Core` | `○` |
| C-01 | `CachingTelemetry` and `CachingTagKeys` constants; `TelemetryVersion.Of`; `IMeterFactory`-created instruments with units and seconds; constants replace every tag literal in `FusionCacheService` | `SharedKernel.Caching.FusionCache` | `○` |
| C-02 | Span rename and failure recording through `RecordException`/`RecordResult`, cancellation excluded; `outcome` tag on the factory histogram; `caching.operation` on the error counter | `SharedKernel.Caching.FusionCache` | `○` |
| C-03 | `FusionCacheLoggingEventIds` + `FusionCacheLog` consolidating `FusionCacheService`, `EncryptedCacheService` and `CacheWarmupHostedService` logs; template rewrites; new `+18`/`+19` logs on the swallowed catches in `EncryptedCacheService` and `CacheReadinessProbe` | `SharedKernel.Caching.FusionCache` | `○` |
| C-04 | `RedisCoreLoggingEventIds` + `RedisCoreLog`; templates without endpoints; new `+104` log in `RedisConnectionProbe`; catches stay narrow (E4) | `SharedKernel.Caching.Redis.Core` | `○` |
| C-05 | `DistributedLockingLoggingEventIds` + `DistributedLockingLog` (from `RedisDistributedLockService` and `RedisDistributedLock`); `PubSubLoggingEventIds` + `PubSubLog` (from `RedisChannelSubscription`, used by `RedisChannelService`); template rewrites; `Redis` and `HashStore` unchanged (no logging) | `SharedKernel.Caching.Redis.DistributedLocking`, `.Redis.PubSub` | `○` |
| C-06 | E2/E3 message sweep of every `throw` in non-test code, with a list of changed messages; no exception type or code changes beyond `SK.02.LockUnavailableError` | `SharedKernel.Caching.*` | `○` |
| C-07 | Keep doubles in step: `FakeRedisChannelService` and the other `Caching.Testing`/`Caching.Redis.Testing` fakes need no telemetry; verify none logs a message or tag this phase renames | `SharedKernel.Caching.Testing`, `SharedKernel.Caching.Redis.Testing` | `○` |
| T-01 | Migrate `OtelMetricsTests`, `CacheLevelMetricsTests` and `TelemetryRedactionTests` to the shared `ActivityRecorder`/`MetricRecorder` helpers (SK.16.TelemetryCapture); assert names, units, tag keys (no `cache.*` key and no id/tenant in any tag value), `seconds` recording, `outcome` values, span names, and span failure (status description is the exception full name, an exception event is present, the message is absent; cancellation leaves status Unset). Unit lane | `SharedKernel.Caching.FusionCache.Tests` | `○` |
| T-02 | Pin literals: `CachingTelemetry` names, every `CachingTagKeys` value, every instrument name and unit (a rename fails loudly). Unit lane | `SharedKernel.Caching.FusionCache.Tests` | `○` |
| T-03 | Logging: EventId integrity per package (own sub-block, no interleaving, via `LoggingEventIds` helpers from P-580), template conformance (period, quoted names, no forbidden placeholder, no `{EndPoint}`), and a log-with-exception assertion for every catch path (`+6`, `+15`/`+18`, `+19`, `+101`, `+104`, `+305`, `+306`, `+500`, `+501`, `+502`) through the shared logger assertions. Unit lane for FusionCache; Integration lane for the Redis packages | all `.Tests` projects of the domain | `○` |
| T-04 | Migrate the `+101` test of `SK.02.LockUnavailableError` to the shared logger assertions; extend with `+102` (no endpoint in the text) and `+104`. Integration lane, bounded polling | `SharedKernel.Caching.Redis.Core.Tests` | `○` |
| T-05 | Run the P-583 report-only conformance tests and record zero findings for the domain; fix every finding in this phase (none may be suppressed) | domain test projects | `○` |
| T-06 | Existing must-cover behaviours unchanged: hit vs miss, stampede, `SkipCaching` (a skipped value is never broadcast or counted as a hit), expire vs remove under fail-safe, tenant isolation, cross-instance tag invalidation with two `ServiceProvider`s | `SharedKernel.Caching.FusionCache.Tests`, `SharedKernel.Caching.Redis.Tests` | `○` |
| DO-01 | READMEs: `FusionCache` (telemetry tables: instrument names, units, tag keys, span names; remove the `WithCachingTelemetry()` sentence, replaced by the wildcard note), each logging package (EventId table, now with `+18`, `+19`, `+104` and rewritten wording), `Caching.Abstractions` (resource-name sentence), domain `README.md` | `SharedKernel.Caching.*` | `○` |
| DO-02 | Release-notes list for the PR body "Breaking changes": every renamed instrument, unit change (ms to s), tag key, span name; log text changes (event ids unchanged); the exception changes from `SK.02.LockUnavailableError` | `SharedKernel.Caching.*` | `○` |

**Acceptance:**
- [ ] Every applicable rule (L1-L8, T1-T6, E1-E4, C1-C3) holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Source and meter are `SharedKernel.Caching` public constants; every instrument has a unit and durations are seconds; no hand-typed tag-key or outcome literal remains in the package.
- [ ] No log in the domain contains a host, a full key, an id or a tenant; every caught-exception log passes the exception.
- [ ] Breaking renames are listed for the release notes; `PublicAPI.Unshipped.txt`, package READMEs and the domain `CLAUDE.md` are updated in the same change; tests assert telemetry and codes through the shared capture helpers.
- [ ] Unit and Integration lanes green and warning-free; `consumer-verify/SharedKernel.Caching.ConsumerVerify` runs.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core** (inbound, open): `SK.02.LockUnavailableError` needs `IHasError` / the recorder from `SK.01.ObservabilityHelpers`; `SK.02.ObservabilityConformance` needs `TelemetryVersion`, `ActivityFailureExtensions`, `WellKnownOutcomes`, `WellKnownTagKeys.Outcome` and the `LoggingEventIdRanges` helpers. Also depends on `SK.01.CoreConformance` renaming `unexpected.exception` to `unexpected.default` only if a test here asserts that literal (none planned).
- **16.Testing** (inbound, open): `SK.02.ObservabilityConformance` T-tasks use `ActivityRecorder`, `MetricRecorder` and the logger assertions from `SK.16.TelemetryCapture`.
- **00.Governance** (inbound, open): the report-only SK0043/SK0044 and EventId/telemetry/template/error-code assertions from `SK.00.ObservabilityReportOnly` must exist before this phase's zero-findings acceptance is checkable.
- **13.ServiceDefaults** (outbound note): the instrument, tag and span renames break any dashboard built on `cache.*`. `SK.13.WildcardTelemetry` deletes `WithCachingTelemetry()`; its Shop call sites (Ordering, Inventory, Catalog) are theirs. The `SharedKernel.Caching` source and meter names do not change.
- **14.Presentation** (outbound note): `DistributedLockUnavailableException` becomes a `SharedKernelException`, so the existing edge mapping answers Unavailable with no edge change; if `SK.02.LockUnavailableError` T-02 finds no public seam, `14.Presentation` is asked for a test-visible one.
- **19.Scheduling** (outbound note): catches `DistributedLockUnavailableException` by type, unaffected; its `OccurrenceLeaseTests` use `ForResource`, which keeps its signature.
- **05.Application** (outbound note): `Application.Pipeline.Caching`'s own metrics are owned by 05 and are not renamed here.
- **Root (arch-lead)**: `SharedKernel.Caching.Abstractions` gains a reference to `SharedKernel.Core` (Foundation, tier-legal, not a declared adapter edge); the root `CLAUDE.md` Abstractions description needs no change unless arch-lead wants it recorded.
