# SharedKernel.Caching.Redis.HashStore

Structured Redis Hash storage: `IRedisHashService` (low-level, explicit `JsonTypeInfo<T>` per call)
and `ITypedHashStore<T>` (AOT-safe typed wrapper, no per-call `JsonTypeInfo<T>`) — for sessions,
counters, and typed DTOs. Depends only on `SharedKernel.Caching.Abstractions` +
`SharedKernel.Caching.Redis.Core` — does not transitively reference `SharedKernel.Caching.Redis`
(L2), distributed locking, or the pub/sub package.

## Install

```
dotnet add package SharedKernel.Caching.Redis.HashStore
```

```xml
<PackageReference Include="SharedKernel.Caching.Redis.HashStore" Version="1.0.0" />
```

## Usage

```csharp
services.AddRedisConnection("localhost:6379"); // IConnectionMultiplexer via .Redis.Core

var builder = new MyCachingBuilder(services); // any ICachingBuilder wrapping `services`
builder.AddRedisHashService()
       .AddTypedHashStore(MyAppSerializerContext.Default.SessionDto);
// Inject: IRedisHashService, ITypedHashStore<SessionDto>
```

```csharp
public sealed class SessionStore(ITypedHashStore<SessionDto> sessions)
{
    public ValueTask<SessionDto?> GetAsync(string sessionId, CancellationToken ct) =>
        sessions.GetFieldAsync(key: "sessions", field: sessionId, ct);

    public ValueTask SaveAsync(string sessionId, SessionDto session, CancellationToken ct) =>
        sessions.SetFieldAsync(key: "sessions", field: sessionId, value: session, ct);
}
```

`AddRedisHashService` requires `AddRedisConnection` (directly, or transitively via `AddRedisL2` /
`AddRedisDistributedLocking` / `AddRedisChannelService`) to have registered `IConnectionMultiplexer`
first. `AddTypedHashStore<T>` requires `AddRedisHashService` to have been called first.

## Layering

```
SharedKernel.Caching.Redis.HashStore  →  SharedKernel.Caching.Abstractions, SharedKernel.Caching.Redis.Core
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
