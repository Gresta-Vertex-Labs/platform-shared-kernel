# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and open phase keys. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## ID Counters

| Counter | Next free | Note |
| --- | --- | --- |
| Next phase id | P-639 | Highest used: P-638 (WO-091) |
| Next work order | WO-092 | Highest used: WO-091 |

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

### WO-091 — Shop Catalog and Ordering on the service template's folder layout (1.2.0)

Target 1.2.0. Kernel 1.1.0 ships the service template's nine `ServicePlacementRules`, but the Shop's Catalog and Ordering apply only three of them (middleware and filters, generic folder names, exception types): their use cases sit in feature files rather than one namespace per use case, and Ordering's consumer sits outside `Infrastructure.Messaging.Consumers`. Accept as a samples change; no kernel package or public API changes.

#### P-638 — Restructure Shop Catalog and Ordering to the template's folder layout
**Status:** `○` Pending
**Domain:** samples
**Depends on:** None
**Phase key:** —
Restructure Shop Catalog and Ordering to the service template's folder layout (one folder per use case, the template's "Where things go" table) so all nine `ServicePlacementRules` apply; then the recipes' `proven-by` snippets match the layout every service uses.
- [ ] Catalog and Ordering follow the template's "Where things go" table: one namespace and folder per command or query use case, domain types under `Domain.{Feature}` (with `Rules`, `Specifications`), domain-event handlers under `Application.{Feature}.EventHandlers`, EF configurations under `Infrastructure.Persistence.Configurations`, consumers under `Infrastructure.Messaging.Consumers`, endpoint modules under `Api.Endpoints`.
- [ ] `CatalogArchitectureTests` and `OrderingArchitectureTests` apply all nine `ServicePlacementRules` and pass; the other Shop tests and the E2E flows still pass.
- [ ] Every consumer-skill recipe snippet and `proven-by` path that cites a moved Shop file matches it again; `generate-package-index.cs --check` and the consumer-skill checks stay green.

## Blocked

None.
