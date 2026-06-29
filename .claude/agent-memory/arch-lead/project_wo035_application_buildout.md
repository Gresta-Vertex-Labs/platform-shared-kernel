---
name: wo035-application-buildout
description: 05.Application first real build-out — P-214-P-219, seven-step pipeline (adds Authorization + Idempotency), local-seam bridging pattern reused
metadata:
  type: project
---

WO-035 dispatched the first real build-out of `05.Application` (P-214–P-219), which sat at root board `○ Not Started` despite already having a detailed, correct design pre-written into `05.Application/CLAUDE.md` and `state-map.md` (six-phase lifecycle scaffolded, zero tasks dispatched). The existing design — `ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>`, the domain-event-to-MediatR bridge fulfilling `03.Domain`'s P-081 forward reference, and five pipeline behaviors (Validation/Logging/Metrics/Transaction/Caching) — was accepted as-is, no redesign.

**Upgrade applied:** added two new opt-in pipeline behaviors to reach gold-standard before dispatch, since "is this caller allowed" and "did this exact request already run" are the two most universal CQRS cross-cutting concerns left undone after the original five:
- `AuthorizationBehavior<TRequest,TResponse>` + `IAuthorizeRequest` marker, backed by a **local** `IAuthorizationContext` seam (never `12.Security.Abstractions.IUserContext` directly — `05.Application`'s layering ceiling is `01–04`). Short-circuits with `Result.Failure(Error.Unauthorized(...))`, never throws.
- `IdempotentCommandBehavior<TRequest,TResponse>` + `IIdempotentRequest` marker, constrained to `ICommandBase` only, backed by a **local** idempotency-key seam (never `07.Messaging.Abstractions.IIdempotencyStore` directly). Mirrors that interface's `HasProcessedAsync`/`MarkProcessedAsync` shape but stays a separate, locally-owned contract.

Both follow the exact precedent `TransactionBehavior`'s local `IUnitOfWork` already established in this same domain — bridge to the real cross-domain interface at the consuming service's composition root, never a project reference from `05.Application` upward past its `01–04` ceiling. This is becoming a recurring, reusable pattern across the platform — see also [[project_wo034_cryptography_generalization]] for a different kind of "don't reach across the layering ceiling" judgment call.

**Canonical pipeline order revised to seven steps:** Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction. Authorization sits after Validation (don't spend a permission check on malformed input) and before Caching/Transaction (never let an unauthorized request reach a cache lookup or mutation). Idempotency sits innermost, wrapping the handler-and-commit boundary for commands, so a duplicate is caught before `TransactionBehavior`'s `SaveChangesAsync` runs twice for the same logical operation.

**Phase structure (6 phases, not the usual flat 6):** Split "Core" into two sequential phases — P-216 Core (Contracts: command/query vocabulary + domain-event bridge, the base package every service must reference) and P-217 Core (Behaviors: the seven-behavior suite, the opt-in package) — mirroring the WO-031/WO-032 precedent of splitting an oversized Core phase into independently-verifiable sub-phases when a domain has two packages with a clear base/opt-in split.

**Declined adding a third concern:** did NOT add a request-context/correlation-id propagation behavior here — that's already fully owned end-to-end by `14.Presentation.WebApi` middleware and `11.Communication` delegating handlers. Adding a third home for it in `05.Application` would have been scope creep, not a real gap.

Domain was `○ Not Started` on the root board → `state-map-phase` was called (now `◐ Design`). `sync-brain` added two new "What Goes Where" rows (Authorization, Idempotency) to root `CLAUDE.md` — no folder map change needed since `05.Application` was already documented at row 05.

P-214–P-219 all in `05.Application`, Work Order WO-035.
