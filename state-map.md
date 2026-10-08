# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and open phase keys. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## ID Counters

| Counter | Next free | Note |
| --- | --- | --- |
| Next phase id | P-580 | Highest used: P-579 (WO-086) |
| Next work order | WO-087 | Highest used: WO-086 |

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
| 06 | [Persistence](src/Infrastructure/Persistence/state-map.md) | 6 (1 Abstractions, 5 Adapter) | ● | None — never published; first release is P-577 |
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
| — | [samples](samples/README.md) | 8: OrderApi, BillingApi, ShippingApi, DocumentsApi, CatalogApi, CheckoutApi, InventoryApi, Shop (Aspire platform) | ● | None |

Every package ships at one repo-wide version through the release train (`CONTRIBUTING.md`, "Versioning and releases"); nothing is released per package.

## Open Work

### P-577 — First release train (WO-086)

**Status:** `○` Not started
**Domain:** cross-domain (every packable project)
**Depends on:** P-562–P-576, P-579 (all ●)

**What is needed**

Cut the first release of the WO-086 package set: `git tag vX.Y.Z` on `main`, letting `release.yml` run every gate (tier check, full build, Unit and Integration suites, every consumer-verify harness and sample), pack all packages at that one version, check the set against every packable project, and publish them together to GitHub Packages.

**Acceptance**

- One tag, one version on every published package; no `<Version>` in any `.csproj`.
- The published set equals the set of packable projects (including the per-capability `*.Testing` packages and the Tooling packages).
- `06.Persistence` and the other never-published or re-shaped packages (`SharedKernel.Execution`, `Application.Pipeline`/`.Pipeline.Caching`/`.Mediator.MediatR`, the MassTransit satellites, `Presentation.Core`, `Idempotency.*`) appear on the feed for the first time.
- This supersedes every per-package "publish pending" task left in older records (P-557's P-17, P-563's PUB, P-544's cross-domain follow-ups).

### P-578 — Retire old package IDs (WO-086)

**Status:** `○` Not started — approved by the user 2026-09-26: delete them after the release
**Domain:** cross-domain (feed housekeeping)
**Depends on:** P-577

**What is needed**

After P-577 is on the feed, delete the package IDs that WO-086 and the pre-publish passes replaced (for example `SharedKernel.Application.Abstractions`, `SharedKernel.Application.Behaviors`, `SharedKernel.Application.Behaviors.Caching`, `SharedKernel.Guards`, `SharedKernel.Persistence.PostgreSQL`, the probe-only `SharedKernel.ServiceDefaults.*` packages). Confirm the exact list with the user before deleting anything.

**Acceptance**

- Every retired ID is either deleted from the feed or explicitly kept by the user's decision.
- No retired ID is still referenced by any project, sample or README.

## Blocked

None.
