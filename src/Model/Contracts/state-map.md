# 04.Contracts — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Contracts` | Model | ● | Cross-service wire contracts: `IIntegrationEvent` + `[IntegrationEvent]`, CloudEvents `EventEnvelope<TEvent>` (built only by `EventEnvelope.Wrap`), `PagedList<T>`, `CursorPagedList<T>`, `PageRequest`/`CursorPageRequest`, `PageCursor`. Never references `SharedKernel.Domain`. Ships with the repo-wide release train. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.04.Design` | Design | ● |
| `SK.04.Scaffold` | Scaffold | ● |
| `SK.04.Core` | Core | ● |
| `SK.04.Tests` | Tests | ● |
| `SK.04.Docs` | Docs | ● |
| `SK.04.Published` | Published | ● |
| `SK.04.P543` | P-543 Pre-First-Publish Redesign (BREAKING) | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — Model tier declared; `EventEnvelope.TenantId` stays `Guid?` on the wire; no code change in this domain (P-565, P-574, P-575) (2026-09-26)
- P-543 ● `SharedKernel.Contracts` pre-first-publish redesign — CloudEvents envelope over `IIntegrationEvent`, `Envelope`/mapping/serializer context and the `03.Domain` reference removed, `long` totals, page requests and cursor codec (2026-09-15)
- P-332 ● Cross-service cursor-paginated response contract (WO-052) (2026-07-31)
- P-331 ● Optional tenant propagation on `EventEnvelope<TEvent>` (WO-052) (2026-07-31)
- P-328 ● Eliminate the `Envelope` namespace/type collision (WO-052) (2026-07-31)
- P-314 ● Correct the `EventEnvelope` XML doc `AggregateId` claim (WO-051) (2026-07-30)
- P-166 ● `Result<T>` ↔ `Envelope<T>` mapping extensions (WO-026; removed by P-543) (2026-06-18)
- P-058–P-062 ● Scaffold, Core, Tests, Docs, Published (WO-012) (2026-05-30)
- P-055 ● `EventEnvelope<TEvent>` transport metadata wrapper (WO-011) (2026-05-30)

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 (P-565, P-574, P-575) — no new tasks and no code change: Model tier declared (enforced by the build), `ContractsNeverReferencesDomain` kept as a same-tier purity rule
- [2026-09-15] P-543 complete (10/10): `SharedKernel.Contracts` published as `1.0.0-alpha.0.935` from `9f3ee5f`; ConsumerVerify 5/5 against the feed
- [2026-09-15] SK.04.P543 opened and implemented — 10 execution-confirmed defects fixed, integration-event CloudEvents envelope with required `[IntegrationEvent]`
- [2026-07-31] WO-052 (P-328/P-331/P-332) closed across all six phases — `Envelopes` namespace rename, `EventEnvelope<TEvent>.TenantId`, `CursorPagedList<T>`
