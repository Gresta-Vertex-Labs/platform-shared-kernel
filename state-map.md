# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and open phase keys. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## ID Counters

| Counter | Next free | Note |
| --- | --- | --- |
| Next phase id | P-626 | Highest used: P-625 (WO-088) |
| Next work order | WO-089 | Highest used: WO-088 |

Phases written by `arch-lead` take the next phase id; a user-directed phase without a work order still takes one, so no id is ever reused.

## Domain Summary Board

| # | Domain | Packages | Status | Open items |
| --- | --- | --- | --- | --- |
| 00 | [Governance](tools/Governance/state-map.md) | 3 (Tooling) + dev-only Benchmarks | ● | None |
| 01 | [Core](src/Foundation/state-map.md) | 13 (10 Foundation, 3 Adapter) | ● | None |
| 02 | [Caching](src/Infrastructure/Caching/state-map.md) | 7 (1 Abstractions, 6 Adapter) | ● | None |
| 03 | [Domain](src/Model/Domain/state-map.md) | 1 (Model) | ● | None |
| 04 | [Contracts](src/Model/Contracts/state-map.md) | 1 (Model) | ● | None |
| 05 | [Application](src/Application/state-map.md) | 4 (1 Abstractions, 3 Host) | ● | None |
| 06 | [Persistence](src/Infrastructure/Persistence/state-map.md) | 6 (1 Abstractions, 5 Adapter) | ● | None |
| 07 | [Messaging](src/Infrastructure/Messaging/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ● | None |
| 08 | [Storage](src/Infrastructure/Storage/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 09 | [Search](src/Infrastructure/Search/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 10 | [Intelligence](src/Infrastructure/AI/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 11 | [Communication](src/Infrastructure/Communication/state-map.md) | 3 (Adapter) | ● | None |
| 12 | [Security](src/Hosting/Security/state-map.md) | 5 (1 Abstractions, 4 Host) | ● | None |
| 13 | [ServiceDefaults](src/Hosting/ServiceDefaults/state-map.md) | 7 (Host) | ● | None |
| 14 | [Presentation](src/Hosting/Presentation/state-map.md) | 6 (Host) + WebApi generator (Tooling) | ● | None |
| 15 | [Integration](src/Infrastructure/Integration/state-map.md) | 4 (1 Abstractions, 3 Adapter) | ● | None |
| 16 | [Testing](src/Testing/state-map.md) | 21 (Testing; 20 packable + `Testing.Internal`) | ● | None |
| 17 | [Workflows](src/Infrastructure/Workflows/state-map.md) | 1 (Adapter) | ● | None |
| 18 | [Idempotency](src/Infrastructure/Idempotency/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 19 | [Scheduling](src/Infrastructure/Scheduling/state-map.md) | 1 (Adapter) | ● | None |
| 20 | [Reporting](src/Infrastructure/Reporting/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ● | None |
| — | [samples](samples/README.md) | `Shop`: one Aspire platform, 7 services (Catalog, Ordering, Inventory, Billing, Merchant, Notify, Reports) | ● | None |

Every package ships at one repo-wide version through the release train (`CONTRIBUTING.md`, "Versioning and releases"); nothing is released per package.

## Open Work

### P-578 — Retire old package IDs (WO-086)

**Status:** `○` Not started — ready: v1.0.0 is on the feed (2026-10-21); approved by the user 2026-09-26: delete them after the release
**Domain:** cross-domain (feed housekeeping)
**Depends on:** P-577 (done: v1.0.0, 2026-10-21)

**What is needed**

Now that v1.0.0 is on the feed, delete the package IDs that WO-086 and the pre-publish passes replaced (for example `SharedKernel.Application.Abstractions`, `SharedKernel.Application.Behaviors`, `SharedKernel.Application.Behaviors.Caching`, `SharedKernel.Guards`, `SharedKernel.Persistence.PostgreSQL`, the probe-only `SharedKernel.ServiceDefaults.*` packages). Confirm the exact list with the user before deleting anything.

**Acceptance**

- Every retired ID is either deleted from the feed or explicitly kept by the user's decision.
- No retired ID is still referenced by any project, sample or README.

## Blocked

None.
