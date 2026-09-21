# SharedKernel.Application.Abstractions

**The contracts the application pipeline and the persistence layer share: who is calling, how work
commits, and how an audited action is recorded. No MediatR, no ORM.**

| Contract | Consumed by | Implemented by |
| --- | --- | --- |
| `IRequestContext` (+ `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`) | `AuthorizationBehavior`, the caching behaviors, persistence audit columns, tenant filters, the audit trail | your composition root — `13.ServiceDefaults` ships one over `12.Security`'s `IUserContext` |
| `IUnitOfWork` | `TransactionBehavior`, your own code | `SharedKernel.Persistence.EfCore` (`EfUnitOfWork`) |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | `AuditingBehavior` | `SharedKernel.Persistence.EfCore.Auditing` |

Because persistence implements these interfaces directly, a service needs no adapter between
`SharedKernel.Application.Behaviors` and `SharedKernel.Persistence.*`.

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
- Calling it inside an active transaction joins that transaction; only the outermost call commits.
- `OnBeforeCommit(callback)` queues work inside the active transaction, after the last save and before
  the commit. The audit trail writes its `Succeeded` record there.
- There is no `BeginTransactionAsync`: a caller-held transaction handle cannot be replayed by a retrying
  strategy.

## The caller

`IRequestContext` exposes `IsAuthenticated`, `UserId`, `TenantId`, `ActorKind`, `ClientId`, `SessionId`,
`ImpersonatorId` and `HasPermissionAsync`. The last four attribution members have default
implementations, so an implementation written before they existed keeps compiling.

When nothing is registered, persistence falls back to `AnonymousRequestContext`: unauthenticated, no
tenant. A `null` tenant matches no tenant-scoped row and rejects every tenant-scoped write.

`IRequestContext`, `SystemRequestContext` and `AnonymousRequestContext` moved here from
`SharedKernel.Application`, which type-forwards them; the namespace (`SharedKernel.Application.Context`)
is unchanged.

## The audit writer

`AuditEntry` carries only what the caller knows (action, resource, snapshots, outcome, error code,
approval id, idempotency key). The writer resolves actor, tenant, client, session, time and correlation
itself, so a caller cannot attribute an entry to someone else.

- `Succeeded` is written inside the business transaction and commits or rolls back with it.
- `Failed` is written on its own connection and committed at once.
