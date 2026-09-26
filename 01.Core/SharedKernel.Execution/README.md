# SharedKernel.Execution

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The execution context every package of a service shares: who is calling, which tenant the work belongs to,
> which correlation id follows it, how work commits, and how an audited action is recorded. No mediator, no ORM,
> no ASP.NET Core.**

A service has one caller per call, but many packages need to know it: the request pipeline authorizes against it,
persistence stamps audit columns and filters tenant data by it, the cache scopes keys by it, and every outbound HTTP,
gRPC, message or workflow call must carry its tenant and correlation id to the next service. This package is the one
place those contracts live, so none of those packages depends on another to learn who is calling.

| You get | So that |
| --- | --- |
| `IRequestContext` with `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext` | One caller contract for HTTP requests, message consumers, workflow activities and jobs |
| `RequestContextScope` and `IRequestContextAccessor` | Code with no DI scope (an `HttpClient` handler, a publisher, a log enricher) reads the same caller |
| `RequestContextPropagation` and `PropagatedRequestContext` | Every transport writes and reads the same headers, so the tenant and correlation id survive every hop |
| `CorrelationIds` | One validation rule and one format for correlation ids on every channel |
| `TenantId` and `TenantScope` | One tenant type everywhere: never `Guid.Empty`, one string form, an explicit global scope |
| `IUnitOfWork` | A retry-safe transaction the pipeline and your own code share, implemented by persistence |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | Audited actions recorded without the caller choosing who they are attributed to |

## Contents

