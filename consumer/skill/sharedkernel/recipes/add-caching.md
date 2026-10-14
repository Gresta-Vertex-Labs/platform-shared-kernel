---
proven-by:
  - samples/Shop/Catalog/Shop.Catalog.Application/Products/GetProduct.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Products/ChangePrice.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/CatalogInfrastructure.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/Pricing/RedisPriceChanges.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/Program.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogPipelineTests.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/RedisTotpGuards.cs
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Stock/StockCommands.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Stock/StockStore.cs
  - samples/Shop/Inventory/Shop.Inventory.Tests/InventoryTests.cs
unproven:
  - Direct ICacheService and ITenantCacheService calls: the reference platform caches only through the query pipeline, so there is no snippet; read the README
  - AddTenantCacheService registration: same reason; the tenant cache is reached through the query pipeline in the reference platform
---
# Add caching

**Use when:** a query result should be served from a cache and evicted after the command that changes it commits,
several replicas need one shared cache (Redis L2 and backplane), a resource must be locked across replicas, small
per-tenant records live in Redis hashes, or replicas must hear a loss-tolerant signal from each other.
**Not when:** a command or consumer must be safe to retry (add-idempotency.md); a signal must not be lost or must
reach another service (add-messaging.md); a job must run once across replicas (add-background-job.md, which uses the
lock from here).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | `ICacheableQuery<T>`, `IInvalidatesCache`, `CacheKeyRef` |
| Application | `SharedKernel.Caching.Abstractions` | `CachePolicy`, `ICacheService`, `ITenantCacheService`, `IDistributedLockService` |
| Infrastructure | `SharedKernel.Caching.FusionCache` | `AddSharedKernelCaching(configuration)`: the in-process L1 cache behind the contracts |
| Infrastructure | `SharedKernel.Caching.Redis.Core` | `AddRedisConnection(configuration)`: the one Redis connection every Redis package shares |
| Infrastructure | `SharedKernel.Caching.Redis` | `AddRedisL2(configuration)`: Redis as L2 and backplane across replicas |
| Infrastructure | `SharedKernel.Caching.Redis.DistributedLocking` | `AddRedisDistributedLocking()`: `IDistributedLockService` on Redis |
| Infrastructure | `SharedKernel.Caching.Redis.HashStore` | `AddRedisHashService()`, `AddTypedHashStore(typeInfo)` |
| Infrastructure | `SharedKernel.Caching.Redis.PubSub` | `AddRedisChannelService()`: `IRedisChannelService` |
| Api/Worker | `SharedKernel.Application.Pipeline.Caching` | `app.WithCaching()`: the caching behavior and post-commit eviction |
| Tests | `SharedKernel.Caching.Testing`, `SharedKernel.Caching.Redis.Testing`, `SharedKernel.Application.Testing` | Fakes and the pipeline harness |

Take only the rows you need: query caching needs the first two Application rows, FusionCache and the pipeline
package; Redis rows are opt-in.

## Steps

1. **Application: cache a query, evict it from the command that changes it.** A query implements
   `ICacheableQuery<T>` (it is an `IQuery<T>`) and names its key and policy; the key is partitioned by query type and
   tenant for you. A command implements `IInvalidatesCache` and points at the query's key with `CacheKeyRef.For<TQuery>`.
   Eviction runs after the transaction commits.

   ```csharp
   using System.Globalization;
   using SharedKernel.Application.Caching;
   using SharedKernel.Application.Messaging;
   using SharedKernel.Caching.Abstractions;

   public sealed record GetProductQuery(Guid ProductId) : ICacheableQuery<ProductDto>
   {
       public CachePolicy CachePolicy =>
           CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

       public string CacheKey => ProductId.ToString("D", CultureInfo.InvariantCulture);
   }

   public sealed record ChangePriceCommand(Guid ProductId, decimal Price, string Currency)
       : ICommand,
           IInvalidatesCache
   {
       public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
           [CacheKeyRef.For<GetProductQuery>(ProductId.ToString("D", CultureInfo.InvariantCulture))];
   }
   ```

   The handlers are ordinary `IQueryHandler<,>`/`ICommandHandler<>` and know nothing about the cache. To cache data
   that is not a query result, inject `ICacheService` (or `ITenantCacheService` for tenant data) instead; the
   reference platform does not do this, so read the `SharedKernel.Caching.Abstractions` README first.

2. **Application: lock a resource across replicas.** `IDistributedLockService` is an Abstractions contract, so a
   handler may use it. `TryAcquireAsync` returns `null` when the resource is busy; turn that into a `Result` failure.
   Put the tenant in the resource name.

   ```csharp
   using SharedKernel.Caching.Abstractions;
   using SharedKernel.Execution.Tenancy;

   public sealed class SkuLocks(IDistributedLockService locks)
   {
       private static readonly DistributedLockOptions Options = new()
       {
           Expiry = TimeSpan.FromSeconds(15),
           WaitTime = TimeSpan.FromSeconds(10),
       };

       public ValueTask<IDistributedLock?> AcquireAsync(TenantId tenant, string sku, CancellationToken ct) =>
           locks.TryAcquireAsync($"inventory:{tenant.Value:N}:{sku}", Options, ct);
   }
   ```

