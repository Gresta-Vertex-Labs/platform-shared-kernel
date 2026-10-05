# SharedKernel.Execution

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The execution context every package of a service shares: who is calling, which tenant the work belongs to, which
> correlation id follows it, how work commits, and how an audited action is recorded. No mediator, no ORM, no ASP.NET
> Core.**

The request pipeline authorizes against the caller, persistence filters tenant data by it, the cache scopes keys by it,
and every outbound call carries its tenant and correlation id to the next service. This package is the one place those
contracts live, so none of those packages depends on another to learn who is calling.

| You get | So that |
| --- | --- |
| `IRequestContext` with `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext` | One caller contract for HTTP requests, message consumers, workflow activities and jobs |
| `RequestContextScope` and `IRequestContextAccessor` | Code with no DI scope (an `HttpClient` handler, a publisher, a log enricher) reads the same caller |
| `RequestContextPropagation` and `PropagatedRequestContext` | Every transport writes and reads the same headers, so tenant and correlation id survive every hop |
| `CorrelationIds` | One validation rule and one format for correlation ids on every channel |
| `TenantId` and `TenantScope` | One tenant type everywhere: never `Guid.Empty`, one string form, an explicit global scope |
| `IUnitOfWork` | A retry-safe transaction the pipeline and your own code share, implemented by persistence |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | Audited actions recorded without the caller choosing who they are attributed to |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Execution" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).
You rarely reference it directly: every package that needs the caller or the transaction already depends on it.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project |
| Depends on | `SharedKernel.Primitives` only |
| Namespaces | `SharedKernel.Execution.Context`, `SharedKernel.Execution.Tenancy`, `SharedKernel.Execution.Transactions`, `SharedKernel.Execution.Auditing` |

## Quick start

The package has no registration method. In an HTTP host the request context comes from
`SharedKernel.ServiceDefaults.Security`:

```csharp
builder.Services.AddSharedKernelRequestContext();   // IRequestContext over IUserContext

var app = builder.Build();
app.UseSharedKernelRequestContext();                // first middleware: correlation id + the request's scope
```

Application code injects the caller:

```csharp
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed class CloseAccount(IRequestContext caller, IAccountStore accounts)
{
    public async Task<Result> HandleAsync(Guid accountId, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)                       // no tenant: fail closed
            return Error.Forbidden("account.no_tenant", "A tenant is required.");
        if (!await caller.HasPermissionAsync("accounts.close", ct))
            return Error.Forbidden("account.forbidden", "You may not close accounts.");

        return await accounts.CloseAsync(tenant, accountId, ct);
    }
}
```

No configuration section: the package has no options.

## How it works

### Where each contract comes from

| Contract | Consumed by | Implemented or opened by |
| --- | --- | --- |
| `IRequestContext` | the application pipeline, persistence (audit columns, tenant filters, RLS), idempotency stores, publishing | `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`; `AnonymousRequestContext` when nothing is registered |
| `RequestContextScope` | every inbound adapter | HTTP middleware (`UseSharedKernelRequestContext()`), gRPC server interceptor, MassTransit consume filter, Temporal activity interceptor, scheduler job runner |
| `IRequestContextAccessor` | outbound adapters (REST/gRPC clients, publishers, Temporal dispatch, idempotency stores) | `RequestContextAccessor`, registered by each adapter with `TryAddSingleton` |
| `TenantId`, `TenantScope` | every tenant-aware API | this package |
| `IUnitOfWork` | the pipeline's transaction step, your own code | `SharedKernel.Persistence.EfCore` |
| `IAuditTrailWriter` | the pipeline's auditing step | `SharedKernel.Persistence.EfCore.Auditing` |

### The caller

| Caller | Context |
| --- | --- |
| An authenticated HTTP or gRPC request | the host's registration, `ActorKind.User` or `Service` |
| An unauthenticated request | `ActorKind.Anonymous`, never `System` |
| A message consumer or workflow activity | `PropagatedRequestContext`, rebuilt from the sender's headers |
| A background job, a startup task | `new SystemRequestContext(permissions, identity: "nightly-export", tenantId: tenant, correlationId: CorrelationIds.New())` |
| Nothing registered | `AnonymousRequestContext.Instance`: unauthenticated, no tenant |

- `IRequestContext` members after `TenantId` (`ActorKind`, `ClientId`, `SessionId`, `ImpersonatorId`, `CorrelationId`)
  have default implementations.
- `SystemRequestContext` holds exactly the permissions you pass; an empty set means none, never "all".
- **Fail closed:** a `null` tenant matches no tenant-scoped row and rejects every tenant-scoped write.

### The ambient scope

- `RequestContextScope.Begin(context)` flows with `ExecutionContext`, like `AsyncLocal<T>`: awaited continuations see it;
  a scope opened inside an awaited method is not visible to its caller.