- [Install](#install)
- [Where each contract comes from](#where-each-contract-comes-from)
- [The caller](#the-caller)
- [The ambient context](#the-ambient-context)
- [Correlation ids](#correlation-ids)
- [Propagation across hops](#propagation-across-hops)
- [Tenants](#tenants)
- [The unit of work](#the-unit-of-work)
- [The audit writer](#the-audit-writer)
- [Reference](#reference)
- [Related packages](#related-packages)

## Install

```xml
<PackageReference Include="SharedKernel.Execution" />
```

The version comes from your repository's single `SharedKernelVersion` property (see "Consuming the kernel" in the
root README). You rarely reference this package directly: every package that needs the caller or the transaction
already depends on it.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation: references only other Foundation packages, and any package may reference it |
| Depends on | `SharedKernel.Primitives` only |
| Namespaces | `SharedKernel.Execution.Context`, `SharedKernel.Execution.Tenancy`, `SharedKernel.Execution.Transactions`, `SharedKernel.Execution.Auditing` |

## Where each contract comes from

| Contract | Consumed by | Implemented or opened by |
| --- | --- | --- |
| `IRequestContext` | the application pipeline (authorization, caching, auditing), persistence (audit columns, tenant filters, row-level security), idempotency stores, message publishing | `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` over `IUserContext`; `AnonymousRequestContext` when nothing is registered |
| `RequestContextScope` | every inbound adapter | the HTTP middleware (`UseSharedKernelRequestContext()`), the gRPC server interceptor, the MassTransit consume filter, the Temporal activity interceptor, the scheduler's job runner |
| `IRequestContextAccessor` | outbound adapters: REST and gRPC clients, MassTransit publishers, Temporal dispatch, idempotency stores | `RequestContextAccessor` in this package; every adapter that needs it registers it with `TryAddSingleton` |
| `TenantId`, `TenantScope` | every tenant-aware API: caching, storage, search, vectors, workflows, scheduling, feature flags, persistence | this package |
| `IUnitOfWork` | the pipeline's transaction step, your own code | `SharedKernel.Persistence.EfCore` |
| `IAuditTrailWriter` | the pipeline's auditing step | `SharedKernel.Persistence.EfCore.Auditing` |

## The caller

`IRequestContext` exposes `IsAuthenticated`, `UserId`, `TenantId`, `ActorKind` (`User`, `Service`, `System`,
`Anonymous`), `ClientId`, `SessionId`, `ImpersonatorId`, `CorrelationId` and `HasPermissionAsync`. The members after
`TenantId` have default implementations, so a minimal implementation only answers the first three and the permission
check.

| Caller | Context |
| --- | --- |
| An authenticated HTTP or gRPC request | the host's registration (`AddSharedKernelRequestContext()`), `ActorKind.User` or `Service` |
| An unauthenticated request | `ActorKind.Anonymous`, never `System` |
| A message consumer or workflow activity | `PropagatedRequestContext`, rebuilt from the headers the sender wrote |
| A background job, a startup task | `new SystemRequestContext(permissions, identity: "nightly-export", tenantId: tenant, correlationId: CorrelationIds.New())` |
| Nothing registered | `AnonymousRequestContext.Instance`: unauthenticated, no tenant |

`SystemRequestContext` holds exactly the permissions you pass; an empty set means none, never "all permissions".

**Fail closed.** A `null` tenant matches no tenant-scoped row and rejects every tenant-scoped write, so a caller that
forgot its tenant cannot read or change another tenant's data.

`WithTenant(tenantId)` and `WithCorrelationId(id)` derive a context that differs in one member and forwards the rest;
inbound adapters use them to refine the context they open.

## The ambient context

Resolve `IRequestContext` from DI wherever a scope exists (handlers, repositories). Code that has no scope reads the
context of the call it runs inside through `IRequestContextAccessor`:

```csharp
using SharedKernel.Execution.Context;

public sealed class AuditEnricher(IRequestContextAccessor accessor)   // a singleton
{
    public string? CurrentTenant() => accessor.Current?.TenantId?.ToString();
}
```

Each inbound adapter opens exactly one scope per call. If you write your own inbound adapter (a queue listener, a
hosted-service loop), do the same:

```csharp
var context = new SystemRequestContext([], identity: "outbox-relay", correlationId: CorrelationIds.New());

using (RequestContextScope.Begin(context))
{
    await relay.RunOnceAsync(ct);   // everything awaited here sees the same context
}
```

- The value flows with `ExecutionContext`, like `AsyncLocal<T>`: awaited continuations and tasks started inside the
  scope see it; a scope opened inside an awaited method is not visible to its caller.
- Disposing a scope restores the context that was current before it, so scopes nest. Disposing twice is harmless.
- Outside any scope (startup, a bare background thread) `Current` is `null`.
- Open and dispose a scope in the same method, with `using`.

With `AddSharedKernelRequestContext()`, `IRequestContext` resolves to the ambient scope when one is open and to the
request's security-backed context otherwise, so handlers see the same caller whichever way they obtain it.

## Correlation ids

A correlation id follows one logical operation across every service, message, workflow and job it reaches. It is not
a trace id: a trace id is replaced wherever a new trace starts, a correlation id is never replaced once the operation
has one.

```csharp
string id = CorrelationIds.New();                        // a GUID in "D" format
bool ok = CorrelationIds.IsValid(candidate);             // 1-128 chars of [A-Za-z0-9-_:.]
string accepted = CorrelationIds.AcceptOrCreate(header); // the caller's value when valid, else a new one
string? current = CorrelationIds.Current(accessor.Current);
```

- Every inbound adapter applies the same rule, so a value accepted at the edge is never rejected by a later hop.
  GUIDs, ULIDs and W3C trace ids all qualify.
- The rule exists because the value reaches logs and baggage: an unchecked caller string would be a log-injection and
  oversized-baggage vector.
- `Current` reads the context's `CorrelationId`, then the `correlation.id` baggage item. It never falls back to
  `Activity.Id` or `TraceId`.

## Propagation across hops

`RequestContextPropagation` is the one mapping between a context and transport headers. Each transport supplies only
how a header is set or read:

```csharp
// Outbound: write the current caller onto a message's headers.
RequestContextPropagation.WriteHeaders(
    accessor.Current,
    message.Headers,
    static (headers, name, value) => headers.TryAdd(name, value));   // keep a value the caller already set

// Inbound: rebuild the sender, then run the handler inside its scope.
PropagatedRequestContext sender = RequestContextPropagation.ReadHeaders(
    message.Headers,
    static (headers, name) => headers.TryGetValue(name, out var value) ? value : null);

using (RequestContextScope.Begin(sender))
{
    await handler.HandleAsync(message, ct);
}
```

| Header (`WellKnownHeaders`) | Value | Written when |
| --- | --- | --- |
| `X-Correlation-Id` (`CorrelationId`) | the correlation id | there is one |
| `X-Tenant-Id` (`TenantId`) | `TenantId.ToString()` | the caller has a tenant |
| `x-sk-actor-id` (`ActorId`) | `UserId` | the caller has a subject |
| `x-sk-actor-kind` (`ActorKind`) | `User`, `Service`, `System` or `Anonymous` | there is a caller |
| `x-sk-client-id` (`ClientId`) | the OAuth2 client id | the caller has one |

- Permissions are never written. `PropagatedRequestContext.HasPermissionAsync` always returns `false`: a header anyone
  on the transport can set must not grant anything. Code that must authorize re-resolves permissions from the identity
  provider using `UserId`.
- `ReadHeaders` never throws on a malformed header: an unparsable tenant becomes "no tenant" (fail closed), an unknown
  actor kind becomes `Anonymous`, an invalid correlation id is replaced by a new one (or dropped with
  `createCorrelationId: false`).
- The platform's REST and gRPC clients, MassTransit and Temporal already call these methods. Webhooks send only the
  correlation id, because a subscriber is outside the trust boundary.

## Tenants

`TenantId` wraps a non-empty `Guid`. "No tenant" is `TenantId?` = `null`, never `Guid.Empty`.

```csharp
using SharedKernel.Execution.Tenancy;

var tenant = TenantId.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
string key = $"orders:{tenant}";                                 // always the lowercase "D" form
TenantId? fromColumn = TenantId.FromNullable(row.TenantGuid);    // null and Guid.Empty both become null
bool ok = TenantId.TryParse(header, out TenantId parsed);
```

- The constructor rejects `Guid.Empty`. `default(TenantId)` exists because it is a struct; `IsDefault` reports it and
  APIs reject it.
- The string form is the same everywhere: cache keys, storage prefixes, headers, baggage, logs, row-level-security
  settings. JSON reads and writes it as a string.
- Conversions to and from `Guid` are explicit.

`TenantScope` is the explicit scope of an operation that could otherwise leak across tenants, such as a search, a
vector query, a workflow dispatch or a scheduled job:

```csharp
await index.SearchAsync(request, TenantScope.For(tenant), ct);
await index.SearchAsync(request, TenantScope.Global, ct);          // work that belongs to no tenant, on purpose
TenantScope scope = TenantScope.FromNullable(context.TenantId);   // Global when there is no tenant
```

APIs take it as a required parameter with no default, because `default(TenantScope)` is `Global`.

## The unit of work

```csharp
using SharedKernel.Execution.Transactions;

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

- **The delegate may run more than once.** A retrying execution strategy replays it after a transient failure,
  discarding what the failed attempt staged. Load what you need inside it; keep HTTP calls and messages out of it.
- `ExecuteInTransactionAsync<TResult>` rolls back and returns the result unchanged when the result is a failed
  `Result`/`Result<T>`: nothing is saved. Overloads take an `IsolationLevel`.
- Calling it inside an active transaction joins that transaction; only the outermost call commits. With
  `SharedKernel.Persistence.EfCore` every context of the DI scope on the same database shares that one transaction.
- A joined call that throws or returns a failed result marks the transaction **rollback-only**: nothing commits, and an
  outermost operation that still returns success gets `TransactionRolledBackException`.
- A commit that fails without a server response throws `CommitOutcomeUnknownException` and is never replayed. The
  commit may or may not have happened; re-read (or check the idempotency key) before retrying.
- `OnBeforeCommit(callback)` queues work inside the active transaction, after the last save and before the commit. The
  audit trail writes its `Succeeded` record there. `IsTransactionActive` reports whether one is open.
- There is no `BeginTransactionAsync`: a caller-held transaction handle cannot be replayed by a retrying strategy.

## The audit writer

`AuditEntry` carries only what the caller knows: action, resource type and id, before/after snapshots, outcome, error
code, approval id and idempotency key. The writer resolves actor, tenant, client, session, time and correlation id from
the request context itself, so a caller cannot attribute an entry to someone else.

- `Succeeded` is written inside the business transaction and commits or rolls back with it.
- `Failed` is written on its own connection and committed at once.

The pipeline's auditing behavior writes these for commands marked `IAuditableRequest`; call `RecordAsync` directly only
for actions that do not go through the pipeline.

## Reference

| Type | Namespace | Purpose |
| --- | --- | --- |
| `IRequestContext` | `.Context` | The caller: identity, tenant, actor kind, client, session, impersonator, correlation id, permission check |
| `ActorKind` | `.Context` | `User`, `Service`, `System`, `Anonymous` |
| `SystemRequestContext` | `.Context` | Authenticated system actor with an explicit permission set, optional tenant and correlation id |
| `AnonymousRequestContext` | `.Context` | Unauthenticated, no tenant; `Instance` |
| `PropagatedRequestContext` | `.Context` | The sender rebuilt from propagation headers; attribution only, grants no permission |
| `RequestContextScope` | `.Context` | `Begin(context)` makes a context ambient; `Current` |
| `IRequestContextAccessor`, `RequestContextAccessor` | `.Context` | Reads the ambient context; register as a singleton |
| `RequestContextExtensions` | `.Context` | `WithTenant`, `WithCorrelationId` |
| `RequestContextPropagation` | `.Context` | `WriteHeaders`, `ReadHeaders`, `ParseActorKind` |
| `CorrelationIds` | `.Context` | `New`, `IsValid`, `AcceptOrCreate`, `Current`, `MaxLength` (128) |
| `TenantId` | `.Tenancy` | Non-empty `Guid` tenant id; `Parse`, `TryParse`, `FromNullable`, JSON converter |
| `TenantScope` | `.Tenancy` | `For(tenant)`, `Global`, `FromNullable`; `Tenant`, `IsGlobal` |
| `IUnitOfWork` | `.Transactions` | `ExecuteInTransactionAsync` (4 overloads), `SaveChangesAsync`, `IsTransactionActive`, `OnBeforeCommit` |
| `CommitOutcomeUnknownException`, `TransactionRolledBackException` | `.Transactions` | The two transaction outcomes a caller must handle |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | `.Auditing` | Audit records written through the persistence ledger |

The header and baggage names are in `SharedKernel.Primitives.Propagation` (`WellKnownHeaders`, `WellKnownBaggageKeys`).

## Related packages

- [`SharedKernel.Primitives`](../SharedKernel.Primitives/README.md): `Result`, `Error`, `WellKnownHeaders`.
- `SharedKernel.ServiceDefaults.Security` (`13.ServiceDefaults`): `AddSharedKernelRequestContext()` and
  `app.UseSharedKernelRequestContext()`, the HTTP adapter. Register the middleware first, before
  `UseExceptionHandler()`.
- `SharedKernel.MultiTenancy` (`13.ServiceDefaults`): resolves the request's tenant and opens an inner scope that
  replaces only the tenant.
- `SharedKernel.Persistence.EfCore` and `.EfCore.Auditing` (`06.Persistence`): implement `IUnitOfWork` and
  `IAuditTrailWriter`.
- `SharedKernel.Testing` (`16.Testing`): `TestRequestContext` for unit tests.