3. **Infrastructure: register the cache and the Redis connection once.** `AddRedisConnection` comes first and is
   shared by L2, locking, hashes and pub/sub.

   ```csharp
   using SharedKernel.Caching.FusionCache.Extensions;
   using SharedKernel.Caching.Redis.Core.Extensions;
   using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
   using SharedKernel.Caching.Redis.Extensions;
   using SharedKernel.Caching.Redis.HashStore.Extensions;
   using SharedKernel.Caching.Redis.PubSub.Extensions;

   services.AddRedisConnection(configuration);
   services.AddSharedKernelCaching(configuration).AddRedisL2(configuration);
   services.AddRedisDistributedLocking();
   services.AddRedisHashService();                                   // IRedisHashService
   services.AddTypedHashStore(InventoryJsonContext.Default.StockLevel); // ITypedHashStore<StockLevel>
   services.AddRedisChannelService();                                // IRedisChannelService
   ```

4. **Infrastructure: Redis hashes and pub/sub behind your own ports.** These types live in Adapter packages, so wrap
   them in a class the Application sees through an interface. Serialization uses a source-generated
   `JsonSerializerContext`.

   ```csharp
   using SharedKernel.Caching.Redis.HashStore;
   using SharedKernel.Execution.Tenancy;

   public sealed class StockLevelCache(ITypedHashStore<StockLevel> hashes)
   {
       private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(1);

       public async ValueTask<StockLevel?> GetAsync(TenantId tenant, string sku, CancellationToken ct)
       {
           var lookup = await hashes.GetFieldAsync(Key(tenant), sku, ct);
           return lookup.TryGetValue(out StockLevel? level) ? level : null;
       }

       public ValueTask SetAsync(TenantId tenant, StockLevel level, CancellationToken ct) =>
           hashes.SetFieldAsync(Key(tenant), level.Sku, level, TimeToLive, ct);

       private static string Key(TenantId tenant) => $"inventory:stock:{tenant.Value:D}";
   }
   ```

   ```csharp
   using SharedKernel.Caching.Redis.PubSub;

   public sealed class RedisPriceChangeBroadcaster(IRedisChannelService channels) : IPriceChangeBroadcaster
   {
       public async Task PublishAsync(PriceChange change, CancellationToken cancellationToken) =>
           await channels.PublishAsync(
               PriceChannels.PriceChanged,
               change,
               PriceChangeJsonContext.Default.PriceChange,
               cancellationToken);
   }
   ```

   Subscribe in an `IHostedService` with `channels.SubscribeAsync(channel, typeInfo, handler, ct)` and dispose the
   returned subscription on stop. Counters with an expiry use `IRedisHashService.IncrementFieldAsync(key, field,
   timeToLive: …)`.

5. **Api/Worker: turn on query caching.** Add `WithCaching()` to the one `AddSharedKernelApplication` call.

   ```csharp
   using SharedKernel.Application.Mediator.MediatR;
   using SharedKernel.Application.Pipeline;
   using SharedKernel.Application.Pipeline.Caching;
   using SharedKernel.ServiceDefaults.HealthChecks;

   builder.Services.AddSharedKernelApplication(
       typeof(CreateProductCommand).Assembly,
       app => app.UseMediatR().WithTransactions().WithCaching());

   builder.Services.AddHealthChecks().AddSharedKernelReadiness(); // includes the Redis probe
   ```

6. **Configuration:** `SharedKernel:Caching` (the cache), `SharedKernel:Caching:Redis` (the connection),
   `SharedKernel:Caching:Redis:L2` (the second level). The keys are in each package README.

## Test it

- Query caching: run the real pipeline with `ApplicationPipelineTestHarness` and `services.AddFakeCachingServices()`,
  `.Configure(app => app.WithTransactions().WithCaching())`, then send the query twice and the command once and
  assert the second read is a hit and the read after the command is fresh (advance a `FakeClock` between them).
- Locks: construct your class over `new FakeDistributedLockService()` (namespace `SharedKernel.Testing.Caching`) and
  assert that a held resource returns `null` and is released on dispose.
- Hashes and pub/sub: `new ServiceCollection().AddFakeRedisServices()` gives in-memory `IRedisHashService` and
  `IRedisChannelService`; `AddFakeTypedHashStore<T>()` gives an `ITypedHashStore<T>`.
- Cross-replica behavior (L2, backplane, pub/sub delivery) needs a real Redis; test it end to end.

## Check

- `SK0017` flags a command that implements `ICacheableQuery<T>`; `SK0018` a query that implements
  `IInvalidatesCache`; `SK0041` two cacheable queries that share a simple type name.
- `SK0007` flags `IRedisChannelService` used where a command or event belongs on the message bus.
- `SK0022` flags a raw literal used as a cross-cutting key; keep channel names and key prefixes in constants.
- Host start fails when `WithCaching()` finds no `ICacheService` (register `AddSharedKernelCaching`).
- Registration throws when a Redis package is added before `AddRedisConnection`, or when `AddRedisConnection` is
  called twice.
- An Application project referencing a Redis or FusionCache package breaks the four-project rule; the service's
  architecture tests catch it. Those packages are Infrastructure only.

## Read next

`SharedKernel.Caching.Abstractions`, `SharedKernel.Application.Pipeline.Caching`, `SharedKernel.Caching.FusionCache`,
`SharedKernel.Caching.Redis.Core`, `SharedKernel.Caching.Redis`, `SharedKernel.Caching.Redis.DistributedLocking`,
`SharedKernel.Caching.Redis.HashStore`, `SharedKernel.Caching.Redis.PubSub`, `SharedKernel.Caching.Testing`,
`SharedKernel.Caching.Redis.Testing`.
