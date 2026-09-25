# SharedKernel.Caching.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

In-memory doubles for `SharedKernel.Caching.Abstractions`: `FakeCacheService`, `FakeTenantCacheService`, `FakeDistributedLockService` (locks, leases and fencing tokens), key providers and a recording warmup strategy. `AddFakeCachingServices()` registers the cache, lock and key-provider fakes in one call. The Redis-specific fakes live in `SharedKernel.Caching.Redis.Testing`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Caching.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Caching`
- **Types:** `CachingServiceCollectionExtensions`, `FakeCacheService`, `FakeCacheWarmupStrategy`, `FakeDistributedLock`, `FakeDistributedLockService`, `FakeTenantCacheKeyProvider`, `FakeTenantCacheService`

## Dependencies

References `SharedKernel.Caching.Abstractions` only. No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
