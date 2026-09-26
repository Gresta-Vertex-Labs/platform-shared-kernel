---
name: project_p544_stateless_store_followup
description: 2026-09-15 same-day follow-up to P-544 — made IRequestIdempotencyStore's CompleteAsync/ReleaseAsync take an explicit reservationToken and return bool, so both 18.Idempotency providers hold zero reservation state of their own.
type: project
---

> WO-086 (2026-09): `IRequestIdempotencyStore`/`IdempotencyBeginResult` are now `SharedKernel.Idempotency.Abstractions.IIdempotencyStore`/`IdempotencyReservation`; `SharedKernel.Application.Behaviors` is `SharedKernel.Application.Pipeline` (no MediatR); `FakeRequestIdempotencyStore` is `FakeIdempotencyStore` in `SharedKernel.Idempotency.Testing`; `SharedKernel.Testing.SelfTests` test counts below are historical. The token-round-trip pattern still holds.

**What happened:** Same day as [[project_p544_request_idempotency_migration]] shipped, the coordinator asked
for a follow-up refinement to the still-unpublished `IRequestIdempotencyStore` contract. The problem: both
providers kept a per-instance `ConcurrentDictionary<string, token>` remembering which key its own
`TryBeginAsync` had won, so `CompleteAsync`/`ReleaseAsync` from a *different* store instance than the one
that reserved the key were always a silent no-op — stateful stores, and a real correctness trap if a
consumer ever resolved a fresh scoped instance between reserve and confirm (unlikely in the normal
MediatR pipeline, but not structurally prevented).

**The fix — pushed the token round trip up into the contract itself:**
- `IdempotencyBeginResult` gained `ReservationToken` (string?, non-null exactly when `Status == Started`).
  `Started()` became `Started(string reservationToken)`.
- `IRequestIdempotencyStore.CompleteAsync`/`ReleaseAsync` both gained a `string reservationToken` parameter
  and changed return type from `Task` to `Task<bool>` — `true` when the token still owned the reservation
  and the mutation applied, `false` (never a thrown exception) otherwise.
- `IdempotencyBehavior<TRequest,TResponse>` now passes `begin.ReservationToken!` through explicitly, gained
  an `ILogger<IdempotencyBehavior<TRequest,TResponse>>` constructor parameter, and logs a `Warning`
  (new EventId 5120, `ApplicationBehaviorsLoggingEventIds.LogCompleteReservationLost`) when `CompleteAsync`
  returns `false` after a successful handler run — the response is still returned regardless, since the
  work already happened. A `false` from `ReleaseAsync` needs no log.
- Both `18.Idempotency` providers removed their `ConcurrentDictionary` entirely — see
  [[reference_redis_reservation_protocol]] / [[reference_efcore_reservation_protocol]] for the exact
  before/after shape. `.EfCore`'s `CompleteAsync` also gained the `Status == InProgress` guard it had been
  missing (parity with `ReleaseAsync`, and with `.Redis`'s `CompleteScript`, which got the same guard).
- `16.Testing`'s `FakeRequestIdempotencyStore` was migrated onto the new signature the same way (token
  stored per-entry inside the fake's own dictionary, since a fake legitimately needs to simulate a real
  backing store's state — this is not the same as the removed *caller-tracking* dictionary the two real
  providers had).

**A second, unrelated bug found and fixed in the same pass (coordinator-flagged mid-task):**
`ApplicationBehaviorsBuilder.Build()` registers `CommandScopeBehavior` and (now) `IdempotencyBehavior`, both
of which constructor-inject `ILogger<T>`, but `Build()` only ever called `services.AddMetrics()`, never
`services.AddLogging()`. A bare `ServiceCollection` + `AddMediatR` + `Build()` with idempotency or a custom
command-stage behavior active but no prior `AddLogging()` call would fail to resolve `ILogger<T>` at the
*first dispatch*, not at `Build()` time. Fixed by calling `services.AddLogging()` unconditionally
(idempotent, safe to call multiple times) at the top of `Build()`. Required adding a real
`Microsoft.Extensions.Logging` package reference to `SharedKernel.Application.Behaviors.csproj` — the
project previously only referenced `Microsoft.Extensions.Logging.Abstractions`, which does not carry the
`AddLogging()` DI extension method (that lives in the concrete `Microsoft.Extensions.Logging` package's
`LoggingServiceCollectionExtensions`). Proven by a new test,
`PipelineOrderTests.Build_WithIdempotencyAndTransaction_NoPriorAddLogging_DispatchesSuccessfully`, which
deliberately never calls `services.AddLogging()` anywhere.

**Test results, all real (not mocked):**
- `SharedKernel.Application.Behaviors.Tests`: 56/56 (up from ~52 — net-new: two lost-reservation
  behavior tests, one no-prior-AddLogging dispatch test).
- `SharedKernel.Testing.SelfTests`: 1273/1273 (the full slow suite, run in full — includes the migrated
  `FakeRequestIdempotencyStoreTests`).
- `SharedKernel.Idempotency.Redis.Tests`: 33/33 against real Testcontainers Redis (up from 31 — net-new:
  two foreign-token tests; several existing tests renamed to `..._ReturnsFalse`/`..._ReturnsTrue` variants
  to assert the new bool return alongside existing behavior).
- `SharedKernel.Idempotency.EfCore.Tests`: 35/35 against real Testcontainers PostgreSQL (up from 32 —
  net-new: two foreign-token tests, one non-Guid-token test).

**PublicAPI.Unshipped.txt regeneration:** since `05.Application.Behaviors` is pre-publish, RS0016/RS0017
were fixed by hand-editing the file to match exactly what the RS0016 diagnostics reported (never run a
code-fix tool for this — the diagnostic message itself is the exact line to add/replace). Order matters:
build once to see every RS0016 (new public member not in the file) and RS0017 (stale entry no longer
matching any real member) error, patch the file, rebuild until 0 errors.

**If you touch this contract again:** the pattern now established — "the winning call returns an opaque
token; every subsequent mutating call must present it; the store never remembers who it gave a token to" —
is the reusable shape for any future reserve/confirm-or-release seam on this platform, not just this one.
It is strictly better than the per-instance-dictionary shape because it removes a whole class of
cross-instance/cross-scope staleness bugs by construction rather than by convention.
