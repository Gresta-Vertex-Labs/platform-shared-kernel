# 03.Domain — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Domain` | Model | ● | `Entity`, `AggregateRoot` with audit/soft-delete/tenanted bases (`IHasTenant.TenantId` is a `TenantId`), `ValueObject`, `StronglyTypedId`, `DomainEvent`, `IBusinessRule`, `IPolicy`, specifications with offset/keyset paging, `Money`. References `SharedKernel.Execution`; logging-free; never references `SharedKernel.Contracts`. Ships with the repo-wide release train. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.03.Design` | Design | ● |
| `SK.03.Scaffold` | Scaffold | ● |
| `SK.03.Core` | Core | ● |
| `SK.03.Tests` | Tests | ● |
| `SK.03.Docs` | Docs | ● |
| `SK.03.Published` | Published | ● |
| `SK.03.P540` | P-540 Pre-First-Publish Gold-Standard Pass (BREAKING) | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — Model tier; references `SharedKernel.Execution`; `IHasTenant.TenantId` and every `Tenanted…AggregateRoot` use `TenantId` (P-565, P-567, P-574, P-575) (2026-09-26)
- P-540 ● `SharedKernel.Domain` pre-first-publish gold-standard pass — 14 defects fixed, public API tracked; follow-ups P-541 (06.Persistence) and P-542 (00.Governance) (2026-09-15)
- P-509 ● Re-point `SharedKernel.Domain` from `SharedKernel.Guards` to `SharedKernel.Core` (WO-082) (2026-09-10)
- P-439 ● `Money` — currency-aware monetary value object (WO-066) (2026-09-02)
- P-313 ● Ad hoc `Specification<T>.Create(criteria)` factory (WO-051)
- P-312 ● `IPolicy<T>` non-compliance reason surface (WO-051)
- P-311 ● Constructor guard-clause adoption and reflection-caching hardening (WO-051)
- P-310 ● `ValueObject` `Result<T>` creation helper (WO-051)
- P-309 ● `IHasAggregateId<TId>` domain-event correlation marker (WO-051)
- P-308 ● Specification query-shape extensions — keyset/cursor paging, `AsSplitQuery` (WO-051)
- P-307 ● Composite specification include propagation fix (WO-051)
- P-152 ● Generic STJ converter for `StronglyTypedId<TValue>` (WO-024)
- P-095 ● `IncludeDeleted` flag on `ISpecification` (WO-016)
- P-081 ● `IDomainEventDispatcher` interface (WO-014)
- P-054 ● Aggregate `Result`-returning factory pattern — `IAggregateFactory<T>` (WO-011)
- P-053 ● `DomainEventVersion` attribute (WO-011)
- P-052 ● Specification builder ergonomics (WO-011)
- P-051 ● `PagedSpecification<T>` (WO-011)
- P-050 ● Specification sentinels — `AllSpecification`/`EmptySpecification` (WO-011)
- P-049 ● `DomainException` base (WO-011)
- P-048 ● `IHasVersion` (WO-011)
- P-047 ● `DomainService` base (WO-011)
- P-046 ● `SingleValueObject<TValue>` (WO-011)
- P-045 ● `IHasDomainEvents` (WO-011)
- P-044 ● `TenantedAggregateRoot` family (WO-010)
- P-043 ● `BusinessRuleViolationException` classification fix (WO-010)
- P-036–P-041 ● WO-009 architectural audit (auditable hierarchy, `IsSatisfiedBy`, typed event payload, `AsNoTracking`, `AggregateRoot.Now` guard)
- P-032 ● DDD building blocks for `SharedKernel.Domain` (WO-008)

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 (P-565, P-567, P-574, P-575) — no new tasks: Model tier, `SharedKernel.Execution` reference, `TenantId` on every tenanted aggregate (breaking)
- [2026-09-15] P-540 complete (13/13): `SharedKernel.Domain` published as `1.0.0-alpha.0.923` from `b45b61b`; ConsumerVerify 28/28 against the feed
- [2026-09-15] SK.03.P540 opened and implemented — 14 execution-confirmed defects fixed, user-ruled API decisions applied, 511 tests
- [2026-09-10] P-509 (WO-082) — `SharedKernel.Domain` re-pointed from the deleted `SharedKernel.Guards` to `SharedKernel.Core`; `SharedKernel.Guards` C# namespaces preserved
