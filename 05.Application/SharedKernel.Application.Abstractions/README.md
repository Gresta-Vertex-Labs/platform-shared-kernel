# SharedKernel.Application.Abstractions

**The contracts the application pipeline and the persistence layer share: who is calling, how work
commits, and how an audited action is recorded. No MediatR, no ORM.**

| Contract | Consumed by | Implemented by |
| --- | --- | --- |
| `IRequestContext` (+ `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`) | the pipeline's authorization, idempotency and caching; persistence audit columns, tenant filters and the audit trail; your handlers | your composition root — `13.ServiceDefaults`' `AddSharedKernelRequestContext()` over `12.Security`'s `IUserContext` |
| `IUnitOfWork` | the pipeline's transaction (`WithTransactions()`), your own code | `SharedKernel.Persistence.EfCore` (`EfUnitOfWork`) |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | the pipeline's auditing (`WithAuditing()`) | `SharedKernel.Persistence.EfCore.Auditing` |

A service does not reference this package itself: it comes with `SharedKernel.Application`, and the infrastructure
that implements it references it directly. Because persistence implements these interfaces, a service needs no
adapter between `SharedKernel.Application` and `SharedKernel.Persistence.*`.

**Dependencies:** `SharedKernel.Primitives` only.

## The unit of work

```csharp
public sealed class ApproveOrderJob(IUnitOfWork unitOfWork, IRepository<Order, OrderId> orders)
{
    public Task RunAsync(OrderId id, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var order = await orders.GetByIdAsync(id, token);   // load INSIDE the delegate
            order!.Approve();
        }, ct);
}
```

- **The delegate may run more than once.** A retrying execution strategy replays it after a transient
  failure, discarding what the failed attempt staged. Load what you need inside it; keep HTTP calls and
  messages out of it.
- `ExecuteInTransactionAsync<TResult>` rolls back and returns the result unchanged when the result is a
  failed `Result`/`Result<T>` — nothing is saved.
- Calling it inside an active transaction joins that transaction; only the outermost call commits. With
  `SharedKernel.Persistence.EfCore` every context of the DI scope shares that one transaction.
- A joined call that throws or returns a failed result marks the transaction **rollback-only**: nothing
  commits, and an outermost operation that still returns success gets `TransactionRolledBackException`.
- A commit that fails without a server response throws `CommitOutcomeUnknownException` and is never
  replayed — the commit may or may not have happened; re-read (or check the idempotency key) before retrying.
- `OnBeforeCommit(callback)` queues work inside the active transaction, after the last save and before
  the commit. The audit trail writes its `Succeeded` record there.
- There is no `BeginTransactionAsync`: a caller-held transaction handle cannot be replayed by a retrying
  strategy.

## The caller

`IRequestContext` exposes `IsAuthenticated`, `UserId`, `TenantId`, `ActorKind` (`User`, `Service`, `System`,
`Anonymous`), `ClientId`, `SessionId`, `ImpersonatorId` and `HasPermissionAsync`. An unauthenticated caller is
`Anonymous`, never `System`; background work runs under a `SystemRequestContext`. The last four attribution members have default
implementations, so an implementation written before they existed keeps compiling.

When nothing is registered, persistence falls back to `AnonymousRequestContext`: unauthenticated, no
tenant. A `null` tenant matches no tenant-scoped row and rejects every tenant-scoped write.

The namespaces are `SharedKernel.Application.Context`, `.Transactions` and `.Auditing`. `SharedKernel.Application`
no longer type-forwards these types (P-563); it brings this assembly along, so `using` the namespace is enough.

## The audit writer

`AuditEntry` carries only what the caller knows (action, resource, snapshots, outcome, error code,
approval id, idempotency key). The writer resolves actor, tenant, client, session, time and correlation
itself, so a caller cannot attribute an entry to someone else.

- `Succeeded` is written inside the business transaction and commits or rolls back with it.
- `Failed` is written on its own connection and committed at once.
