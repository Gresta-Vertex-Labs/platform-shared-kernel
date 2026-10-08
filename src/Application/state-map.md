# 05.Application — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release of the four packages ships with root P-577 (release train); root P-578 then retires the old package IDs (`SharedKernel.Application.Behaviors`, `.Behaviors.Caching`, `.Abstractions`) on the feed.

## Blocked

None.

## Cross-Domain Dependencies

None open.
