# SharedKernel.Execution

**The execution context every layer shares: who is calling, which tenant the work belongs to, how work
commits, and how an audited action is recorded. No MediatR, no ORM, no ASP.NET Core.**

| Contract | Consumed by | Implemented by |
| --- | --- | --- |
| `IRequestContext` (+ `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`) | the application pipeline's authorization and caching, persistence audit columns, tenant filters, the audit trail, message publishing | your composition root — `13.ServiceDefaults` ships one over `12.Security`'s `IUserContext` |
| `RequestContextScope`, `IRequestContextAccessor` | code with no DI scope: `HttpClient` handlers, message publishers, log enrichers | this package (`RequestContextAccessor`) |
| `TenantId`, `TenantScope` | every tenant-aware API | this package |
| `IUnitOfWork` | the pipeline's transaction step, your own code | `SharedKernel.Persistence.EfCore` |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | the pipeline's auditing step | `SharedKernel.Persistence.EfCore.Auditing` |

Because the pipeline, persistence and messaging all depend on these contracts rather than on each other,
a service needs no adapter between them.

**Dependencies:** `SharedKernel.Primitives` only. This is a Foundation-tier package: any package may
reference it.

## The caller

`IRequestContext` exposes `IsAuthenticated`, `UserId`, `TenantId`, `ActorKind` (`User`, `Service`,
`System`, `Anonymous`), `ClientId`, `SessionId`, `ImpersonatorId`, `CorrelationId` and
`HasPermissionAsync`. An unauthenticated caller is `Anonymous`, never `System`; background work runs under
a `SystemRequestContext` with an explicit permission set. The attribution members after `TenantId` have
default implementations, so an implementation written before they existed keeps compiling.

When nothing is registered, persistence falls back to `AnonymousRequestContext`: unauthenticated, no
tenant. A `null` tenant matches no tenant-scoped row and rejects every tenant-scoped write.

### The ambient context

Resolve `IRequestContext` from DI wherever a scope exists. Code that has no scope reads the context of
the call it runs inside through `IRequestContextAccessor` (register `RequestContextAccessor` as a
singleton). Each inbound adapter opens exactly one scope per call:

```csharp
using (RequestContextScope.Begin(context))
{
    await next(); // everything awaited here sees the same context
}
```

The value flows with `ExecutionContext`, like `AsyncLocal<T>`: awaited continuations and tasks started
inside the scope see it, and a scope opened inside an awaited method is not visible to its caller.
Disposing a scope restores the context that was current before it, so scopes nest; disposing twice is
harmless. Outside any scope (startup, a bare background thread) the accessor returns `null`.

## Tenants

`TenantId` wraps a non-empty `Guid`. "No tenant" is `TenantId?` = `null`, never `Guid.Empty` — the
constructor rejects it, and `TenantId.FromNullable(Guid?)` maps both `null` and `Guid.Empty` to `null`.

```csharp
var tenant = TenantId.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
string key = $"orders:{tenant}";         // always the lowercase "D" form
```

The string form is the same everywhere (cache keys, storage prefixes, headers, logs), and JSON reads and
writes it as a string. `default(TenantId)` exists because it is a struct; `IsDefault` reports it.

`TenantScope` is the explicit scope of an operation that could otherwise leak across tenants:
`TenantScope.For(tenant)` or `TenantScope.Global` for work that belongs to no tenant. APIs take it as a
required parameter with no default, because `default(TenantScope)` is `Global`.

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

## The audit writer

`AuditEntry` carries only what the caller knows (action, resource, snapshots, outcome, error code,
approval id, idempotency key). The writer resolves actor, tenant, client, session, time and correlation
itself, so a caller cannot attribute an entry to someone else.

- `Succeeded` is written inside the business transaction and commits or rolls back with it.
- `Failed` is written on its own connection and committed at once.

## Migrating from `SharedKernel.Application.Abstractions`

This package replaces `SharedKernel.Application.Abstractions` (WO-086). The types are unchanged; only the
package and namespaces moved:

| Before | After |
| --- | --- |
| `SharedKernel.Application.Context` | `SharedKernel.Execution.Context` |
| `SharedKernel.Application.Transactions` | `SharedKernel.Execution.Transactions` |
| `SharedKernel.Application.Auditing` | `SharedKernel.Execution.Auditing` |
