# SharedKernel.Caching.Redis.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

In-memory doubles for the Redis-specific caching contracts, so code that uses them runs without Redis: `FakeRedisHashService` and `FakeTypedHashStore<T>` (hashes with field TTLs and counters, expiry driven by a registered `TimeProvider`) and `FakeRedisChannelService` (pub/sub). Register them with `AddFakeRedisServices()` and `AddFakeTypedHashStore<T>()`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Caching.Redis.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Caching`
- **Types:** `FakeRedisChannelService`, `FakeRedisHashService`, `FakeTypedHashStore`, `RedisCachingServiceCollectionExtensions`

## Dependencies

References `SharedKernel.Caching.Redis.HashStore` and `SharedKernel.Caching.Redis.PubSub`, which bring StackExchange.Redis; that is why these fakes are not in `SharedKernel.Caching.Testing`. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
