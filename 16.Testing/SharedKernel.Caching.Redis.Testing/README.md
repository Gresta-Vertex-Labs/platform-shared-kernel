# SharedKernel.Caching.Redis.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory doubles for the Redis-specific caching contracts — hashes and pub/sub — so code that uses them runs
without Redis and without `AddRedisConnection`.** They are a separate package from
[`SharedKernel.Caching.Testing`](../SharedKernel.Caching.Testing/README.md) because the contracts they implement live
in `SharedKernel.Caching.Redis.HashStore` and `.PubSub`, which bring StackExchange.Redis.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Caching.Redis.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Caching`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `FakeRedisHashService` | `IRedisHashService` | Field get/set (with `JsonTypeInfo<T>`), increments, whole-hash expiry via a registered `TimeProvider`; `Seed<T>`, `SeedRaw`, `GetRawField`, `GetTimeToLive`, `ContainsKey`, `Keys`, `SimulateFailure`, `Reset()` |
| `FakeTypedHashStore<T>` | `ITypedHashStore<T>` | The same over one DTO type, no type info needed; `Seed`, `GetTimeToLive`, `SimulateFailure`, `Reset()` |
| `FakeRedisChannelService` | `IRedisChannelService` | Publish and subscribe in process, at most once like Redis pub/sub; `PublishedMessages`, `GetPublishedMessages<T>(channel, typeInfo)`, `IsSubscribed`, `GetSubscriptionCount`, `SubscribedChannels`, `HandlerExceptions`, `SkippedMessages`, `SimulateFailure`, `Reset()` |

## Registration

```csharp
services.AddFakeRedisServices();                    // IRedisChannelService + IRedisHashService, singletons
services.AddFakeTypedHashStore<SessionState>();     // once per DTO type
services.AddFakeCachingServices();                  // optional: the provider-neutral cache and lock fakes
```

## Example

```csharp
var channels = new FakeRedisChannelService();
var notifier = new PriceChangeNotifier(channels);

await notifier.NotifyAsync(new PriceChanged(productId, 9.99m), ct);

channels.GetPublishedMessages("prices", AppJsonContext.Default.PriceChanged)
    .Should().ContainSingle(m => m.ProductId == productId);
```

## Related packages

- References `SharedKernel.Caching.Redis.HashStore` and `SharedKernel.Caching.Redis.PubSub`.
- [`SharedKernel.Caching.Testing`](../SharedKernel.Caching.Testing/README.md) — `ICacheService`, locks, key providers.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — shared basics (`FakeClock`, `InMemoryLogger`,
  `TestRequestContext`).
