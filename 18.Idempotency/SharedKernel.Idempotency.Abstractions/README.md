# SharedKernel.Idempotency.Abstractions

One reservation contract for every idempotency guard in a service: the application pipeline's command
idempotency (`IdempotencyBehavior`) and MassTransit's consumer idempotency (`WithIdempotency()`) both call the
same `IIdempotencyStore`. No Redis, EF Core or messaging dependency — it references only
`SharedKernel.Execution` and `Microsoft.Extensions.DependencyInjection.Abstractions`.

Implementations: `SharedKernel.Idempotency.Redis` (atomic Lua scripts) and `SharedKernel.Idempotency.EfCore`
(atomic `INSERT … ON CONFLICT` on PostgreSQL). Test double: `16.Testing`'s `FakeIdempotencyStore`.

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
