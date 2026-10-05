# 18.Idempotency — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Idempotency.Abstractions` | Abstractions | ● | The one purpose-keyed `IIdempotencyStore` (`IdempotencyPurpose.Request`/`.Message`), `IdempotencyReservation`, `IdempotencyTenantScope` (`no-tenant`), `AddIdempotencyStore<T>`/`HasIdempotencyStore`/`GetRequiredIdempotencyStore`. Consumed by `05`'s `IdempotencyBehavior` and `07`'s consumer idempotency. |
| `SharedKernel.Idempotency.Redis` | Adapter | ● | `RedisIdempotencyStore` (atomic Lua) for every purpose; `AddRedisIdempotency(p => …, o => …)`; message entries are hashes. Built on `Caching.Redis.Core` (declared adapter edge). |
| `SharedKernel.Idempotency.EfCore` | Adapter | ● | `EfCoreIdempotencyStore` (`INSERT … ON CONFLICT`); `AddEfCoreIdempotency(db => …, p => …, o => …)`; table keyed `(tenant_scope, purpose, key)`. Built on `Persistence.EfCore` (declared adapter edge). |

Test double: `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` in `src/Testing/SharedKernel.Idempotency.Testing`. Provider tests run in the Integration lane against real Redis/PostgreSQL.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.18.Design` | Design (D-01–D-09; WO-070, P-454/P-455) | ● |
| `SK.18.Scaffold` | Scaffold (S-01–S-08) | ● |
| `SK.18.Core` | Core (C-01–C-10) | ● |
| `SK.18.Tests` | Tests (T-01–T-10) — the WO-070 rows left open were closed by WO-086 FD-06 (provider tests in the Integration lane) | ● |
| `SK.18.Docs` | Docs (DO-01–DO-06) | ● |
| `SK.18.Published` | Published (P-01–P-05) — per-package publish rows superseded by the repo-wide release train | ● |
| `SK.18.WO086` | WO-086 foundation refactor — unified idempotency contract (FD-01–FD-07; P-565, P-568, P-571, P-572, P-575) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (The domain needs no readiness probe of its own: Redis and database connectivity are covered by the `redis` probe and the persistence readiness checks.)

## Completed Phases

- WO-086 ● Foundation refactor — `Idempotency.Abstractions` owns `IIdempotencyStore`; one store class per provider; tenant from `IRequestContextAccessor`; persisted formats changed (P-565, P-568, P-571, P-572, P-575) (2026-09-26)
- Bug fix ● Redis confirm/store-response race (T-03) found by the first real Integration-lane run (2026-09-09)
- SK.18.Docs ● Package READMEs and CLAUDE.md (2026-09-04)
- SK.18.Core ● Redis and EF Core stores implemented, zero warnings (2026-09-04)
- SK.18.Scaffold ● Both provider projects scaffolded (2026-09-04)
- SK.18.Design ● D-01–D-09 (WO-070, P-454/P-455) (2026-08-26)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] Consumer idempotency keys are `{MessageId:D}:{sha256-hex("{endpoint path}|{consumer type}")}` — one reservation per consumer (fixed in `07.Messaging`'s `IdempotentConsumerBehavior`).
- [2026-09-26] WO-086 recorded ● — `SharedKernel.Idempotency.Abstractions` added; WO-070 "no .Abstractions here" rule retired.
- [2026-09-09] Bug fix: Redis confirm-then-store-response ordering race, reproduced deterministically and fixed.
- [2026-09-04] Implementation session: Design/Scaffold/Core/Docs ●; Testcontainers suites executed 25/25 per provider.
