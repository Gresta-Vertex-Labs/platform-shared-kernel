# SharedKernel.Idempotency.Redis

Atomic, tenant-scoped, Redis-backed implementation of the platform's two idempotency contracts:

- `IRequestIdempotencyStore` (`SharedKernel.Application.Behaviors`) — `RedisRequestIdempotencyStore`.
- `IIdempotencyStore` (`SharedKernel.Messaging.Abstractions`) — `RedisIdempotencyMessageStore`.

This package ships no new interface — it implements two other domains' existing contracts. See the
root `CLAUDE.md` Folder Map entry for `18.Idempotency` for why this domain exists at all.

## Quick start

```csharp
services.AddRedisConnection(configuration);   // 02.Caching.Redis.Core — shared connection, bound from SharedKernel:Caching:Redis
// or in code: services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
services.AddSharedKernelRedisIdempotency(o =>
{
    o.InFlightTtl = TimeSpan.FromSeconds(30);
    o.RetentionWindow = TimeSpan.FromHours(24);
});

// Required: bridge this platform's tenant identity source. ITenantContextAccessor lives in
// 07.Messaging.Abstractions and is reused here rather than reinvented.
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

## Contract

`IRequestIdempotencyStore.TryBeginAsync(key, requestFingerprint, ct)` atomically reserves a new key
and records the caller's request fingerprint, or reports the key's existing state:

| Existing state | Same fingerprint | Different fingerprint |
|---|---|---|
| No entry | `Started` — a new reservation was created | (not applicable) |
| Reserved, not completed | `InProgress` | `FingerprintMismatch` |
| Completed | `Completed`, with the stored response | `FingerprintMismatch` |

A winning `Started` result carries a `ReservationToken` — an opaque string the caller must pass back
to `CompleteAsync`/`ReleaseAsync`. `CompleteAsync(key, reservationToken, serializedResponse, ct)`
marks the key completed, stores the response, and extends the entry's TTL to `RetentionWindow`, but
only when `reservationToken` still owns the row **and** it is still `InProgress` — otherwise it
returns `false` and touches nothing. `ReleaseAsync(key, reservationToken, ct)` deletes the
reservation under the same guard — **only** while it is still `InProgress`; a completed entry is
never deleted by `ReleaseAsync`. Both return `true` only when the token still owned the reservation
and the operation actually applied; `false` — never an exception — means the reservation was already
lost: expired and reclaimed by someone else, already completed or released, or a foreign token. A
reservation that is never completed or released self-expires once `InFlightTtl` elapses, so a
crashed caller can never permanently wedge a key.

## Storage shape and atomicity

Each entry is a single Redis hash (`status`, `fingerprint`, `token`, and — once completed —
`response`), keyed as `sk:idempotency:{tenant}:key:{key}`, where `{key}` is the key passed to
`TryBeginAsync` exactly as given. Through `IdempotencyBehavior` that is never the command's raw key but a
SHA-256 digest of tenant, caller and key (64 lowercase hex characters), so a reservation belongs to one
caller of one tenant and another caller using the same key cannot be handed its stored response. The
`{tenant}` segment from `ITenantContextAccessor` stays on top of that. `TryBeginAsync`, `CompleteAsync` and
`ReleaseAsync` are each a single Lua script — one atomic Redis round trip, comparing fingerprint/
token/status and mutating the hash in the same call. No `WATCH`/`MULTI` retry loop and no
check-then-act window ever exists.

**Reservation token — this store keeps none of its own.** Every winning `TryBeginAsync` call
generates a fresh token, writes it into the hash, and returns it as `ReservationToken`. The caller —
not this store — is responsible for passing that exact token back to `CompleteAsync`/`ReleaseAsync`.
Those calls mutate the row only when the supplied token still matches what is stored there and the
row is still `InProgress`; this makes the store itself genuinely stateless (no per-instance "who won
which reservation" tracking) and turns a slow caller's late confirm/release — arriving after its own
reservation already expired and a different caller has since re-reserved the same key — into a safe,
detectable `false` instead of corrupting that other caller's entry.

## Fault vs. failure

A thrown exception from the guarded call never reaches `CompleteAsync` — the reservation's short
`InFlightTtl` expires on its own, and the key becomes retryable with no action from this package
(self-healing). A returned business failure calls `ReleaseAsync` instead, which also frees the key
immediately. See `IRequestIdempotencyStore`'s own XML docs for the full contract this package
honors.

## Fail-closed by default

When the Redis connection is unreachable, every store call throws by default — the guarded command
is blocked rather than allowed to run unprotected. Set
`RedisIdempotencyOptions.AllowExecutionOnStoreUnavailable = true` to instead let `TryBeginAsync`
proceed as `Started` during an outage.

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
        services.AddRedisConnection(o => o.ConnectionString = connectionString);
        services.AddSharedKernelRedisIdempotency();
        services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
    })
    .Build();

await host.StartAsync(); // throws InvalidOperationException here if ITenantContextAccessor is missing

using var scope = host.Services.CreateScope();
var requestStore = scope.ServiceProvider.GetRequiredService<IRequestIdempotencyStore>();
var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
```
