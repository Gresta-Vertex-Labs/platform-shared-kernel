---
name: project_application_p544_contracts
description: 05.Application's P-544 redesign — the new local-seam contracts 16.Testing/Application now fakes
metadata:
  type: project
---

As of P-544 (2026-09-15), `05.Application`'s local-seam contracts that `16.Testing/Application/`
fakes are:

- `SharedKernel.Application.Context.IRequestContext` (package: `SharedKernel.Application`, NOT
  `.Behaviors`) — `IsAuthenticated`, `UserId` (`string?`), `TenantId` (`Guid?`, nullable is
  legitimate here — no tenant is a real state, unlike `12.Security.ITenantProvider`),
  `HasPermissionAsync(string, CancellationToken)`. Replaces the old `IAuthorizationContext`
  (arbitrary requirement strings) — `AuthorizationBehavior` now reads
  `IAuthorizeRequest.RequiredPermissions`/`PermissionMatch` (All/Any) instead.
- `SharedKernel.Application.Behaviors.Idempotency.IRequestIdempotencyStore` — one interface
  (`TryBeginAsync` returns a `Status` enum: Started/InProgress/Completed/FingerprintMismatch,
  plus `CompleteAsync`/`ReleaseAsync`) replacing the old two-interface
  `IIdempotencyKeyStore`/`IIdempotencyResponseStore` optional-capability-via-`is`-check pattern
  entirely. `IdempotencyBehavior` (renamed from `IdempotentCommandBehavior`) depends on this
  single contract — no second fake type is needed for "replay" testing anymore, `TryBeginAsync`'s
  `Completed` status carries the stored response directly.
- `DualApproval` (`IDualApprovalStore`, `IRequiresDualApproval`, `DualApprovalBehavior`) was
  retired from `05.Application` entirely — no replacement, no fake.
- `Transaction.IUnitOfWork` (single `SaveChangesAsync(CancellationToken) -> Task<int>`) and the
  local `Auditing.IAuditTrailWriter`/`AuditEntry` shape are otherwise unchanged, EXCEPT
  `AuditEntry` dropped `ApprovalId` and added `bool Succeeded`/`string? ErrorCode`.
- Pipeline stage order is now: Tracing → Logging → Metrics → Authorization → Validation → Query
  stage → Command stage (`CommandScopeBehavior` → Idempotency → Transaction → Auditing → custom).
  `TracingBehavior`'s span tag is `request.type` (full type name) now, not the old
  `request.name`.
- New sibling package `SharedKernel.Application.Behaviors.Caching` (`CachingBehavior`,
  `CacheInvalidationBehavior`, `AddCachingBehaviors()`) composes via the same
  `ApplicationBehaviorsBuilder.AddBehavior(...)`/`PipelineStage` extension point — needs no new
  `16.Testing` fake, `02.Caching`'s existing `FakeCacheService` already satisfies its
  `ICacheService` prerequisite.

**How to apply:** any future `16.Testing/Application/` work should read
`05.Application/SharedKernel.Application/Context/IRequestContext.cs` and
`05.Application/SharedKernel.Application.Behaviors/Idempotency/IRequestIdempotencyStore.cs`
directly (never from memory/this note) for the exact current shape before writing code — this
note is a pointer to what to check, not a substitute for reading the live source.
