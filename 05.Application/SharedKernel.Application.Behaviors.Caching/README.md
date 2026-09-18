# SharedKernel.Application.Behaviors.Caching

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MediatR 12.4](https://img.shields.io/badge/MediatR-12.4-5c6bc0)](https://github.com/jbogard/MediatR)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Published: GitHub Packages](https://img.shields.io/badge/published-GitHub%20Packages-success)
![Scopes: fail closed](https://img.shields.io/badge/scopes-fail%20closed-success)
![Eviction: after commit](https://img.shields.io/badge/eviction-after%20commit-orange)

> **Cache a query by adding one interface to it, and let the command that changes the data evict it — after the
> transaction commits, never before. Keys are partitioned by query type, tenant and caller, so nothing can read
> anything it should not.**

Two MediatR pipeline behaviors. A query declares `ICacheableQuery<TValue>` and is served from cache — in memory,
or Redis through `02.Caching` — without its handler knowing a cache exists. A command declares `IInvalidatesCache`
and names what it made stale. Everything else — key construction, tenant and caller partitioning, stampede
protection, the "never cache a failure" rule, and evicting only once the write has actually landed — is the
behavior's job, not yours.

```csharp
// Read side: one interface, and the handler never changes.
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    public string CacheKey => OrderId.ToString();
}

// Write side: name the query you invalidated, not a key string.
public sealed record ApproveOrderCommand(Guid OrderId) : ICommand, IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<GetOrderQuery>(OrderId.ToString())];
}
```

```text
GetOrderQuery(42)   tenant acme, first call        -> handler runs, value cached    outcome Miss
                    tenant acme, second call       -> served from cache             outcome Hit
                    tenant other, same order id    -> handler runs (different key)  outcome Miss
                    handler returns NotFound       -> failure returned, NOT cached  outcome NotCachedFailure
                    tenant could not be resolved   -> cache skipped, handler runs   outcome BypassedScopeUnavailable

ApproveOrderCommand(42)   handler succeeds, transaction commits  -> entry evicted, after the commit
                          handler returns a failed Result        -> nothing evicted
                          handler throws                         -> nothing evicted
                          client disconnects mid-request         -> entry still evicted (uncancellable)
```

| You get | So that |
| --- | --- |
| A cache around any `ICacheableQuery<TValue>` | A hot read is served from memory or Redis, and the handler stays a plain handler |
| Keys partitioned by query type, tenant and caller | Two queries cannot read each other's entries, one tenant cannot read another's, and one user cannot read another's |
| Scopes that fail closed | A missing tenant or caller skips the cache instead of quietly writing under a wider key every request would share |
| A failed `Result` that is never cached | A transient `NotFound` is not pinned for the whole expiry window |
| Stampede protection on every cached query | A cold key under load runs the handler once, not once per concurrent caller |
| Eviction that waits for the commit, and cannot be cancelled | Nothing evicts on a write that has not landed, and a client disconnect cannot leave the cache serving stale data |
| `CacheKeyRef.For<TQuery>(key)` | Renaming a query is a compile error at the command, not a missed eviction found in production |
| Hit and miss counters tagged by query type | You can answer "what is the hit ratio of `GetOrderQuery`", not just "of this key prefix" |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which member do I need?](#which-member-do-i-need)
- [How it works](#how-it-works)
- [Cache scope](#cache-scope)
- [How keys are built](#how-keys-are-built)
- [Invalidating after a command](#invalidating-after-a-command)
- [Refreshing, and declining to cache](#refreshing-and-declining-to-cache)
- [Why the value is cached and not the `Result`](#why-the-value-is-cached-and-not-the-result)
- [Telemetry](#telemetry)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [Deliberately not included](#deliberately-not-included)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

The packages are published to GitHub Packages. Add the feed once, in a `nuget.config` at your repository root:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="shared-kernel" value="https://nuget.pkg.github.com/Gresta-Vertex-Labs/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="shared-kernel"><package pattern="SharedKernel.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

GitHub Packages needs a token even to read: a personal access token with `read:packages` locally, or `GITHUB_TOKEN`
in GitHub Actions.

```shell
dotnet add package SharedKernel.Application.Behaviors.Caching
```

This is an opt-in sibling of [`SharedKernel.Application.Behaviors`](../SharedKernel.Application.Behaviors/README.md),
for one reason: it needs `SharedKernel.Caching.Abstractions`, and the core behaviors package refuses to put a cache
dependency in front of every service that never caches anything. Reference this only when you want caching.

## Quick start

```csharp
using SharedKernel.Application.Behaviors.Caching.Extensions;
using SharedKernel.Caching.FusionCache.Extensions;

builder.Services.AddSharedKernelCaching(o => o.ServiceName = "orders");   // ICacheService + key providers

builder.Services.AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()
    .AddCachingBehaviors()          // both behaviors, each in its correct stage
    .AddTransactionBehavior()       // so eviction has a commit to follow
    .Build();
```

`AddCachingBehaviors()` puts the caching behavior in the **Query** stage and the invalidation behavior in the
**Command** stage, and declares `ICacheService` and `ITenantCacheKeyProvider` as required. `Build()` throws at
startup naming whichever is missing — never at the first request. `AddSharedKernelCaching()` registers both.

```csharp
// A cached query. The handler is unchanged and unaware.
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    public string CacheKey => OrderId.ToString();
}

internal sealed class GetOrderHandler(IOrderRepository orders) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct) =>
        await orders.FindAsync(query.OrderId, ct) is { } order
            ? order.ToDto()
            : Error.NotFound("order.not_found", $"Order {query.OrderId} does not exist.");
}
```

## Which member do I need?

| I want to… | Declare | On |
| --- | --- | --- |
| Cache a query's result | `ICacheableQuery<TValue>` | the query |
| Set durations, tags, fail-safe, jitter | `CachePolicy` | the query |
| Say what makes this instance unique | `CacheKey` | the query |
| Cache per tenant (the default) | nothing — `Scope` defaults to `Tenant` | — |
| Cache per caller | `Scope => CacheScope.User` | the query |
| Cache one entry for the whole service | `Scope => CacheScope.Global` | the query |
| Force a reload that also refreshes the entry | `RefreshCache => true` | the query |
| Keep an empty or negative result out of the cache | `ShouldCache(TValue)` | the query |
| Evict entries after a write commits | `IInvalidatesCache` | the command |
| Name one entry to evict | `CacheKeyRef.For<TQuery>(key)` | the command |
| Evict a whole group of entries | `CacheTagsToInvalidate` + `CachePolicy.WithTags(...)` | both |

## How it works

```mermaid
sequenceDiagram
    autonumber
    participant C as Caller
    participant CB as Caching behavior<br/>(Query stage)
    participant Cache as ICacheService
    participant H as Handler

    C->>CB: GetOrderQuery(42)
    CB->>CB: build key from scope + query type + CacheKey
    alt scope identity missing
        CB->>H: run the handler, cache nothing (Warning logged)
        H-->>C: Result
    else key built
        CB->>Cache: GetOrSetAsync(key, factory, policy)
        alt hit
            Cache-->>CB: cached TValue
            CB-->>C: Result.Success(value)
        else miss
            Cache->>H: factory runs the handler once
            H-->>Cache: Result
            alt success and ShouldCache
                Cache->>Cache: store TValue
            else failure or declined
                Cache->>Cache: SkipCaching()
            end
            Cache-->>CB: value
            CB-->>C: Result
        end
    end
```

The handler runs **inside** one `ICacheService.GetOrSetAsync` call, so concurrent identical queries do not all run
it. Eager refresh and factory timeouts are switched off on the query's policy — either would let the cache run the
handler in the background after the request's DI scope had been disposed. Fail-safe, durations, tags and jitter are
all kept exactly as the query declared them.

## Cache scope

Every query and every invalidating command declares the identity its entries are partitioned by.

| Scope | The key carries | Use it for |
| --- | --- | --- |
| `CacheScope.Tenant` *(default)* | the tenant | anything a multi-tenant service caches |
| `CacheScope.User` | the caller, plus the tenant when there is one | anything that varies by who is asking: filtered by permissions, ownership or preferences |
| `CacheScope.Global` | nothing | genuinely tenant- and caller-independent data: reference tables, ISO code lists, published rate cards |

```csharp
public sealed record GetMyOrdersQuery : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => "mine";
    public CacheScope Scope => CacheScope.User;     // the result depends on the caller
}
```

**Scopes fail closed.** A `Tenant` query on a request with no resolved tenant, or a `User` query with no
authenticated caller, does *not* fall back to a wider key. The cache is skipped, the handler runs, and the skip is
logged at `Warning`. Falling back is the dangerous option: every request whose tenant resolution failed would share
one entry, across tenants. Widening is always an explicit `CacheScope.Global`, never an accident.

`CacheScope.Tenant` is the zero value, so `default(CacheScope)` and both interface defaults land on the safe option.

The scope comes from `IRequestContext` — `05.Application`'s local seam, which your service bridges to its real
identity source at the composition root. No `IRequestContext` registered means `Tenant` and `User` queries are never
cached, which is the correct behaviour for a service that has not wired identity up yet.

## How keys are built

Every key goes through the registered `ITenantCacheKeyProvider`, never string interpolation, so it carries the
owning service name and every segment is escaped.

```text
Global   {service}:{QueryType}:{CacheKey}
Tenant   {service}:@{tenant}:{QueryType}:{CacheKey}
User     {service}:@{tenant}:{QueryType}:{CacheKey}:u:{userId}

orders:@8f3a…:GetOrderQuery:42
orders:@8f3a…:GetMyOrdersQuery:mine:u:auth0%7C7c1
```

`CacheKey` is therefore the query's identity **within its own namespace**, not a whole key. Build it from every
parameter that changes the result — `$"{OrderId}"`, or `$"{OrderId}:{Currency}"` — and nothing else.

The `{QueryType}` segment is doing real work. An entry holds the bare `TValue` as JSON with no type discriminator,
so if two query types shared a key, the second would deserialize the first's payload into its own type — and
`System.Text.Json` does that on a best-effort basis, leaving unmatched members at their defaults and raising
nothing. You would get a half-empty object and no error. **SK0041** reports the one way that namespace can still
collapse: two cacheable queries sharing a simple type name.

Tenant-scoped entries also carry the tenant-wide tag, so `ITenantCacheService.RemoveTenantAsync` removes a tenant's
cached query results along with the rest of its entries.

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

A command names the **query** that owns each entry rather than a raw key, because keys are namespaced per query
type — there is no whole-key form for a command to repeat. `CacheKeyRef.For<TQuery>(key)` captures that type, so
renaming or moving the query breaks the build at the command instead of silently skipping an eviction.

The command's `Scope` must match the queries it invalidates. Both default to `Tenant`, so they agree unless you
change one; a mismatch evicts a key those queries never wrote.

**Eviction happens after the commit.** On success the behavior registers an `ICommandScope.OnCompleted` callback
rather than evicting directly, and `CommandScopeBehavior` runs those callbacks only once the outermost command's
`next()` — which includes `TransactionBehavior`'s commit — has returned. A failed `Result` or a thrown exception
registers nothing. This holds regardless of registration order; it is a property of the command scope, not a
pipeline-ordering trick. Without it, a concurrent reader could repopulate the cache from pre-commit state.

**Eviction is not cancellable, and does not give up early.** The callback runs with `CancellationToken.None`: the
write has already committed, so honouring a cancelled request token would abandon the eviction and leave the cache
serving a superseded value for the entry's whole lifetime. Each key and tag is evicted under its own `try`/`catch`
and logged at `Error`, so one unreachable key cannot skip the rest.

Keys and tags are built and validated **before** the handler runs, so a malformed target fails the command rather
than the post-commit callback, after the change is already saved.

## Refreshing, and declining to cache

`RefreshCache` ignores any existing entry, runs the handler, and writes the result over the entry — the
force-refresh path for a "reload" button, or a read that must observe a write made outside this service. It still
writes, so the next ordinary caller is served the refreshed value; it is not a way to opt one call out of caching.

```csharp
public sealed record GetOrderQuery(Guid OrderId, bool Reload = false) : ICacheableQuery<OrderDto>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => OrderId.ToString();
    public bool RefreshCache => Reload;
}
```

A failed refresh leaves the existing entry alone. A transient fault is not evidence that the cached value is wrong,
and dropping the entry would turn one upstream blip into a cache-wide miss storm.

`ShouldCache(TValue)` decides whether a particular successful value is worth caching — typically to keep an empty
or negative result out when the caller is likely to write the missing data immediately. Returning `false` returns
the value uncached and leaves any existing entry untouched.

```csharp
public bool ShouldCache(IReadOnlyList<OrderDto> value) => value.Count > 0;
```

It runs inside the cache factory, so it must be pure and must not throw — a throw there propagates to every waiting
caller.

## Why the value is cached and not the `Result`

`Result` and `Result<T>` have private constructors and `Value`/`Error` properties that throw in the opposite state,
so a reflection-based JSON serializer cannot even write one — verified: FusionCache's serializer throws
`FusionCacheSerializationException` on the L2 write. Caching the `TValue` and rebuilding
`Result<TValue>.Success(value)` on a hit sidesteps that entirely, and keeps `01.Core`'s `Result` free of any
serialization concern.

The consequence for you: **`TValue` must round-trip through `System.Text.Json`.** If your service registers a
`JsonSerializerContext` for the cache, add `TValue` to it.

## Telemetry

Both behaviors record under the existing `"SharedKernel.Application"` meter and activity source, so
`13.ServiceDefaults`' `WithApplicationTelemetry()` exports them with no extra registration and no new instrument
name to add anywhere.

| Signal | Name | Tags |
| --- | --- | --- |
| Counter | `sharedkernel.application.query.cache.outcome` | `sharedkernel.query.type`, `sharedkernel.cache.scope`, `sharedkernel.cache.outcome` |
| Counter | `sharedkernel.application.command.cache.invalidation` | `sharedkernel.command.type`, `sharedkernel.cache.target`, `sharedkernel.cache.evicted` |
| Activity tags | on the request span | `sharedkernel.cache.outcome`, `sharedkernel.cache.scope` |

Outcomes: `Hit`, `Miss`, `Refreshed`, `NotCachedFailure`, `NotCachedByPredicate`, `BypassedScopeUnavailable`. Hit
ratio for one query type is `Hit / (Hit + Miss)`; the other four tell you *why* a call was not a plain hit or miss.

Logging uses `[LoggerMessage]` at EventIds **5200-5299**, this package's sub-block of the `05.Application` range.
No tenant id, user id, cache key or cached value is ever a log or metric parameter — cache keys are high-cardinality
and can carry identifiers, and neither belongs in a metric dimension.

## Recipes

### 1. Cache a lookup table for the whole service

```csharp
public sealed record GetCurrenciesQuery : ICacheableQuery<IReadOnlyList<CurrencyDto>>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromHours(1), TimeSpan.FromHours(12));
    public string CacheKey => "all";
    public CacheScope Scope => CacheScope.Global;   // no tenant or caller affects this
}
```

### 2. Cache something that depends on the caller

```csharp
public sealed record GetMyPermissionsQuery : ICacheableQuery<PermissionSetDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(5));
    public string CacheKey => "permissions";
    public CacheScope Scope => CacheScope.User;
}
```

### 3. Evict a whole group with a tag

```csharp
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
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

### 4. Give a "reload" button a real refresh

```csharp
public sealed record GetDashboardQuery(bool Reload = false) : ICacheableQuery<DashboardDto>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(2));
    public string CacheKey => "dashboard";
    public bool RefreshCache => Reload;
}
```

### 5. Do not cache an empty page

```csharp
public sealed record SearchOrdersQuery(string Term) : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.For(TimeSpan.FromMinutes(1));
    public string CacheKey => Term;

    public bool ShouldCache(IReadOnlyList<OrderDto> value) => value.Count > 0;
}
```

### 6. Compose a key from more than one parameter

```csharp
public sealed record GetOrderTotalQuery(Guid OrderId, CurrencyCode Currency) : ICacheableQuery<MoneyDto>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => $"{OrderId}:{Currency}";   // every input that changes the result
}
```

### 7. Survive a Redis outage with fail-safe

```csharp
public CachePolicy CachePolicy =>
    CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
        .WithFailSafe(TimeSpan.FromHours(2))     // serve a stale entry rather than fail
        .WithJitter(TimeSpan.FromSeconds(10));   // stagger expiry across instances
```

### 8. Test a cached query

```csharp
var cache = new FakeCacheService();               // SharedKernel.Testing
services.AddSingleton<ICacheService>(cache);
services.AddSingleton<ITenantCacheKeyProvider>(new FakeTenantCacheKeyProvider());
services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors().Build();

await sender.Send(new GetOrderQuery(id));
await sender.Send(new GetOrderQuery(id));

cache.FactoryInvocationCount.Should().Be(1);      // second dispatch was a hit
```

## Reference

| Type | Purpose |
| --- | --- |
| `ICacheableQuery<TValue>` | A query whose successful value is cached. Also an `IQuery<TValue>`, so a query declares it alone |
| `ICacheableQuery.CacheKey` | This instance's identity within its own namespace |
| `ICacheableQuery.CachePolicy` | Durations, tags, fail-safe, jitter — `02.Caching.Abstractions` |
| `ICacheableQuery.Scope` | `Tenant` (default), `User` or `Global` |
| `ICacheableQuery.RefreshCache` | Ignore the entry, run the handler, overwrite. Default `false` |
| `ICacheableQuery<TValue>.ShouldCache` | Decline to cache one value. Default: cache every success |
| `IInvalidatesCache` | A command declaring what it made stale |
| `IInvalidatesCache.CacheKeysToInvalidate` | The entries to evict, as `CacheKeyRef` |
| `IInvalidatesCache.CacheTagsToInvalidate` | The tags to evict. Default empty |
| `IInvalidatesCache.Scope` | Must match the queries being invalidated. Default `Tenant` |
| `CacheKeyRef.For<TQuery>(key)` | Names one entry by its owning query type |
| `CacheScope` | `Tenant` = 0, `User`, `Global` |
| `AddCachingBehaviors()` | Registers both behaviors in their stages and declares their required services |

The two behavior types are **internal**. They are registered by `AddCachingBehaviors()` and resolved by MediatR;
the package's contract is the interfaces above.

## Pitfalls

- **A `CacheKey` that omits a parameter.** Every input that changes the result belongs in the key. The query type,
  service name, tenant and caller are added for you; the rest is yours.
- **A caller-dependent result left at `Tenant` scope.** If the value is filtered by permissions or ownership,
  declare `CacheScope.User` — otherwise one user is served another's data.
- **A command whose `Scope` disagrees with its queries.** It evicts a key nothing ever wrote, and the stale entry
  survives. Both default to `Tenant`; change them together or not at all.
- **A `TValue` that does not round-trip through `System.Text.Json`.** It works against the in-memory layer and
  fails against the distributed one, so it passes locally and breaks in an environment with Redis.
- **Two cacheable queries with the same simple type name.** They share a cache namespace. SK0041 reports it.
- **Expecting a failure to be cached.** It never is — deliberately. A query that should remember "this does not
  exist" must model that as a successful value, not a `NotFound`.
- **`ShouldCache` that throws or has side effects.** It runs inside the cache factory, where a throw reaches every
  waiting caller.
- **Caching on a command.** SK0017 flags a command implementing the query marker; SK0018 flags a query implementing
  the command marker. Caching is for queries, invalidation is for commands.
- **Expecting eviction without a transaction behavior.** `AddTransactionBehavior()` is what gives the callback a
  commit to follow; without it the callback still runs after the handler, which is usually what you want, but the
  "after commit" guarantee is only as real as the commit.

## Design decisions

| Decision | Why |
| --- | --- |
| A separate package from `SharedKernel.Application.Behaviors` | The core behaviors package will not put `SharedKernel.Caching.Abstractions` in front of every service that never caches anything |
| Cache `TValue`, never `Result<TValue>` | `Result` has private constructors, so a reflection-based serializer cannot write it. Caching the value keeps `01.Core` free of serialization concerns |
| Keys namespaced by query type | Without it, two queries picking the same key silently deserialize each other's payloads. The namespace is what makes `CacheKey` safe to write casually |
| A command names the query, not the key | Keys carry the query type, so a raw key string cannot be reconstructed by a command — and naming the type makes a rename a compile error |
| Scope defaults to `Tenant` and fails closed | The unsafe direction is silent widening. A missing identity skipping the cache costs a cache miss; falling back costs a cross-tenant read |
| `GetOrSetAsync` with `SkipCaching`, not get-then-set | Stampede protection on the cold path, without ever writing a failure |
| Eager refresh and factory timeouts forced off | Both let the cache run the handler after the request's DI scope is disposed |
| Eviction deferred to `ICommandScope.OnCompleted` | Makes "after the commit" structural rather than a registration-order fact that a later edit could break |
| Eviction uncancellable, and isolated per key | The write has already committed; a disconnect or one bad key must not leave the cache stale |
| Instruments on the existing `"SharedKernel.Application"` meter | A new meter name would need its own `AddMeter` call and would silently export nothing until a host added it |
| Behaviors internal | Nothing outside should construct them, and it keeps the public surface to the contract |

## Deliberately not included

- **No automatic key derivation from the query's properties.** Reflecting over a record to build a key is exactly
  how a key silently stops matching when a property is added. The query states its key.
- **No cache-aside helper for handlers.** If a handler needs to cache something that is not its own result, inject
  `ICacheService` directly — that is not a pipeline concern.
- **No output or HTTP response caching.** That is a `14.Presentation` concern, with different invalidation rules.
- **No second-level invalidation graph.** A command names what it made stale; the package does not try to infer
  dependencies between queries.
- **No cache warming.** `02.Caching.FusionCache` owns warmup.

## AI quick reference

```text
PACKAGE     SharedKernel.Application.Behaviors.Caching (05.Application). net10.0. Opt-in sibling of
            SharedKernel.Application.Behaviors; the only package in 05.Application allowed to reference
            SharedKernel.Caching.Abstractions.
REGISTER    services.AddSharedKernelCaching(o => o.ServiceName = "svc");        // ICacheService + ITenantCacheKeyProvider
            services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors().AddTransactionBehavior().Build();
            Build() throws naming ICacheService / ITenantCacheKeyProvider if missing.
QUERY       record Q(...) : ICacheableQuery<TValue>   // ALSO an IQuery<TValue>; response is Result<TValue>
              CachePolicy CachePolicy => CachePolicy.Default | .For(l1, l2) | .WithTags(..) | .WithFailSafe(..) | .WithJitter(..)
              string CacheKey => "<identity WITHIN this query's namespace, not a whole key>"
              CacheScope Scope => Tenant(0, default) | User | Global
              bool RefreshCache => false      // true: skip read, run handler, OVERWRITE entry
              bool ShouldCache(TValue v) => true
COMMAND     record C(...) : ICommand, IInvalidatesCache
              IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<Q>(key), ...]
              IReadOnlyCollection<string> CacheTagsToInvalidate => []      // optional
              CacheScope Scope => Tenant                                   // MUST match the queries
KEYS        {service}:{QueryType}:{CacheKey}                       Global
            {service}:@{tenant}:{QueryType}:{CacheKey}             Tenant
            {service}:@{tenant}:{QueryType}:{CacheKey}:u:{userId}  User
            Built via ITenantCacheKeyProvider; every segment escaped. Never interpolate a key yourself.
SCOPE       Identity comes from IRequestContext (TenantId, UserId). MISSING IDENTITY => cache skipped, handler runs,
            Warning logged. NEVER falls back to a wider key.
SEMANTICS   Handler runs inside ONE ICacheService.GetOrSetAsync => stampede-protected. Failed Result is returned and
            NEVER cached (SkipCaching). Eager refresh + factory timeouts forced off. TValue must round-trip STJ.
            Eviction: registered via ICommandScope.OnCompleted, runs AFTER the outermost commit, with
            CancellationToken.None, per-key try/catch. Failure or throw => nothing registered.
TELEMETRY   Meter + ActivitySource "SharedKernel.Application" (exported by WithApplicationTelemetry()).
            sharedkernel.application.query.cache.outcome {query.type, cache.scope, cache.outcome}
            sharedkernel.application.command.cache.invalidation {command.type, cache.target, cache.evicted}
            Outcomes: Hit Miss Refreshed NotCachedFailure NotCachedByPredicate BypassedScopeUnavailable.
            Logging [LoggerMessage] EventIds 5200-5299. No tenant/user/key/value in logs or metric tags.
ANALYZERS   SK0017 command implements ICacheableQuery. SK0018 query implements IInvalidatesCache.
            SK0041 two ICacheableQuery types share a simple type name (their key namespaces collapse).
TEST        SharedKernel.Testing FakeCacheService (stampede-faithful: per-key gate + post-gate re-check),
            FakeTenantCacheKeyProvider. Assert FactoryInvocationCount for hit/miss.
FORBIDDEN   Constructing the behaviors (internal). Interpolating a whole cache key. Leaving a caller-dependent query
            at Tenant scope. Implementing ICacheableQuery on a command, or IInvalidatesCache on a query.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and every public member is documented;
  the build fails on an undocumented member or an unrecorded API change.
- **Failures are never cached**, and a cached entry is never a `Result` — both covered by tests against a real
  FusionCache over a shared distributed cache.
- **Scope isolation is tested, not asserted:** one tenant cannot read another's entry, one caller cannot read
  another's, and a missing identity caches nothing.
- **Stampede semantics are pinned against a real cache.** One test proves the handler runs exactly once for
  concurrent callers on a hit path; another proves each caller gets its own failure on the skip path. The failure
  path depends on a skipped factory value not being broadcast to waiters, so that contract is held in place by a
  test rather than by a comment.
- **Eviction follows the commit**, proved end to end through a real `ServiceCollection`, a real MediatR pipeline and
  a real dispatch — including an independent cross-domain lock in `00.Governance`.
- **Thread-safe.** The behaviors hold no mutable state; per-request state lives on the stack.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