- Disposing restores the previous context, so scopes nest; disposing twice is harmless. Outside any scope `Current` is `null`.
- With `AddSharedKernelRequestContext()`, `IRequestContext` resolves to the ambient scope when one is open and to the
  request's security-backed context otherwise.

### Correlation ids

A correlation id follows one logical operation across every service, message, workflow and job. Unlike a trace id it is
never replaced once the operation has one. Every inbound adapter applies the same rule — 1–128 characters of
`[A-Za-z0-9-_:.]` (GUIDs, ULIDs and W3C trace ids qualify) — because the value reaches logs and baggage.
`CorrelationIds.Current` reads the context's `CorrelationId`, then the `correlation.id` baggage item; it never falls back
to `Activity.Id` or `TraceId`.

### Propagation across hops

`RequestContextPropagation` is the one mapping between a context and transport headers:

| Header (`WellKnownHeaders`) | Value | Written when |
| --- | --- | --- |
| `X-Correlation-Id` (`CorrelationId`) | the correlation id | there is one |
| `X-Tenant-Id` (`TenantId`) | `TenantId.ToString()` | the caller has a tenant |
| `x-sk-actor-id` (`ActorId`) | `UserId` | the caller has a subject |
| `x-sk-actor-kind` (`ActorKind`) | `User`, `Service`, `System` or `Anonymous` | there is a caller |
| `x-sk-client-id` (`ClientId`) | the OAuth2 client id | the caller has one |

- **Permissions are never written.** `PropagatedRequestContext.HasPermissionAsync` always returns `false`: a header anyone
  on the transport can set must not grant anything.
- `ReadHeaders` never throws on a malformed header: an unparsable tenant becomes "no tenant", an unknown actor kind
  `Anonymous`, an invalid correlation id is replaced by a new one (or dropped with `createCorrelationId: false`).
- The platform's REST and gRPC clients, MassTransit and Temporal already call these methods. Webhooks send only the
  correlation id — a subscriber is outside the trust boundary.

### The unit of work

- **The delegate may run more than once.** A retrying execution strategy replays it after a transient failure. Load what
  you need inside it; keep HTTP calls and messages out of it.
- `ExecuteInTransactionAsync<TResult>` rolls back and returns the result unchanged when it is a failed `Result`/`Result<T>`.
- A call inside an active transaction joins it; only the outermost call commits. A joined call that throws or fails marks
  the transaction **rollback-only**, and an outermost operation that still succeeds gets `TransactionRolledBackException`.
- A commit that fails without a server response throws `CommitOutcomeUnknownException` and is never replayed — re-read
  (or check the idempotency key) before retrying.
- `OnBeforeCommit(callback)` queues work inside the transaction after the last save; `IsTransactionActive` reports one.

### The audit writer

`AuditEntry` carries only what the caller knows (action, resource type and id, snapshots, outcome, error code, approval
id, idempotency key). The writer resolves actor, tenant, client, session, time and correlation id from the request context
itself. `Succeeded` is written inside the business transaction; `Failed` on its own connection, committed at once.

## Recipes

### 1. Open a scope in your own inbound adapter or background loop

```csharp
var context = new SystemRequestContext([], identity: "outbox-relay", correlationId: CorrelationIds.New());

using (RequestContextScope.Begin(context))
{
    await relay.RunOnceAsync(ct);   // everything awaited here sees the same context
}
```

### 2. Read the caller from a singleton

```csharp
public sealed class AuditEnricher(IRequestContextAccessor accessor)
{
    public string? CurrentTenant() => accessor.Current?.TenantId?.ToString();
}
```

### 3. Propagate the caller over a custom transport

```csharp
// Outbound: keep a value the caller already set.
RequestContextPropagation.WriteHeaders(
    accessor.Current,
    message.Headers,
    static (headers, name, value) => headers.TryAdd(name, value));

// Inbound: rebuild the sender, then run the handler inside its scope.
PropagatedRequestContext sender = RequestContextPropagation.ReadHeaders(
    message.Headers,
    static (headers, name) => headers.TryGetValue(name, out var value) ? value : null);

using (RequestContextScope.Begin(sender))
{
    await handler.HandleAsync(message, ct);
}
```

### 4. Work with tenants

```csharp
using SharedKernel.Execution.Tenancy;

var tenant = TenantId.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
string key = $"orders:{tenant}";                                 // always the lowercase "D" form
TenantId? fromColumn = TenantId.FromNullable(row.TenantGuid);    // null and Guid.Empty both become null

await index.SearchAsync(request, TenantScope.For(tenant), ct);
await index.SearchAsync(request, TenantScope.Global, ct);          // work that belongs to no tenant, on purpose
TenantScope scope = TenantScope.FromNullable(context.TenantId);   // Global when there is no tenant
```

### 5. Run retry-safe work in one transaction

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

## Reference

### Registration

