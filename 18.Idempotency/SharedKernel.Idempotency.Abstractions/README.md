# SharedKernel.Idempotency.Abstractions

One reservation contract for every idempotency guard in a service: the application pipeline's command
idempotency (`IdempotencyBehavior`) and MassTransit's consumer idempotency (`WithIdempotency()`) both call the
same `IIdempotencyStore`. No Redis, EF Core or messaging dependency — it references only
`SharedKernel.Execution` and `Microsoft.Extensions.DependencyInjection.Abstractions`.

Implementations: [`SharedKernel.Idempotency.Redis`](../SharedKernel.Idempotency.Redis/README.md) (atomic Lua
scripts) and [`SharedKernel.Idempotency.EfCore`](../SharedKernel.Idempotency.EfCore/README.md) (atomic
`INSERT … ON CONFLICT` on PostgreSQL). Test double: `16.Testing`'s `SharedKernel.Idempotency.Testing`
(`FakeIdempotencyStore`).

**Tier:** Abstractions. It replaces the two earlier contracts — `05.Application`'s `IRequestIdempotencyStore`
(with `IdempotencyBeginResult`/`IdempotencyBeginStatus`) and `SharedKernel.Messaging.Abstractions`' own
`IIdempotencyStore` — with one purpose-keyed interface (WO-086, P-568).

## Install

Most services never reference this package directly: it arrives with `SharedKernel.Application.Pipeline`,
`SharedKernel.Messaging.MassTransit` and both providers. Reference it yourself only to write a custom store or to
use the contract from your own code:

```xml
<PackageReference Include="SharedKernel.Idempotency.Abstractions" />
```

Versions come from your single `SharedKernelVersion` property (the repository's `PLATFORM.md`, "Consuming the
kernel").

## The contract

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyReservation> TryBeginAsync(IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken ct);
    Task<bool> CompleteAsync(IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken ct);
    Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken ct);
}

public readonly record struct IdempotencyReservation(IdempotencyReservationStatus Status, string? StoredResponse, string? Token);
```

| Existing entry for (tenant, purpose, key) | Same fingerprint | Different fingerprint |
| --- | --- | --- |
| None, or expired | `Started` — the caller holds the reservation and gets a `Token` | — |
| In flight | `InProgress` | `FingerprintMismatch` |
| Completed | `Completed`, with the stored response (none for a message) | `FingerprintMismatch` |

- `TryBeginAsync` is one atomic compare-and-set. A read followed by a write is not a valid implementation.
- `ttl` is the in-flight lease: a reservation never completed or released expires after it, so a crashed caller
  cannot wedge a key. It must outlast the guarded work.
- `CompleteAsync` stores the response for `retention`; `ReleaseAsync` frees the key at once. Both act only while
  the token still owns an **in-flight** reservation, and report `false` otherwise — a late caller whose reservation
  expired and was taken over cannot touch the new owner's, and a release never removes a completed reservation.

## How the two callers use it

| | `IdempotencyPurpose.Request` (`IdempotencyBehavior`) | `IdempotencyPurpose.Message` (MassTransit) |
| --- | --- | --- |
| Key | `IIdempotentRequest.IdempotencyKey` | `ConsumeContext.MessageId`, "D" form |
| Fingerprint | `IIdempotentRequest.Fingerprint`, or a SHA-256 of the serialized command | the fixed value `"message"` |
| `ttl` / `retention` | `IdempotencyBehaviorOptions.LeaseDuration` / `.RetentionWindow` | `IdempotencyOptions.LeaseDuration` / `.ExpiryWindow` |
| `Started` | runs the handler; completes with the serialized `Result` on success, releases on failure or exception | runs the consumer; completes (no response) on success, releases when it throws |
| `InProgress` | `Error.Conflict("idempotency.in_progress")` | throws `ConcurrentMessageDeliveryException` — the message stays unacknowledged and is redelivered |
| `Completed` | replays the stored response | acknowledges the duplicate without consuming |
| `FingerprintMismatch` | `Error.Conflict("idempotency.key_reused")` | a store defect (the fingerprint is fixed): throws |

A message whose consumer fails is released, so its redelivery is consumed rather than dropped as a duplicate.

## Registration: keyed by purpose

A store is registered once per purpose, so each purpose can use a different backend:

```csharp
services.AddRedisConnection(configuration);
services.AddRedisIdempotency(p => p.ForRequests());                          // requests in Redis
services.AddEfCoreIdempotency(o => o.UsePostgres(dataSource), p => p.ForMessages()); // messages next to the outbox
```

A custom store registers the same way — `services.AddIdempotencyStore<MyStore>(IdempotencyPurpose.Message)` —
and is resolved with `[FromKeyedServices(IdempotencyPurpose.Request)] IIdempotencyStore` or
`provider.GetRequiredIdempotencyStore(purpose)`. A second registration for the same purpose throws: it would
silently leave the first unused. `ApplicationBehaviorsBuilder.Build()` and `MessagingBusBuilder.Build()` fail at
startup when the purpose they need has no store (`HasIdempotencyStore(purpose)`).

## Tenant scope

Stores scope every key by the tenant of the ambient request context (`IRequestContextAccessor`), which the
service's inbound adapters set: `UseSharedKernelRequestContext()` for HTTP, `WithInboundRequestContext()` for
messages, the job runner for scheduled work. `IdempotencyTenantScope` is the one encoding every store uses:

| Context | Scope value |
| --- | --- |
| A tenant | the `TenantId` in "D" form (36 characters) |
| No context, or a context without a tenant | `no-tenant` — never a GUID, so it can never collide with a tenant |

A multi-tenant service must establish the request context before idempotency runs; otherwise every tenant
shares the `no-tenant` scope.

## Store outage

An unreachable store throws (fail closed). Each provider has one explicit opt-out,
`AllowExecutionOnStoreUnavailable`, which lets the guarded work run as `Started` during an outage — at the cost
of possible duplicates.

## Writing a custom store

A third backend implements the three members with the same semantics and registers itself per purpose:

```csharp
public sealed class DynamoIdempotencyStore(IAmazonDynamoDB dynamo, IRequestContextAccessor context) : IIdempotencyStore
{
    public async Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken ct)
    {
        var scope = IdempotencyTenantScope.Current(context);   // "D" tenant id, or "no-tenant"
        // ONE conditional write keyed by (scope, purpose, key) that returns the existing item on conflict.
        // Map it to IdempotencyReservation.Started(token) / InProgress() / Completed(response) / FingerprintMismatch().
        ...
    }

    // CompleteAsync / ReleaseAsync: mutate only while `token` owns an in-flight entry; otherwise return false.
    ...
}

