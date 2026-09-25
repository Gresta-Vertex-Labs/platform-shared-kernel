# SharedKernel.Application.Caching

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Scopes: fail closed](https://img.shields.io/badge/scopes-fail%20closed-success)
![Eviction: after commit](https://img.shields.io/badge/eviction-after%20commit-orange)

> **Cache a query by adding one interface to it, and let the command that changes the data evict it after the
> transaction commits. Keys are partitioned by query type, tenant and caller.**

Two pipeline behaviors for [`SharedKernel.Application`](../SharedKernel.Application/README.md). A query declares
`ICacheableQuery<TValue>` and is served from the cache — in memory, or Redis through `02.Caching` — without its handler
knowing. A command declares `IInvalidatesCache` and names what it made stale. Key construction, tenant and caller
partitioning, stampede protection, never caching a failure and evicting only after the commit are the behaviors' job.

```csharp
using SharedKernel.Application;
using SharedKernel.Application.Caching;
using SharedKernel.Caching.Abstractions;

// Read side: one interface; the handler does not change.
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    public string CacheKey => OrderId.ToString();
}

// Write side: name the query whose entry the command made stale.
public sealed record ApproveOrderCommand(Guid OrderId) : ICommand, IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<GetOrderQuery>(OrderId.ToString())];
}
```

```text
GetOrderQuery(42)   tenant acme, first call        -> handler runs, value cached    outcome Miss
                    tenant acme, second call       -> served from the cache         outcome Hit
                    tenant other, same order id    -> handler runs (another key)    outcome Miss
                    handler returns NotFound       -> failure returned, NOT cached  outcome NotCachedFailure
                    no tenant could be resolved    -> cache skipped, handler runs   outcome BypassedScopeUnavailable

ApproveOrderCommand(42)   succeeds and commits        -> entry evicted, after the commit
                          returns a failed Result     -> nothing evicted
                          throws                      -> nothing evicted
                          client disconnects          -> entry still evicted (not cancellable)
```

## Install and register

```shell
dotnet add package SharedKernel.Application.Caching
```

```csharp
using SharedKernel.Application;
using SharedKernel.Application.Caching;
using SharedKernel.Caching.FusionCache.Extensions;

builder.Services.AddSharedKernelCaching(o => o.ServiceName = "orders");   // ICacheService + key providers (02.Caching)

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithTransactions()   // so eviction has a commit to follow
    .WithCaching());      // caching in the Query stage, eviction in the Command stage
```

`WithCaching()` needs `ICacheService` and `ITenantCacheKeyProvider`, which `AddSharedKernelCaching()` registers. Like
every seam they are checked when the host starts, so they may be registered before or after
`AddSharedKernelApplication`. This is a separate package so that a service that never caches does not take a
dependency on `SharedKernel.Caching.Abstractions`.

## Which member do I need?

| I want to… | Declare | On |
| --- | --- | --- |
| Cache a query's result | `ICacheableQuery<TValue>` (it is also an `IQuery<TValue>`) | the query |
| Set durations, tags, fail-safe, jitter | `CachePolicy` | the query |
| Say what makes this instance unique | `CacheKey` | the query |
| Cache per tenant (the default) | nothing: `Scope` defaults to `Tenant` | — |
| Cache per caller | `Scope => CacheScope.User` | the query |
| Cache one entry for the whole service | `Scope => CacheScope.Global` | the query |
| Force a reload that also refreshes the entry | `RefreshCache => true` | the query |
| Keep an empty or negative result out of the cache | `ShouldCache(TValue)` | the query |
| Evict entries after a write commits | `IInvalidatesCache` | the command |
| Name one entry to evict | `CacheKeyRef.For<TQuery>(key)` | the command |
| Evict a group of entries | `CacheTagsToInvalidate` + `CachePolicy.WithTags(…)` | both |

## How it works

The handler runs inside one `ICacheService.GetOrSetAsync` call, so concurrent identical queries run it once. A
successful value is cached (unless `ShouldCache` declines it); a failed `Result` is returned and never cached. Eager
refresh and factory timeouts are switched off on the query's policy — either would let the cache run the handler in the
background after the request's DI scope was disposed. Durations, tags, fail-safe and jitter are kept as declared.

## Cache scope

| Scope | The key carries | Use it for |
| --- | --- | --- |
| `CacheScope.Tenant` *(default)* | the tenant | anything a multi-tenant service caches |
| `CacheScope.User` | the caller, and the tenant when there is one | anything that varies with who asks: filtered by permission, ownership or preference |
| `CacheScope.Global` | nothing | data independent of tenant and caller: reference tables, code lists |

**Scopes fail closed.** A `Tenant` query without a resolved tenant, or a `User` query without an authenticated caller,
does not fall back to a wider key: the cache is skipped, the handler runs and the skip is logged at Warning. A fallback
would let every request whose tenant resolution failed share one entry across tenants. Widening is always an explicit
`CacheScope.Global`. The identity comes from `IRequestContext`.

## How keys are built

Every key goes through the registered `ITenantCacheKeyProvider`, so it carries the service name and every segment is
escaped:

```text
Global   {service}:{QueryType}:{CacheKey}
Tenant   {service}:@{tenant}:{QueryType}:{CacheKey}
User     {service}:@{tenant}:{QueryType}:{CacheKey}:u:{userId}
```

`CacheKey` is the query's identity **within its own namespace**: build it from every parameter that changes the result
(`$"{OrderId}:{Currency}"`) and nothing else. The `{QueryType}` segment keeps two queries that pick the same key from
reading each other's payload, which `System.Text.Json` would deserialize on a best-effort basis without an error.
SK0041 reports the one way that namespace can still collapse: two cacheable queries with the same simple type name.

Tenant-scoped entries carry the tenant-wide tag, so `ITenantCacheService.RemoveTenantAsync` removes a tenant's cached
query results too.

## Invalidating after a command

```csharp
public sealed record ApproveOrderCommand(Guid OrderId) : ICommand, IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
    [
        CacheKeyRef.For<GetOrderQuery>(OrderId.ToString()),
        CacheKeyRef.For<GetOrderHistoryQuery>(OrderId.ToString()),
    ];

    public IReadOnlyCollection<string> CacheTagsToInvalidate => ["orders"];
}
```

- **Name the query, not the key.** Keys carry the query type, so a command cannot rebuild one; `CacheKeyRef.For<TQuery>`
  makes a renamed query a compile error at the command instead of a missed eviction.
- **The command's `Scope` must match the queries it invalidates.** Both default to `Tenant`.
- **Eviction happens after the commit.** On success the behavior queues the eviction with `ICommandScope.OnCompleted`,
  which runs once the outermost command committed. A failed `Result` or an exception queues nothing. Without it, a
  concurrent reader could fill the cache again from the state before the commit.
- **Eviction cannot be cancelled and does not stop at the first error.** It runs with `CancellationToken.None`, and
  each key and tag is evicted and logged on its own.
- Keys and tags are built and validated **before** the handler runs, so a malformed target fails the command, not the
  eviction after the change is saved.

## Refreshing, and declining to cache

`RefreshCache => true` ignores the entry, runs the handler and overwrites the entry: the path for a "reload" button. A
failed refresh leaves the existing entry alone.

`ShouldCache(TValue)` declines to cache one successful value, typically an empty result the caller is about to fill.
It runs inside the cache factory: keep it pure and never let it throw.

```csharp
public sealed record SearchOrdersQuery(string Term, bool Reload = false) : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(1));
    public string CacheKey => Term;
    public bool RefreshCache => Reload;
    public bool ShouldCache(IReadOnlyList<OrderDto> value) => value.Count > 0;
}
```

## The value is cached, not the `Result`

`Result` and `Result<T>` have private constructors, so a reflection-based serializer cannot write one (FusionCache's
L2 serializer throws). The behavior caches the `TValue` and rebuilds `Result<TValue>.Success(value)` on a hit.
**`TValue` must round-trip through `System.Text.Json`**; a service with a `JsonSerializerContext` for the cache adds it
there.

## Recipes

```csharp
// A lookup table for the whole service, surviving a Redis outage.
public sealed record GetCurrenciesQuery : ICacheableQuery<IReadOnlyList<CurrencyDto>>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromHours(1), TimeSpan.FromHours(12))
        .WithFailSafe(TimeSpan.FromHours(24))    // serve a stale entry rather than fail
        .WithJitter(TimeSpan.FromSeconds(10));   // stagger expiry across instances
    public string CacheKey => "all";
    public CacheScope Scope => CacheScope.Global;
}

// Something that depends on the caller.
public sealed record GetMyPermissionsQuery : ICacheableQuery<PermissionSetDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(5));
    public string CacheKey => "permissions";
    public CacheScope Scope => CacheScope.User;
}

// A group evicted by tag.
public sealed record GetOrderSummaryQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public CachePolicy CachePolicy => CachePolicy.Default.WithTags("orders");
    public string CacheKey => OrderId.ToString();
}

public sealed record RebuildOrderProjectionsCommand : ICommand, IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [];
    public IReadOnlyCollection<string> CacheTagsToInvalidate => ["orders"];
}
```

## Telemetry

Both behaviors record on the `SharedKernel.Application` meter and activity source, which `13.ServiceDefaults`'
`WithApplicationTelemetry()` exports.

| Signal | Name | Tags |
| --- | --- | --- |
| Counter | `sharedkernel.application.query.cache.outcome` | `sharedkernel.query.type`, `sharedkernel.cache.scope`, `sharedkernel.cache.outcome` |
| Counter | `sharedkernel.application.command.cache.invalidation` | `sharedkernel.command.type`, `sharedkernel.cache.target`, `sharedkernel.cache.evicted` |
| Activity tags | on the request span | `sharedkernel.cache.outcome`, `sharedkernel.cache.scope` |

Outcomes: `Hit`, `Miss`, `Refreshed`, `NotCachedFailure`, `NotCachedByPredicate`, `BypassedScopeUnavailable`. Logging
uses EventIds 5200–5299. No tenant id, user id, cache key or value is ever a log or metric parameter.

## Pitfalls

- **A `CacheKey` that omits a parameter** that changes the result. The query type, service, tenant and caller are added
  for you; the rest is yours.
- **A caller-dependent result left at `Tenant` scope** serves one user another's data. Declare `CacheScope.User`.
- **A command whose `Scope` differs from its queries** evicts a key nothing wrote.
- **A `TValue` that does not round-trip through `System.Text.Json`** works in memory and fails against Redis.
- **Expecting a failure to be cached.** It never is; model "does not exist" as a successful value if it must be cached.
- **Caching on a command, invalidating from a query.** SK0017 and SK0018 flag both.
- **Eviction without `WithTransactions()`** still runs after the handler succeeded, but "after the commit" is only as
  real as the commit.

## Testing

`16.Testing`'s `FakeCacheService` runs the factory once per key under concurrency, like the real cache, and counts it:

```csharp
var cache = new FakeCacheService();

using var harness = new ApplicationPipelineTestHarness().Configure(app => app.WithCaching());
harness.Services.AddSingleton<ICacheService>(cache);
harness.Services.AddSingleton<ITenantCacheKeyProvider>(new FakeTenantCacheKeyProvider());
harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { TenantId = tenantId });
harness.Build<Program>();

await harness.SendAsync(new GetOrderQuery(orderId));
await harness.SendAsync(new GetOrderQuery(orderId));

Assert.Equal(1, cache.FactoryInvocationCount);   // the second send was a hit
```

## Reference

| Type | Purpose |
| --- | --- |
| `ICacheableQuery<TValue>` | A query whose successful value is cached; also an `IQuery<TValue>` |
| `ICacheableQuery.CacheKey`, `.CachePolicy`, `.Scope`, `.RefreshCache` | Identity in its namespace; policy (`02.Caching`); `Tenant` by default; overwrite on `true` |
| `ICacheableQuery<TValue>.ShouldCache` | Decline to cache one value; caches every success by default |
| `IInvalidatesCache` | `CacheKeysToInvalidate` (`CacheKeyRef`), `CacheTagsToInvalidate` (empty by default), `Scope` (`Tenant` by default) |
| `CacheKeyRef.For<TQuery>(key)` | One entry, named by its owning query type |
| `CacheScope` | `Tenant` = 0, `User`, `Global` |
| `WithCaching()` | Registers both behaviors in their stages and declares the services they need |

Namespace `SharedKernel.Application.Caching`. The behaviors are internal, registered by `WithCaching()`. The package
depends on `SharedKernel.Application` and `SharedKernel.Caching.Abstractions`, is the only package of `05.Application`
that references `02.Caching`, and tracks its public API. Maintainer rules: [`05.Application/CLAUDE.md`](../CLAUDE.md).
