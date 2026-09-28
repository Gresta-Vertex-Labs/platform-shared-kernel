# 05.Application — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Application` | Abstractions | ● | MediatR-free kernel contracts: `IRequest<T>`/`IRequestHandler<,>`, `ICommand`/`IQuery<T>`/`IStreamQuery<T>`, `ISender`, `IPipelineBehavior<,>`, `IRequestValidator<T>`, `IDomainEventHandler<T>`, `[RequirePermission]` and every pipeline marker. |
| `SharedKernel.Application.Pipeline` | Host | ● | `AddSharedKernelApplication(assemblies, app => …)` — one call, seams checked at host start, second call throws; fixed pipeline Tracing → Logging → Metrics → Authorization → Validation → query stage → command stage (`WithIdempotency()`, `WithAuditing()`, `WithTransactions()`); behaviors internal. |
| `SharedKernel.Application.Pipeline.Caching` | Host | ● | `app.WithCaching()` — `ICacheableQuery` caching and post-commit `IInvalidatesCache` eviction. |
| `SharedKernel.Application.Mediator.MediatR` | Host | ● | `app.UseMediatR()` — MediatR 12.4.1 (last MIT release) behind `ISender`; the only MediatR reference in the repository. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.05.Design` | Design (WO-035, WO-036, WO-038, WO-039, WO-040, WO-041, WO-058, WO-071) | ● |
| `SK.05.Scaffold` | Scaffold (same work orders) | ● |
| `SK.05.Core` | Core (same work orders, plus WO-080 C-88) | ● |
| `SK.05.Tests` | Tests (same work orders, plus WO-080 T-79/T-80) | ● |
| `SK.05.Docs` | Docs (same work orders, plus WO-080 DO-31) | ● |
| `SK.05.Published` | Published (P-30 board-update task superseded by this rewrite) | ● |
| `SK.05.P544` | P-544 Pre-Publish Redesign (BREAKING) — published 2026-09-19; cross-domain migrations C-90–C-97 superseded by WO-086 | ● |
| `SK.05.WO086` | WO-086 foundation refactor (kernel mediator, Execution contracts, unified idempotency) | ● |
| `SK.05.P563` | P-563 one application model with a thin HTTP edge (main) — A1/A3 superseded, PUB replaced by the release train | ● |
| `SK.05.P579` | P-579 main's application model re-implemented on the WO-086 packages | ● |

## Open Work

None — every phase in this domain is complete. The first public release of the four packages ships with root P-577 (release train); root P-578 then retires the old package IDs (`SharedKernel.Application.Behaviors`, `.Behaviors.Caching`, `.Abstractions`) on the feed.

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- P-579 ● `origin/main`'s application model re-implemented on the WO-086 packages — `[RequirePermission]`, one `AddSharedKernelApplication(…)` call, `app.UseMediatR()`/`app.WithCaching()`, per-tenant-and-caller idempotency (2026-09-26)
- WO-086 ● Foundation refactor — `Application.Abstractions` deleted (contracts to `SharedKernel.Execution`), MediatR-free kernel contracts, `.Behaviors` → `.Pipeline`, `.Mediator.MediatR`, idempotency over `IIdempotencyStore`, test doubles to `SharedKernel.Application.Testing` (P-564, P-565, P-567, P-568, P-571, P-575) (2026-09-26)
- P-563 ● One application model with a thin HTTP edge (main; merged by P-579, A1/A3 superseded) (2026-09-24)
- P-556 ● `SharedKernel.Application.Behaviors.Caching` pre-first-publish pass — published `1.0.0-alpha.0.1116` (2026-09-19)
- P-544 ● `SharedKernel.Application`/`.Behaviors` pre-first-publish redesign — published `1.0.0-alpha.0.1116`; later replaced by WO-086's packages (2026-09-19)
- P-488 ● Fix `CacheInvalidationBehavior` pre-commit eviction ordering (WO-080) (2026-09-04)
- P-458 ● Opt-in `AuditingBehavior` and audit-writer seam (WO-071) (2026-09-04)
- P-380 ● Dual-control (maker-checker) authorization (WO-058; removed by P-544) (2026-08-17)
- P-253 ● Logging retrofit to `[LoggerMessage]` (WO-041)
- P-246 ● Opt-in structured request/response payload logging with self-supplied redaction (WO-040)
- P-237–P-243 ● WO-039 — reflection-free failure factory, fire-and-forget fix, metrics outcome tag, governance tests on real assemblies, idempotency replay, DX preset
- P-231–P-234 ● WO-038 — bug fixes, contract evolution, parallel domain events and fire-and-forget (both removed by P-544), stream behavior coverage
- P-220–P-224 ● WO-036 — tracing parity, streaming query vocabulary, resilience behavior (removed by P-544), pipeline test harness, write-side cache invalidation
- P-214–P-219 ● WO-035 — the domain build: design, scaffold, contracts, seven-step behavior suite, tests, docs and packaging
- P-015 ● `ICacheableQuery` marker and `CachingBehavior` (WO-004)

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] P-579 — `SK.05.P579` recorded ●; `SK.05.P563` closed with A1/A3 superseded; `origin/main` merged into the WO-086 branch
- [2026-09-26] WO-086 (P-564, P-565, P-567, P-568, P-571, P-575) — `SK.05.WO086` recorded ●; `SharedKernel.Application.Abstractions` deleted, contracts moved to `01.Core/SharedKernel.Execution`
- [2026-09-15] Root P-544 — `SK.05.P544` opened and implemented (D-90/S-24/C-89/T-81/DO-32 ●); publish followed on 2026-09-19 with P-556
- [2026-09-04] C-88 / T-79 / T-80 / DO-31 ● (WO-080/P-488) — post-commit eviction ordering fixed