services.AddIdempotencyStore<DynamoIdempotencyStore>(p => p.ForRequests().ForMessages());   // scoped by default
```

- `TryBeginAsync` must be a single conditional write, never a read followed by a write.
- Never throw from `CompleteAsync`/`ReleaseAsync` for a lost or foreign token — return `false`.
- Store the response string exactly as given; never parse it.
- Throw on an unreachable backend unless the store offers an explicit, documented fail-open switch.

## Known limitation: consumer keys are message ids only

MassTransit's consumer idempotency uses `ConsumeContext.MessageId` as the key with a fixed fingerprint, and the
store scopes it only by tenant. Two receive endpoints (or two polymorphic consumers) in **one** service that both
receive the same message therefore share one reservation: the second is acknowledged as a duplicate and skipped.
Until the key includes the consumer/endpoint, enable `WithIdempotency()` only where each message is consumed by one
consumer per service.

## Testing

`SharedKernel.Idempotency.Testing` (`16.Testing`) ships `FakeIdempotencyStore`, an in-memory store that implements
the same reservation protocol — the four statuses and the stale-token rule, with `Expire(purpose, key)` to model a lease running out — registered with
`services.AddFakeIdempotencyStore(purposes)`. Atomicity itself is only proved against the real providers (their
Integration-lane tests use Testcontainers).

## Related packages

| Package | Role |
| --- | --- |
| [`SharedKernel.Idempotency.Redis`](../SharedKernel.Idempotency.Redis/README.md) | Redis store — `AddRedisIdempotency(p => …)` |
| [`SharedKernel.Idempotency.EfCore`](../SharedKernel.Idempotency.EfCore/README.md) | PostgreSQL store — `AddEfCoreIdempotency(db => …, p => …)` |
| `SharedKernel.Application.Pipeline` (`05.Application`) | `IdempotencyBehavior`, the `Request` caller |
| `SharedKernel.Messaging.MassTransit` (`07.Messaging`) | `WithIdempotency()`, the `Message` caller |
| `SharedKernel.Execution` (`01.Core`) | `IRequestContextAccessor`, `TenantId` |
