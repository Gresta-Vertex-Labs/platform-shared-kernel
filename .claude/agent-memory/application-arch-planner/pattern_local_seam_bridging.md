---
name: pattern_local_seam_bridging
description: How 05.Application behaviors reach infrastructure after WO-086 — through shared contracts in lower tiers (Execution, Idempotency.Abstractions, Caching.Abstractions) or ports in SharedKernel.Application, never through an Adapter; plus the Build() guard and sibling-capability techniques
metadata:
  type: project
---

> WO-086 (2026-09): the per-domain "local seam bridged at the composition root" pattern is gone. `IUnitOfWork`/`IRequestContext`/`IAuditTrailWriter` are `SharedKernel.Execution` (Foundation); the idempotency store is `SharedKernel.Idempotency.Abstractions.IIdempotencyStore`; `IAuthorizationContext`, `IIdempotencyKeyStore`, `IRequestIdempotencyStore`, `IDualApprovalStore` and every composition-root adapter were deleted.

A pipeline behavior that needs infrastructure depends on a **contract in a tier every package may reference**, and the infrastructure implements that contract directly — no adapter in between:

| Behavior | Contract | Lives in | Implemented by |
| --- | --- | --- | --- |
| `AuthorizationBehavior` | `IRequestContext` | `SharedKernel.Execution.Context` (Foundation) | `13.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` |
| `TransactionBehavior` | `IUnitOfWork` (`ExecuteInTransactionAsync`, `OnBeforeCommit`) | `SharedKernel.Execution.Transactions` | `06.Persistence.EfCore` |
| `AuditingBehavior`/`AuditingCommitBehavior` | `IAuditTrailWriter`/`AuditEntry` | `SharedKernel.Execution.Auditing` | `06.Persistence.EfCore.Auditing` |
| `IdempotencyBehavior` | `IIdempotencyStore` keyed by `IdempotencyPurpose.Request` | `SharedKernel.Idempotency.Abstractions` | `18.Idempotency` (`AddRedisIdempotency`/`AddEfCoreIdempotency`) |
| `CachingBehavior`/`CacheInvalidationBehavior` | `ICacheService`, `ITenantCacheKeyProvider` | `SharedKernel.Caching.Abstractions` | `02.Caching.FusionCache` |
| `ValidationBehavior` | `IRequestValidator<T>` (a port) | `SharedKernel.Application.Validation` | hand-written, or `AddFluentValidationRequestValidators()` |

**Why:** the shared contract is the platform's single definition, so `06.Persistence` can read what the pipeline resolved (and vice versa). A domain-local copy is what forced the old adapters, and redeclaring one is refused by `UnitOfWorkSeamRules.SharedContractsAreNotRedeclared`.

**How to apply when planning a new behavior that needs infrastructure:**
1. Look for an existing contract in Foundation/Abstractions tier first. If none exists and the capability is application-owned, add a **port** to `SharedKernel.Application` (Abstractions tier — no third-party beyond `Microsoft.Extensions.*.Abstractions`), like `IRequestValidator<T>`. Never reference an Adapter from a pipeline package; for `SharedKernel.Application` the tier check (SKTIER001) rejects it anyway.
2. Markers the request type implements (`IAuthorizeRequest`, `IIdempotentRequest`, `ICacheableQuery<TValue>` …) also live in `SharedKernel.Application`, so a consuming service's Application project needs no Host package.
3. Not every behavior needs a new contract — a marker plus an already-permitted dependency is often enough (as `CacheInvalidationBehavior` reuses `ICacheService`).

**`Build()`-time guard convention (still current):** every opt-in behavior with an external dependency is checked in `ApplicationBehaviorsBuilder.Build()`, which throws `InvalidOperationException` naming the missing registration (`IRequestContext`, an `IIdempotencyStore` for `IdempotencyPurpose.Request`, `IUnitOfWork`, `IAuditTrailWriter`); a sibling package passes its required services to `AddBehavior(type, stage, requiredServices)` (as `AddCachingBehaviors()` does for `ICacheService` + `ITenantCacheKeyProvider`). The guard is keyed on the dependency type, not on which call requested it.

**Growing an already-published contract without a breaking change (still current technique):** add a separate, optional-capability interface an implementation MAY also implement, detected with an `is` pattern at the call site — never reflection, never a new member on the shipped interface.

See [[project_wo035_seven_behavior_pipeline]] and [[project_wo036_ten_step_pipeline]] for the history.
