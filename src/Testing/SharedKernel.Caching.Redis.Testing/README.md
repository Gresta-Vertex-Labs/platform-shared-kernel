# SharedKernel.Caching.Redis.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory doubles for the Redis-specific caching contracts — `IRedisHashService`, `ITypedHashStore<T>` and
> `IRedisChannelService` — so code that uses Redis hashes or Pub/Sub is unit-tested without Redis and without
> `AddRedisConnection`.**

| You get | So that |
| --- | --- |
| `FakeRedisHashService` storing the JSON the real service would write | Serialization mismatches fail in the test the way they fail against Redis |
| Whole-hash expiry on a `TimeProvider` you control | TTL logic is tested by advancing time, not by sleeping |
| `HINCRBY`-shaped `IncrementFieldAsync` | Counters start at zero and a non-integer field fails, as on the server |
| `FakeRedisChannelService` delivering in process, one message at a time per subscription | A publish-then-assert test sees every handler run before the `await` completes |
| `PublishedMessages`, `HandlerExceptions`, `SkippedMessages` | You assert what was sent, which handler threw and which message did not deserialize |
| `SimulateFailure` on every fake | The "Redis did not answer" path (`TimeoutException`) is one flag away |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Caching.Redis.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. Production code must never reference a Testing package;
`TestingNeverReferencedByProduction` fails the build's architecture tests when it does.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Caching.Redis.HashStore`, `SharedKernel.Caching.Redis.PubSub` (and through them StackExchange.Redis), `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Caching` |

## Quick start

```csharp
using System.Text.Json.Serialization;
using SharedKernel.Testing.Caching;
using Xunit;

public sealed record PriceChanged(string Sku, decimal Price);

[JsonSerializable(typeof(PriceChanged))]
internal sealed partial class PricingJsonContext : JsonSerializerContext;

public sealed class PriceSignalTests
{
    [Fact]
    public async Task Subscriber_receives_the_published_signal()
    {
        var channels = new FakeRedisChannelService();
        var received = new List<PriceChanged>();

        await using var subscription = await channels.SubscribeAsync(
            "pricing:changed",
            PricingJsonContext.Default.PriceChanged,
            (message, _) => { received.Add(message); return ValueTask.CompletedTask; });

        var receivers = await channels.PublishAsync(
            "pricing:changed", new PriceChanged("SKU-1", 9.99m), PricingJsonContext.Default.PriceChanged);

        Assert.Equal(1, receivers);
        Assert.Equal(new PriceChanged("SKU-1", 9.99m), Assert.Single(received));
        Assert.Single(channels.GetPublishedMessages("pricing:changed", PricingJsonContext.Default.PriceChanged));
    }
}
```

In a real host or `WebApplicationFactory`, register the doubles instead of the Redis packages:

```csharp
services.AddFakeRedisServices();                  // IRedisChannelService + IRedisHashService
services.AddFakeTypedHashStore<SessionState>();   // once per DTO type
```

## How it works

- **Hashes:** `FakeRedisHashService` stores each field as the JSON produced by the `JsonTypeInfo<T>` you pass, and
  reads it back through the one you pass then — a type mismatch throws `JsonException`, as against Redis. A
  `timeToLive` sets the expiry of the whole key together with the write. `ExpireAsync(key, null)` removes the
  expiry (`PERSIST`) and returns `true` only when there was one to remove. Argument validation matches the real
  service (`ArgumentException` for a blank key or empty field, `ArgumentOutOfRangeException` for a non-positive TTL,
  `ArgumentException` for an empty field set); cancellation is checked before the operation.
- **Typed store:** `FakeTypedHashStore<T>` has its own storage (it does not wrap `FakeRedisHashService`) and holds
  values as `T` instances, not JSON. Counters are kept as `long`: reading a counter from a store whose `T` is not
  `long` throws `JsonException` — stricter than Redis, where a counter may read as another numeric type.
- **Pub/Sub:** every `SubscribeAsync` call is an independent subscription that handles its messages one at a time,
  in publish order, until disposed. When no delivery is running, the publisher delivers itself before returning; a
  message published from inside a handler is queued and delivered after the current handler returns. A handler
  exception is recorded and delivery continues; a typed message that does not deserialize is recorded in
  `SkippedMessages` and skipped. Disposing waits for a running handler (unless called from inside it) and cancels
  the handler's token.
- **Differences from Redis:** the receiver count returned by `PublishAsync` is the number of subscriptions on this
  fake, whereas Redis counts connections. There is no connection to lose, so nothing is missed or replayed.
  `PublishedMessages` grows without bound.
- **Lifetimes and threading:** registrations are singletons; every fake is thread-safe.

## Recipes

### 1. Expire a hash by advancing time

```csharp
var time = new FakeTimeProvider();            // Microsoft.Extensions.TimeProvider.Testing
var hashes = new FakeRedisHashService(time);

await hashes.SetFieldAsync("session:42", "user", "ada", MyJson.Default.String, TimeSpan.FromMinutes(20));
Assert.Equal(TimeSpan.FromMinutes(20), hashes.GetTimeToLive("session:42"));

time.Advance(TimeSpan.FromMinutes(21));
Assert.False(hashes.ContainsKey("session:42"));
```

