# SharedKernel.ServiceDefaults.Caching.Redis

Redis readiness check over the shared connection from `SharedKernel.Caching.Redis.Core`. One of the
`SharedKernel.ServiceDefaults.*` integration packages: add it only if your service uses Redis.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Caching.Redis" />
```

```csharp
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();

// The one Redis connection every Redis package shares, bound from SharedKernel:Caching:Redis.
builder.Services.AddRedisConnection(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddRedisHealthCheck();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Chain onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`. `AddServiceDefaults()`
already calls `AddSharedKernelHealthChecks()`, which registers the `"startup"` check; calling it a
second time registers `"startup"` twice, and the application throws `ArgumentException: Duplicate
health checks were registered with the name(s): startup` when it starts.

## Behaviour

| | |
| --- | --- |
| Method | `AddRedisHealthCheck(string name = "redis")` |
| Default name | `HealthCheckNames.Redis` (`"redis"`) |
| Tags | `ready`, `redis`, `cache` — never `live` |
| Healthy | Redis answered `PING`; `Data["latency"]` holds the round-trip time |
| Unhealthy | Redis is not connected or did not answer; the description says which, never with the connection string or an exception message |
| Resolves | `IRedisConnectionProbe`, registered by `AddRedisConnection` |

The check sends `PING` over the connection the distributed cache, locks, hash store and pub/sub
already use, so it reports what they see and never opens a second connection: TLS, timeouts and
credentials are those of `AddRedisConnection`. Without `AddRedisConnection` the check fails the
first time it runs.

The check is tagged `ready` and never `live`, so it gates load-balancer rotation through
`/health/ready` without ever causing Kubernetes to restart the pod through `/health/live`.

### Migrating from `AddRedisHealthCheck(connectionString)`

Earlier versions took a connection string and opened their own connection through
`AspNetCore.HealthChecks.Redis`. Register the connection once with `AddRedisConnection` and call
`AddRedisHealthCheck()` without arguments.

## Why a separate package

It alone brings the Redis client (through `SharedKernel.Caching.Redis.Core`). Kept apart from
`SharedKernel.ServiceDefaults.Caching` so that a service caching only in memory never restores it.

The types keep their `SharedKernel.ServiceDefaults.HealthChecks` namespace from before the WO-084
split, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition
base and the full list of integration packages.
