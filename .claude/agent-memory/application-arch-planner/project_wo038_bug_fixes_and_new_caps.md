---
name: project_wo038_bug_fixes_and_new_caps
description: WO-038 (root P-231–P-234) — self-contained bug fixes, contract evolution, parallel dispatch, fire-and-forget, and streaming pipeline behaviors; design-only as of 2026-07-01
metadata:
  type: project
---

WO-038 dispatched 2026-07-01 (root `state-map.md` P-231–P-234). Four targeted phases, all within `05.Application` / `05.Application.Behaviors`. Reuses the same six phase keys as WO-035/WO-036.

**Why:** Post-build audit of the WO-035/WO-036 implementation revealed confirmed bugs (hot-path allocations, silent correctness hazards, nullable suppression), undocumented design stances (fail-and-consume-key, always-invalidate default), and a streaming vocabulary gap (IStreamQuery has no cross-cutting coverage).

---

## P-231 — Bug Fixes and Behavioral Corrections (no cross-domain dependency)

Eight targeted fixes, all `○` pending:
- `IIdempotencyKeyStore.MarkProcessedAsync` XML doc: **fail-and-consume-key invariant** — a `Result.Failure` (business-rule failure, not a thrown exception) STILL consumes the key; client must use a new key to retry after failure. Fault (exception) does NOT consume the key.
- `ValidationBehavior`: remove `validators.Any()` pre-loop guard — causes double-enumeration; loop is a no-op on empty sequence.
- `CachingBehavior`: direct `await` on `ValueTask` from `GetOrSetAsync` — eliminates unconditional `.AsTask()` allocation on L1 hits.
- `ResilienceBehavior`: resolve **non-generic `ResiliencePipeline`** (not `ResiliencePipeline<TResponse>`) — per-`TResponse`-typed registration silently falls back to no-op when key doesn't match exact closed type.
- `MediatRDomainEventDispatcher`: **compile-time capture of `Publish<T>` MethodInfo** (via typed delegate, not nullable-suppressed `GetMethod(...)!`) — eliminates deferred `NullReferenceException` on rename.
- `CacheInvalidationBehavior`: add **`InvalidateOnlyOnSuccess` opt-in flag** (default `false` = always-invalidate-on-non-throw, backward compatible); XML doc must make the default stance explicit — a `Result.Failure` STILL evicts in default mode.
- `TransactionBehavior`: add `OperationCanceledException` cancellation rollback test (T-20).
- `MetricsBehavior`/`LoggingBehavior`/`TracingBehavior`: `typeof(TRequest).FullName ?? typeof(TRequest).Name` for all tag/key construction — prevents collisions when two assemblies define same short name.

---

## P-232 — Contract Evolution (depends on P-230 for `IHasSuccessFlag`/`IResultOfT<T>` from SharedKernel.Primitives)

Three changes, all `○` pending — **BLOCKED until P-230 ships**:
- `LoggingBehavior`: post-handler log at `Warning` when `response is IHasSuccessFlag f && !f.IsSuccess`; `Information` otherwise.
- `FailureResponseFactory`: eliminate `Expression.Compile()` + `ConcurrentDictionary` cache by using `IResultOfT<T>` generic constraint directly; remove `[RequiresUnreferencedCode]` annotation — makes the factory fully AOT-clean.
- `IAuthorizationContext`/`IAuthorizeRequest`: **multi-requirement evolution** (breaking change — acceptable, types are new in P-217, no downstream consumer yet):
  - `IAuthorizationContext` gains `AllOf(IEnumerable<string>, CancellationToken)` and `AnyOf(IEnumerable<string>, CancellationToken)`.
  - `IAuthorizeRequest` gains `AllOfRequirements` (IReadOnlyCollection<string>) and `AnyOfRequirements` (IReadOnlyCollection<string>); single-string `Requirement` remains as convenience (default-interface-member equivalent to `AllOf([Requirement])`).
  - `AuthorizationBehavior` evaluates AllOf first (short-circuit on failure), then AnyOf (short-circuit on pass).

