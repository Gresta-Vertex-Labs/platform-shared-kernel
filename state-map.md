# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and phase keys. Completed phase and work-order detail is archived outside the repository; `git log` records every change.

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
| — | [samples](samples/README.md) | `Shop`: one Aspire platform, 7 services (Catalog, Ordering, Inventory, Billing, Merchant, Notify, Reports) | ● | None |

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

## Completed Work Orders

- WO-086 ● Foundation refactor — tiered packages, `SharedKernel.Execution` context, kernel mediator abstraction, unified idempotency and readiness probes, optional-dependency satellites, packable `*.Testing` packages, release train, samples as reference architecture (P-562–P-576, P-579; P-577/P-578 open)
- WO-085 ● Messaging pre-first-publish pass and first publish (P-560, P-561)
- — ● User-directed pre-first-publish passes, no work order (P-529, P-530, P-538–P-557, P-558, P-559)
- WO-084 ● ServiceDefaults per-integration package split (P-531–P-537)
- WO-083 ● 01.Core gold-standard audit fixes and TOTP cascades (P-510–P-528)
- WO-082 ● `SharedKernel.Guards` merged into `SharedKernel.Core` (P-505–P-509)
- WO-081 ● Cryptography redesign — AAD, sync-provider gate, async signing, Argon2, Key Vault hardening, consumer cascades (P-491–P-504)
- WO-080 ● Coordinated-pass findings — Key Vault probe, post-commit eviction ordering and locks (P-487–P-490)
- WO-079 ● Mapperly endorsed, reflection-based mapping forbidden (P-486)
- WO-078 ● Localization (P-482–P-485)
- WO-077 ● Reporting (P-477–P-481)
- WO-076 ● DataPrivacy (P-474–P-476)
- WO-075 ● Tenant catalog (P-471–P-473)
- WO-074 ● gRPC server error mapping (P-468–P-470)
- WO-073 ● Scheduling (P-464–P-467)
- WO-072 ● Notifications — SendGrid email, Twilio SMS (P-460–P-463)
- WO-071 ● Append-only audit trail (P-456–P-459)
- WO-070 ● Idempotency stores — Redis, EF Core (P-454, P-455)
- WO-069 ● TOTP/HOTP and step-up (P-451–P-453)
- WO-068 ● Async key provider, envelope encryption, Azure Key Vault (P-446–P-450)
- WO-067 ● Format validators (P-443–P-445)
- WO-066 ● `Money` (P-439–P-442)
- Earlier work orders (WO-001–WO-065, 437 phases P-001–P-438: 422 ●, 15 ⊘) — archived.

## Changelog

- [2026-09-28] Root and domain state maps slimmed to living boards for the public release; ID counters recomputed (next P-580, WO-087); P-544 and P-557 closed — their remaining publish steps are superseded by the P-577 release train
- [2026-09-26] P-579 ● — `origin/main`'s application model (P-563) merged onto WO-086's packages (05.Application, 14.Presentation, samples)
- [2026-09-26] WO-086 P-562–P-576 ● — tier enforcement, `SharedKernel.Execution`, tenant/caller unification, context propagation, kernel mediator, unified idempotency, readiness probes, satellites, `*.Testing` packages, release train and CI, samples, governance cleanup, docs, agents and commands (all domains)
- [2026-09-23] P-560, P-561 ● — `07.Messaging` pre-first-publish pass; both packages published at `1.0.0-alpha.0.1171` (07.Messaging)
- [2026-09-23] `09.Search` pre-publish gold-standard pass and `samples/CatalogApi` against real engines (09.Search, 13.ServiceDefaults)
- [2026-09-22] P-559 ● — `08.Storage` pre-publish redesign verified through `samples/DocumentsApi` (08.Storage, 20.Reporting)
- [2026-09-20] P-557 docs pass — `src/Infrastructure/Persistence/CLAUDE.md` and state map brought current with the 7-package split (06.Persistence)
- [2026-09-19/20] P-557 recorded — `06.Persistence` redesigned before its first publish (06.Persistence, 05.Application, 13.ServiceDefaults, 00.Governance, 03.Domain)
- [2026-09-19] P-556 published — `SharedKernel.Application.Behaviors.Caching` `1.0.0-alpha.0.1116`, with `.Application` and `.Application.Behaviors` republished at the same height (05.Application, 01.Core, 02.Caching, 03.Domain)
- [2026-09-18] P-555 published — `SharedKernel.FeatureManagement` `1.0.0-alpha.0.1112`; every `01.Core` package published (01.Core)
