# SharedKernel.Caching.Redis.Core

Foundational Redis connection layer for `SharedKernel.Caching`: `IConnectionMultiplexer`
registration (`AddRedisConnection`), `ConnectionHealthState` tracking
(`RedisConnectionHealthTracker`), and an opt-in Polly v8 `ResiliencePipeline` factory
(`AddRedisCircuitBreaker`). This is the dependency root shared by
[`SharedKernel.Caching.Redis`](https://www.nuget.org/packages/SharedKernel.Caching.Redis) (L2),
`SharedKernel.Caching.Redis.DistributedLocking`, `SharedKernel.Caching.Redis.HashStore`, and
`SharedKernel.Caching.Redis.PubSub` — take this package directly only if you need a shared Redis
connection with health tracking and nothing else (no FusionCache, no RedLock, no hash store, no
pub/sub).

## Install

```
dotnet add package SharedKernel.Caching.Redis.Core
```

```xml
<PackageReference Include="SharedKernel.Caching.Redis.Core" Version="1.0.0" />
```

## Usage

```csharp
services.AddRedisConnection("localhost:6379", o => o.ConnectTimeoutMs = 5000);

// Optional — off by default
services.AddRedisCircuitBreaker(o =>
{
    o.Enabled = true;
    o.FailureThreshold = 5;
    o.BreakDuration = TimeSpan.FromSeconds(30);
});

// Inject: IConnectionMultiplexer, RedisConnectionHealthTracker, ResiliencePipeline (when enabled)
```

`AddRedisConnection` uses `TryAddSingleton` — first caller wins. The four capability packages above
call it internally, so most consumers never call these extensions directly.

## Layering

```
SharedKernel.Caching.Redis.Core  →  SharedKernel.Caching.Abstractions
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