None in this package. `IRequestContext` is registered by the host (`AddSharedKernelRequestContext()`); adapters
`TryAddSingleton` the `RequestContextAccessor`; persistence registers `IUnitOfWork` and `IAuditTrailWriter`.

### Types

| Type | Namespace | Purpose |
| --- | --- | --- |
| `IRequestContext` | `.Context` | `IsAuthenticated`, `UserId`, `TenantId`, `ActorKind`, `ClientId`, `SessionId`, `ImpersonatorId`, `CorrelationId`, `HasPermissionAsync` |
| `ActorKind` | `.Context` | `User`, `Service`, `System`, `Anonymous` |
| `SystemRequestContext` | `.Context` | `(permissions, identity = "system", tenantId = null, correlationId = null)` |
| `AnonymousRequestContext` | `.Context` | Unauthenticated, no tenant; `Instance` |
| `PropagatedRequestContext` | `.Context` | The sender rebuilt from headers; attribution only |
| `RequestContextScope` | `.Context` | `Begin(context)` → `IDisposable`; `Current` |
| `IRequestContextAccessor`, `RequestContextAccessor` | `.Context` | `Current`; register as a singleton |
| `RequestContextExtensions` | `.Context` | `WithTenant(tenantId)`, `WithCorrelationId(id)` |
| `RequestContextPropagation` | `.Context` | `WriteHeaders`, `ReadHeaders`, `ParseActorKind` |
| `CorrelationIds` | `.Context` | `New`, `IsValid`, `AcceptOrCreate`, `Current`, `MaxLength` (128) |
| `TenantId` | `.Tenancy` | Non-empty `Guid`; `Parse`, `TryParse`, `FromNullable`, `IsDefault`, explicit `Guid` conversions, `TenantIdJsonConverter` (string form) |
| `TenantScope` | `.Tenancy` | `For(tenant)`, `Global`, `FromNullable`; `Tenant`, `IsGlobal` |
| `IUnitOfWork` | `.Transactions` | `ExecuteInTransactionAsync` (4 overloads, optional `IsolationLevel`), `SaveChangesAsync`, `IsTransactionActive`, `OnBeforeCommit` |
| `CommitOutcomeUnknownException`, `TransactionRolledBackException` | `.Transactions` | The two transaction outcomes a caller must handle |
| `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome` | `.Auditing` | `RecordAsync(entry)`; `Succeeded`/`Failed` |

### Logging

The package does not log.

## Testing

Reference [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
and use `TestRequestContext` (`SharedKernel.Testing.Execution`) — settable tenant, actor and permissions:

```csharp
var caller = TestRequestContext.ForTenant(tenant).WithPermissions("accounts.close");
var handler = new CloseAccount(caller, accounts);
```

Other factories: `ForUser`, `Service(clientId)`, `System(identity)`, `Anonymous()`. For persistence fakes of
`IUnitOfWork` and `IAuditTrailWriter`, see
[`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Persistence.Testing/README.md)
(`AddFakeUnitOfWork()`, `AddFakeAuditTrailWriter()`).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Use `Guid.Empty` or a `string` for "no tenant" | `TenantId?` = `null` | The constructor rejects `Guid.Empty`; tenant-scoped code fails closed on `null` |
| Default a `TenantScope` parameter | Make it required | `default(TenantScope)` is `Global` |
| Grant `SystemRequestContext` "all permissions" | Pass the explicit set the job needs | An empty set means none; there is no wildcard |
| Authorize with a `PropagatedRequestContext` | Re-resolve permissions from the identity provider by `UserId` | Headers grant nothing; its permission check always fails |
| Read caller identity from `Activity` baggage | Read `IRequestContext` / `IRequestContextAccessor` | Baggage is caller input |
| Use `Activity.Id` as the correlation id | `CorrelationIds.Current(context)` | A trace id changes; a correlation id must not |
| Open a scope in one method and dispose it elsewhere | `using (RequestContextScope.Begin(…))` in one method | Scopes flow with `ExecutionContext` and restore on dispose |
| Load aggregates before `ExecuteInTransactionAsync` | Load inside the delegate | A retry replays the delegate with fresh state |
| Send HTTP calls or messages inside the transaction delegate | Use the outbox or `OnBeforeCommit` | The delegate may run more than once |
| Retry after `CommitOutcomeUnknownException` blindly | Re-read or check the idempotency key | The commit may have happened |

## Design decisions

**Why no `BeginTransactionAsync`?** A caller-held transaction handle cannot be replayed by a retrying execution strategy;
a delegate can.

**Why does the audit writer resolve the actor itself?** So a caller cannot attribute an entry to someone else.

**Why is this a Foundation package with no mediator, ORM or ASP.NET Core?** Every channel — HTTP, gRPC, messages,
workflows, jobs — needs the same caller, and every tier must be able to read it.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