Under `AddFakeRedisServices()` / `AddFakeTypedHashStore<T>()`, the fakes use a `TimeProvider` registered in the
container, else `TimeProvider.System`.

### 2. Read a value stored in an outdated format

```csharp
var hashes = new FakeRedisHashService();
hashes.SeedRaw("profile:42", "settings", "{\"legacy\":true}");

await Assert.ThrowsAsync<JsonException>(async () =>
    await hashes.GetFieldAsync("profile:42", "settings", MyJson.Default.Settings));
```

`Seed<T>` writes a typed value and `GetRawField` returns the stored text; both bypass `SimulateFailure`.

### 3. Prove a handler failure does not silence the channel

```csharp
await using var sub = await channels.SubscribeAsync("jobs", (_, _) => throw new InvalidOperationException("boom"));

await channels.PublishAsync("jobs", "a");
await channels.PublishAsync("jobs", "b");

Assert.Equal(2, channels.HandlerExceptions.Count);
Assert.True(channels.IsSubscribed("jobs"));
```

### 4. Test the "Redis is down" path

```csharp
var hashes = new FakeRedisHashService { SimulateFailure = true };
await Assert.ThrowsAsync<TimeoutException>(async () => await hashes.DeleteAsync("session:42"));
```

## Reference

### Registration

| Method | Registers (singletons) |
| --- | --- |
| `AddFakeRedisServices(this IServiceCollection)` | `IRedisChannelService` → `FakeRedisChannelService`, `IRedisHashService` → `FakeRedisHashService` |
| `AddFakeTypedHashStore<T>(this IServiceCollection)` | `ITypedHashStore<T>` → `FakeTypedHashStore<T>` (no `JsonTypeInfo<T>` needed) |

Neither needs `AddRedisConnection`. Combine with `AddFakeCachingServices()` from
[`SharedKernel.Caching.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Caching.Testing/README.md)
for the provider-neutral cache and lock fakes. Resolve the interface and cast to the fake to reach its inspection
members.

### Types

| Type | Implements | Inspection and control |
| --- | --- | --- |
| `FakeRedisHashService` | `IRedisHashService` | `Keys`, `ContainsKey(key)`, `GetTimeToLive(key)`, `GetRawField(key, field)`, `Seed<T>(key, field, value, typeInfo)`, `SeedRaw(key, field, rawValue)`, `SimulateFailure`, `Reset()`; constructors `()` and `(TimeProvider)` |
| `FakeTypedHashStore<T>` | `ITypedHashStore<T>` | `Keys`, `ContainsKey(key)`, `GetTimeToLive(key)`, `Seed(key, field, value)`, `SimulateFailure`, `Reset()`; constructors `()` and `(TimeProvider)` |
| `FakeRedisChannelService` | `IRedisChannelService` | `PublishedMessages` (`(Channel, Message)`, typed messages as JSON), `GetPublishedMessages(channel)`, `GetPublishedMessages<T>(channel, typeInfo)`, `IsSubscribed(channel)`, `GetSubscriptionCount(channel)`, `SubscribedChannels`, `HandlerExceptions`, `SkippedMessages`, `SimulateFailure`, `Reset()` |

`Reset()` on the channel fake clears the records and ends every subscription without waiting for running handlers.

### Failures the fakes raise

| Condition | Exception |
| --- | --- |
| `SimulateFailure = true` (after argument validation) | `TimeoutException` |
| Stored JSON does not match the requested type | `JsonException` |
| `IncrementFieldAsync` on a field that is not an integer | `InvalidOperationException` (stands in for the Redis server error) |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Caching.Redis.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Caching.Redis.Testing/SharedKernel.Caching.Redis.Testing.Tests)
and prove each fake against the `IRedisHashService`, `ITypedHashStore<T>` and `IRedisChannelService` contracts. Pair
it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `FakeClock`, `TestRequestContext` and the in-memory logger. Test Redis-only behaviour (cluster, reconnects,
connection counts) against a real Redis in an integration test.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | The architecture tests fail a production reference, and nothing reaches Redis |
| Assert the exact receiver count Redis would return | Assert on received messages, or on `GetSubscriptionCount` | The fake counts subscriptions; Redis counts connections |
| Test reconnect or missed-message behaviour against the fake | Use a real Redis | The fake has no connection, so nothing is ever missed |
| Expect `FakeTypedHashStore<T>` to share data with `FakeRedisHashService` | Seed each fake you inject | They have independent storage |
| Read a counter from a `FakeTypedHashStore<T>` whose `T` is not `long` | Use `ITypedHashStore<long>` for counters | The fake is deliberately stricter than Redis and throws `JsonException` |
| Reuse one channel fake across tests without `Reset()` | Create a fresh fake per test | `PublishedMessages` and subscriptions accumulate |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
