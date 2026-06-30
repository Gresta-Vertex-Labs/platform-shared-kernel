---
name: project_wo036_ten_step_pipeline
description: WO-036 (root P-220–P-224) — tracing, streaming vocabulary, resilience, test harness, write-side cache invalidation; design-only as of 2026-06-30
metadata:
  type: project
---

WO-036 dispatched 2026-06-30 (root `state-map.md` P-220–P-224) extends the published seven-step pipeline (WO-035) with five new capabilities, growing the pipeline to a **ten-named-slot** canonical order. Reuses the same six phase keys as WO-035 (`SK.05.Design/Scaffold/Core/Tests/Docs/Published`) — both work orders' tasks coexist under each phase key in `05.Application/state-map.md`.

**Why:** Closes gaps the domain's own CLAUDE.md had been claiming as already true (tracing parity with `07.Messaging`) or that a 2026 CQRS layer needs (streaming reads, resilience, write-side cache invalidation) but never had vocabulary for.

**The five capabilities (see [[pipeline_behavior_local_seam_pattern]] for the seam pattern — note: none of these five need a NEW local seam):**

1. **Tracing parity** — `ApplicationDiagnostics.ActivitySource` (BCL, same name `"SharedKernel.Application"` + version as the existing `Meter`) + a **distinct** new `TracingBehavior<,>` (not folded into `MetricsBehavior` — single responsibility), positioned immediately after `MetricsBehavior`. Mirrors `07.Messaging.MassTransit.Diagnostics.MessagingDiagnostics.ActivitySource` and `ConsumerBase.Consume`'s `using var activity = ActivitySource.StartActivity(...)` shape exactly. Zero new NuGet dependency.

2. **Streaming query vocabulary** — `IStreamQuery<TResponse>` (over MediatR's `IStreamRequest<TResponse>`) + `IStreamQueryHandler<,>` (over `IStreamRequestHandler<,>`). **Locked decision: NO `Result<T>` wrapping** for streamed items — raw `TResponse` per item via `IAsyncEnumerable<T>`, errors terminate the stream via thrown exception (standard `IAsyncEnumerable` semantics). This is a deliberate, explicit deviation from the `Result`-everywhere convention used everywhere else in this domain — document this distinctly whenever asked about it, never silently apply `Result<T>` here. **None of the ten pipeline behaviors apply to streaming** — `ValidationBehavior`'s `TRequest : IRequest<TResponse>` constraint doesn't match `IStreamRequest<TResponse>`; extending any behavior to streaming is an explicit future phase, never assumed.

3. **Resilience behavior** — new `IRetryableRequest` marker (mirrors `IAuthorizeRequest`/`IIdempotentRequest`) + `ResilienceBehavior<,>` backed by an externally-registered Polly v8 resilience pipeline. **Retry-with-backoff only this phase — circuit-breaking explicitly deferred.** The retry-after-partial-commit hazard is resolved by POSITION, not by a new interface constraint: `ResilienceBehavior` wraps `IdempotentCommandBehavior` + `TransactionBehavior` in the pipeline, so a retry re-runs the full duplicate-check-then-commit unit on every attempt, never a bare second commit. A command implementing `IRetryableRequest` without also implementing `IIdempotentRequest` is a **documented, accepted gap** — not mechanically preventable across two independent C# marker interfaces (no "interface A implies interface B" mechanism exists). Code review must catch this; the compiler will not.

4. **Reusable pipeline test harness** — lives in `SharedKernel.Application.Behaviors.Tests`, never packaged, never referenced by `16.Testing` or production code. Formalizes the existing "prefer real `ServiceCollection`+`AddMediatR` over hand-rolled delegate mocks" test guidance into one shared harness. Depends on Tracing (P-220) and Resilience (P-222) existing first per the phase's own stated dependency, because it needs to assert spans and retries, not just response shape.

5. **Write-side cache invalidation** — new `IInvalidatesCache` marker (command self-supplies `CacheKeysToInvalidate`, mirroring `ICacheableQuery<TResponse>.CacheKey`'s self-supplied pattern exactly) + `CacheInvalidationBehavior<,>` constrained to `ICommandBase`. Calls `ICacheService.RemoveAsync`/`RemoveByTagAsync` **only after `next()` succeeds** (never on failure/exception). Positioned **innermost, after `TransactionBehavior`** — eviction must follow a confirmed commit, never a speculative one. Reuses `CachingBehavior`'s existing `ICacheService` `Build()`-time guard rather than duplicating it (the guard is keyed on the dependency, not which `.AddXBehavior()` call requested it).

**Final ten-named-slot canonical order:**
```
1. Logging → 2. Metrics → 3. Tracing → 4. Validation → 5. Authorization → 6. Caching
→ 7. Resilience → 8. Idempotency → 9. Transaction → 10. CacheInvalidation
```
Steps 6 and {7,8,9,10} remain mutually exclusive by request shape (`ICacheableQuery<TResponse>` vs `ICommandBase`), exactly like the prior five/seven-step design — a single request only ever traverses one branch, not all ten.

**Status as of 2026-06-30: design-only.** `05.Application/CLAUDE.md` and `state-map.md` (Design phase D-11..D-25, 47 total new tasks across all 6 phases) are written, but Scaffold/Core/Tests/Docs/Published are still `○` pending. Before recommending or referencing any WO-036 type as if it exists in code, check `05.Application/state-map.md` Package Board first.

Root backlog IDs: P-220 (Tracing), P-221 (Streaming), P-222 (Resilience), P-223 (Test harness, depends on P-220+P-222), P-224 (Cache invalidation). All under WO-036.
