# SharedKernel.Idempotency.Redis

Atomic, tenant-scoped, Redis-backed implementation of the platform's three idempotency contracts:

- `IIdempotencyKeyStore` + `IIdempotencyResponseStore` (`SharedKernel.Application.Behaviors`) — one class, `RedisIdempotencyKeyStore`.
- `IIdempotencyStore` (`SharedKernel.Messaging.Abstractions`) — `RedisIdempotencyMessageStore`.

This package ships no new interface — it implements two other domains' existing contracts. See the
root `CLAUDE.md` Folder Map entry for `18.Idempotency` for why this domain exists at all.

## Quick start

```csharp
services.AddRedisConnection("localhost:6379");   // 02.Caching.Redis.Core — shared IConnectionMultiplexer
services.AddSharedKernelRedisIdempotency(o =>
{
    o.InFlightTtl = TimeSpan.FromSeconds(30);
    o.RetentionWindow = TimeSpan.FromHours(24);
});

// Required: bridge this platform's tenant identity source. ITenantContextAccessor lives in
// 07.Messaging.Abstractions and is reused here rather than reinvented (Design D-02).
services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
```

```csharp
public sealed class MyTenantContextAccessor(ITenantProvider tenantProvider) : ITenantContextAccessor
{
    public Guid? TenantId => tenantProvider.TenantId;
}
```

Omitting the `ITenantContextAccessor` registration throws `InvalidOperationException` at
`IHost.StartAsync()` — not at first store call.

## Atomicity

`HasProcessedAsync` performs a single `SET key <sentinel> NX PX <InFlightTtl>` — success (key was
absent) means a fresh reservation was created (not yet processed, returns `false`); failure (key
already existed) means an entry is already in-flight or confirmed (returns `true`). No
`SELECT`/`EXISTS`-then-`SET` window ever exists.

`MarkProcessedAsync` extends the reservation's TTL to `RetentionWindow` via `PEXPIRE` — it never
rewrites the key's value, so it can never clobber a response `StoreResponseAsync` already wrote,
regardless of call order.

`StoreResponseAsync` uses a three-line Lua script (`if EXISTS then SET ... KEEPTTL`) — the only Lua
use in this package.

## Fault vs. failure

A thrown exception from the guarded call never reaches `MarkProcessedAsync` — the reservation's
short `InFlightTtl` expires on its own, and the key becomes retryable with no action from this
package (self-healing). A returned business failure **does** consume the key; see
`IIdempotencyKeyStore.MarkProcessedAsync`'s own XML docs for the full fault-vs-failure contract
this package honors but does not restate.

## Fail-closed by default

When the Redis connection is unreachable, every store call throws by default — the guarded command
or consumer is blocked rather than allowed to run unprotected. Set
`RedisIdempotencyOptions.AllowExecutionOnStoreUnavailable = true` to instead let the call proceed as
"not yet processed" during an outage.

> **ENABLING `AllowExecutionOnStoreUnavailable` INCREASES DUPLICATE-EXECUTION RISK.** While the
> store is unreachable, every call — including genuine duplicates — is treated as novel. Only
> enable this for operations where executing twice is safer than blocking entirely.

## Registration lifetime

Both store classes are registered `Scoped`, not singleton — `ITenantContextAccessor`
implementations are conventionally registered `Scoped` in this platform (mirroring
`SharedKernel.Messaging.MassTransit.MessagingBusBuilder.WithTenantContext<TAccessor>()`), and a
singleton service cannot safely consume a scoped dependency under a DI container built with
`ValidateScopes = true`. The shared `IConnectionMultiplexer` (registered by `02.Caching.Redis.Core`)
remains a singleton underneath — only the thin store wrapper is scoped.

## Verifying DI registration resolves

```csharp
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddRedisConnection(connectionString);
        services.AddSharedKernelRedisIdempotency();
        services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
    })
    .Build();

await host.StartAsync(); // throws InvalidOperationException here if ITenantContextAccessor is missing

using var scope = host.Services.CreateScope();
var keyStore = scope.ServiceProvider.GetRequiredService<IIdempotencyKeyStore>();
var responseStore = keyStore as IIdempotencyResponseStore; // non-null — same instance
var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
```
