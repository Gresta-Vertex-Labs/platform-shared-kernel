---
name: project_application_p544_contracts
description: Where the application-pipeline contracts that 16.Testing fakes live after P-544/P-558/WO-086 — IRequestContext, IIdempotencyStore, IUnitOfWork, IAuditTrailWriter
metadata:
  type: project
---

> WO-086 (2026-09): the P-544 shapes this note used to list (`SharedKernel.Application.Context.IRequestContext` with a `Guid?` tenant, `SharedKernel.Application.Behaviors.Idempotency.IRequestIdempotencyStore`, the local `Transaction.IUnitOfWork`) are gone. Current locations below.

Contracts the application-side testing packages fake, and where each fake lives:

- `IRequestContext`, `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`, `IRequestContextAccessor`, `RequestContextScope` — `SharedKernel.Execution.Context` (Foundation, `01.Core`). `TenantId` is `TenantId?` (`SharedKernel.Execution.Tenancy`), plus `CorrelationId`, `ClientId`, `SessionId`. Fakes: `TestRequestContext` (core `SharedKernel.Testing`, namespace `SharedKernel.Testing.Execution`) and `FakeRequestContext` (core `SharedKernel.Testing/Application/`).
- `IIdempotencyStore` — `SharedKernel.Idempotency.Abstractions` (`18.Idempotency`): `TryBeginAsync(purpose, key, fingerprint, ttl)` → `IdempotencyReservation` (`IdempotencyReservationStatus` Started/InProgress/Completed/FingerprintMismatch), `CompleteAsync`, `ReleaseAsync`; one store per `IdempotencyPurpose` (Request/Message). Fake: `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` (`SharedKernel.Idempotency.Testing`). The old `FakeRequestIdempotencyStore` was deleted.
- `IUnitOfWork` (`SaveChangesAsync`, `ExecuteInTransactionAsync`, `OnBeforeCommit`, `IsTransactionActive`) — `SharedKernel.Execution.Transactions`; `IAuditTrailWriter`/`AuditEntry`/`AuditOutcome` — `SharedKernel.Execution.Auditing`. Fakes: `FakeUnitOfWork`, `FakeAuditTrailWriter` (`SharedKernel.Persistence.Testing`).
- Pipeline test harness: `ApplicationPipelineTestHarness` (`SharedKernel.Application.Testing`) builds the kernel `RequestPipeline` with no mediator — the kernel contracts (`IRequest<T>`, `IPipelineBehavior<,>`, markers) are MediatR-free in `SharedKernel.Application`; MediatR lives only in `SharedKernel.Application.Mediator.MediatR`.
- Still true from P-544: `DualApproval` was retired with no fake; the caching behaviors (`SharedKernel.Application.Pipeline.Caching`) need no new fake beyond `FakeCacheService` (`SharedKernel.Caching.Testing`). Stages are `PipelineStage` Observability → Authorization → Validation → Query → Command.

**How to apply:** read the live source (`01.Core/SharedKernel.Execution/`, `18.Idempotency/SharedKernel.Idempotency.Abstractions/`, `05.Application/CLAUDE.md`) for the exact shape before writing code — this note is a pointer, not a substitute.