---

## P-233 — New Features: Parallel Domain Event Dispatch and Fire-and-Forget Commands (no cross-domain dependency)

Two capabilities, all `○` pending:

### Parallel domain event dispatch
- `MediatRDomainEventDispatcherOptions.ParallelDispatch` bool flag (default `false`).
- When `true`: `Task.WhenAll` over all events in a single `DispatchAsync` call; all exceptions collected into `AggregateException` before rethrowing (all handlers run regardless of early faults).
- Serial path (default) unchanged.
- Opt-in via `AddSharedKernelApplication(opts => opts.ParallelDispatch = true)` overload (backward-compatible; existing zero-arg overload still valid).
- **Only valid for independently-observable events (no ordering dependency)** — documented callout required.

### Fire-and-forget commands
- `IFireAndForgetCommand` marker extending `ICommand` (no `TResponse`); lives in `SharedKernel.Application`.
- `IFireAndForgetDispatcher.EnqueueAsync(IFireAndForgetCommand, CancellationToken) → ValueTask` (immediate return).
- `ChannelFireAndForgetDispatcher`: backed by `System.Threading.Channels.Channel<IFireAndForgetCommand>` (BCL, zero new NuGet); bounded with `FireAndForgetOptions.Capacity` (default 1000) and `RejectionPolicy` (DropAndLog / Block).
- `FireAndForgetBackgroundConsumer` (`BackgroundService`): dequeues, resolves `IServiceScope` per command, executes handler, catches exceptions → logs at `Error`, continues.
- `FireAndForgetGuardBehavior<,>`: intercepts any `ISender.Send(IFireAndForgetCommand)` and throws `InvalidOperationException` with descriptive message — registered outermost, never calls `next()`.
- All registered via `ApplicationBehaviorsBuilder.AddFireAndForgetDispatch(Action<FireAndForgetOptions>?)`.

---

## P-234 — IStreamPipelineBehavior Cross-Cutting Coverage (depends on P-232 for evolved IAuthorizationContext)

Five streaming behaviors, all `○` pending:
- `StreamLoggingBehavior<,>` — entry (Info), first-item latency (Debug), completion (Info), fault (Warning).
- `StreamMetricsBehavior<,>` — one `RequestDuration` measurement per stream lifetime, `outcome` tag: "streamed" / "faulted"; uses `FullName ?? Name`.
- `StreamTracingBehavior<,>` — one Activity span covering stream lifetime; same null-safe pattern as `TracingBehavior<,>`.
- `StreamValidationBehavior<,>` — FluentValidation once at stream-open, never per-item; throws `ValidationException` before opening stream.
- `StreamAuthorizationBehavior<,>` — `IAuthorizationContext.AllOf`/`AnyOf` once at stream-open (P-232 multi-requirement shape); throws authorization exception before opening stream.

Non-applicable behaviors (Transaction, Caching, CacheInvalidation, Idempotency, Resilience) are **explicitly documented** with rationale — streaming queries are read-only (no ICommandBase), caching IAsyncEnumerable is architecturally unsound, retry-after-partial-consumption is undefined.

Registered via `ApplicationBehaviorsBuilder.AddStreamingBehaviors()` — distinct from `AddBehaviors()`, opt-in only.

---

## Status as of 2026-07-01: design-only. All 56 WO-038 tasks start `○`.

**Root backlog IDs:** P-231 (bug fixes), P-232 (contract evolution, depends on P-230), P-233 (parallel dispatch + fire-and-forget), P-234 (streaming behaviors, depends on P-232). All under WO-038.

**Key cross-domain blocker:** P-232 and P-234 are both blocked on P-230 shipping `IHasSuccessFlag`/`IResultOfT<T>` in `SharedKernel.Primitives` (01.Core). P-231 and P-233 have no cross-domain dependencies.
