# SharedKernel.Idempotency.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **`FakeIdempotencyStore` is an in-memory `IIdempotencyStore` with the same reservation protocol as the Redis and
> EF Core stores, so a duplicate command or a redelivered message is tested without Redis or PostgreSQL.**

| You get | So that |
| --- | --- |
| The real `Started` / `InProgress` / `Completed` / `FingerprintMismatch` protocol under one lock | Two concurrent duplicates never both start, exactly as in production |
| Keys scoped by tenant from the request context | Tenant separation is proved in a unit test |
| Token-conditional `CompleteAsync` / `ReleaseAsync` | A stale or foreign token is refused (`false`), never thrown |
| `Expire(purpose, key)` | A TTL running out is modelled without a clock |
| `Calls`, `LastTtl`, `LastRetention` | You assert what the pipeline or consumer asked of the store |
| `AddFakeIdempotencyStore(purposes)` | One call replaces the keyed store for `Request`, `Message` or both |

## Install

```xml
<PackageReference Include="SharedKernel.Idempotency.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. Production code must never reference a Testing package;
`TestingNeverReferencedByProduction` fails the build's architecture tests when it does.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Idempotency.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Idempotency` |

## Quick start

```csharp
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Testing.Idempotency;
using Xunit;

public sealed class DuplicateSubmissionTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_completed_key_replays_its_stored_response()
    {
        var store = new FakeIdempotencyStore();

        var first = await store.TryBeginAsync(IdempotencyPurpose.Request, "order-42", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Started, first.Status);

        Assert.True(await store.CompleteAsync(
            IdempotencyPurpose.Request, "order-42", first.Token!, "{\"id\":42}", TimeSpan.FromHours(24), CancellationToken.None));

        var second = await store.TryBeginAsync(IdempotencyPurpose.Request, "order-42", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Completed, second.Status);
        Assert.Equal("{\"id\":42}", second.StoredResponse);
    }
}
```

In a real host, or behind the application pipeline's `WithIdempotency()`:

```csharp
services.AddFakeIdempotencyStore();                             // both purposes
services.AddFakeIdempotencyStore(IdempotencyPurpose.Request);   // only the pipeline's store
```

## How it works

| State of the key (tenant scope, purpose, key) | `TryBeginAsync` returns |
| --- | --- |
| No entry | `Started`, with a fresh token |
| Entry with a different fingerprint (in flight or completed) | `FingerprintMismatch` |
| In-flight entry, same fingerprint | `InProgress` |
| Completed entry, same fingerprint | `Completed`, with the stored response |

- `CompleteAsync` and `ReleaseAsync` act only for the token that owns an **in-flight** entry and return `false`
  otherwise (unknown key, other token, already completed). A release removes the entry, so the key starts fresh.
- **Tenant scope** comes from `IdempotencyTenantScope.Current(accessor)` — the ambient `RequestContextScope` by
  default, or the `IRequestContextAccessor` you pass (DI uses a registered one). No tenant means the `"no-tenant"`
  scope.
- **Keys are stored exactly as given.** Through `IdempotencyBehavior` the store receives the command's key already
  scoped to the tenant and caller — a 64-character lowercase hex digest, the same key the real stores receive — so
  `Calls` records that digest, not the raw `IIdempotentRequest.IdempotencyKey`.
- **Simplified:** time is not simulated. `ttl` and `retention` are validated (positive) and recorded in `LastTtl` /
  `LastRetention`, but no entry expires on its own — call `Expire`. The cancellation token is not observed.
- **Lifetimes and threading:** `AddFakeIdempotencyStore` registers one singleton shared by every purpose it covers;
  the store is thread-safe.

## Recipes

### 1. Prove tenants do not share keys

```csharp
var store = new FakeIdempotencyStore();

using (RequestContextScope.Begin(new SystemRequestContext([], tenantId: new TenantId(Guid.NewGuid()))))
    Assert.Equal(IdempotencyReservationStatus.Started,
        (await store.TryBeginAsync(IdempotencyPurpose.Message, "msg-1", "fp", Ttl, CancellationToken.None)).Status);

using (RequestContextScope.Begin(new SystemRequestContext([], tenantId: new TenantId(Guid.NewGuid()))))
    Assert.Equal(IdempotencyReservationStatus.Started,
        (await store.TryBeginAsync(IdempotencyPurpose.Message, "msg-1", "fp", Ttl, CancellationToken.None)).Status);
```

`RequestContextScope` and `SystemRequestContext` are in `SharedKernel.Execution.Context`, `TenantId` in
`SharedKernel.Execution.Tenancy`.

### 2. Model an expired reservation

```csharp
var begin = await store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None);
store.Expire(IdempotencyPurpose.Request, "k");

Assert.False(await store.ReleaseAsync(IdempotencyPurpose.Request, "k", begin.Token!, CancellationToken.None));
```

### 3. Assert what the pipeline did

```csharp
var store = provider.GetRequiredService<FakeIdempotencyStore>();

Assert.Contains(store.Calls, c => c.Member == "CompleteAsync" && c.Purpose == IdempotencyPurpose.Request);
```

`.WithIdempotency()` also needs a registered `IRequestContext`.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddFakeIdempotencyStore(this IServiceCollection, params IdempotencyPurpose[] purposes)` | One `FakeIdempotencyStore` singleton (as itself, `TryAdd`), and for each purpose — both when none is given — the keyed `IIdempotencyStore`, **removing** any store already registered for that purpose |

### `FakeIdempotencyStore` (implements `IIdempotencyStore`)

| Member | What it does |
| --- | --- |
| `FakeIdempotencyStore()` | Scopes keys by the ambient request context |
| `FakeIdempotencyStore(IRequestContextAccessor requestContextAccessor)` | Scopes keys by the tenant this accessor reports |
| `Calls` | Every call as `RecordedCall(string Member, IdempotencyPurpose Purpose, string Key)`, in order |
| `LastTtl` / `LastRetention` | The last `ttl` passed to `TryBeginAsync` / `retention` passed to `CompleteAsync` |
| `Expire(IdempotencyPurpose purpose, string key)` | Drops the entry in the current tenant scope; `true` if one existed |
| `Reset()` | Clears every reservation, the call list and the recorded durations |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Idempotency.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Idempotency.Testing/SharedKernel.Idempotency.Testing.Tests)
and prove the fake against the `IIdempotencyStore` protocol, including tenant scoping. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
(`TestRequestContext` sets the tenant the store keys by) and
[`SharedKernel.Application.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Application.Testing/README.md),
whose `AddFakeApplicationBehaviorServices()` registers this store for `IdempotencyPurpose.Request`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Use `SharedKernel.Idempotency.Redis` or `.EfCore` | The architecture tests fail a production reference, and nothing survives a restart |
| Assert `Calls` against the command's raw idempotency key | Assert on `Member`/`Purpose`, or compute the scoped key | The pipeline passes a tenant- and caller-scoped digest |
| Wait for a TTL to lapse | Call `Expire(purpose, key)` | The fake never expires entries on its own |
| Expect `ReleaseAsync` to undo a completed entry | Use `Expire` or `Reset()` | Only an in-flight entry can be released |
| Reserve and assert under different tenant scopes by accident | Open one `RequestContextScope` around both, or none | The same key in another tenant is a different entry |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
