# 18.Idempotency — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Idempotency.Abstractions` | Abstractions | ● | The one purpose-keyed `IIdempotencyStore` (`IdempotencyPurpose.Request`/`.Message`), `IdempotencyReservation`, `IdempotencyTenantScope` (`no-tenant`), `AddIdempotencyStore<T>`/`HasIdempotencyStore`/`GetRequiredIdempotencyStore`. Consumed by `05`'s `IdempotencyBehavior` and `07`'s consumer idempotency. |
| `SharedKernel.Idempotency.Redis` | Adapter | ● | `RedisIdempotencyStore` (atomic Lua) for every purpose; `AddRedisIdempotency(p => …, o => …)`; message entries are hashes. Built on `Caching.Redis.Core` (declared adapter edge). |
| `SharedKernel.Idempotency.EfCore` | Adapter | ● | `EfCoreIdempotencyStore` (`INSERT … ON CONFLICT`); `AddEfCoreIdempotency(db => …, p => …, o => …)`; table keyed `(tenant_scope, purpose, key)`. Built on `Persistence.EfCore` (declared adapter edge). |

Test double: `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` in `src/Infrastructure/Idempotency/SharedKernel.Idempotency.Testing`. Provider tests run in the Integration lane against real Redis/PostgreSQL.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

| Domain | Direction | What |
| --- | --- | --- |
| 01.Core | inbound, **open** | `ErrorCodes.Idempotency.StoreUnavailable` = `idempotency.store_unavailable` (additive constant; the store's outage code, `ErrorType.Unavailable`). Also the P-580 helpers (`TelemetryVersion.Of`, `ActivityFailureExtensions`, `WellKnownOutcomes`, `WellKnownTagKeys.Outcome`, `IHasError`, `LoggingEventIdRanges.PackageSubBlockWidth`) and P-584 (`Execution` references `Core`; this phase states the reference explicitly) |
| 05.Application | outbound / coordination | `SK.05.PipelineTelemetryGaps` already passes a `SharedKernelException`'s `Error` through unchanged and keeps the catch-all for third-party stores; nothing there waits on this phase. Its tests may use the fake's outage switch instead of a hand-rolled faulting store; `IdempotencyBehavior` need not name the new type |
| 07.Messaging | outbound / note | The consumer idempotency behavior now sees `IdempotencyStoreUnavailableException` instead of a raw Redis/Npgsql exception from a fail-closed store; retry classification and any `catch` on the raw types is 07's to review |
| 13.ServiceDefaults | inbound | `SK.13.WildcardTelemetry` subscribes `SharedKernel.*`, so the new source and meter need no registration |
| 16.Testing | inbound / outbound | `SK.16.TelemetryCapture` helpers are used by every telemetry test; the `Idempotency.Testing` catalogue row in `src/Testing/CLAUDE.md` gains the outage switch (note for the testing planner) |
| 00.Governance | inbound | `SK.00.ObservabilityReportOnly` (P-583) measures "zero findings" |

The domain needs no readiness probe of its own: Redis and database connectivity are covered by the `redis` probe and the persistence readiness checks.
