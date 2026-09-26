# SharedKernel.Idempotency.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`FakeIdempotencyStore` implements `SharedKernel.Idempotency.Abstractions`' `IIdempotencyStore` in memory, with the
same reservation protocol as the Redis and EF Core stores.** It serves both purposes — `IdempotencyPurpose.Request`
(the pipeline's `IdempotencyBehavior`) and `IdempotencyPurpose.Message` (MassTransit consumer idempotency) — so a
duplicate command or redelivered message can be tested without Redis or PostgreSQL.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Idempotency.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Idempotency`.

## Behaviour

Entries are keyed by tenant (read from the ambient request context), purpose and key, and every decision is made
under one lock, so two concurrent duplicates never both start:

| State of the key | `TryBeginAsync` returns |
| --- | --- |
| No entry | `Started`, with a fresh token |
| Entry with a different fingerprint | `FingerprintMismatch` |
| In-flight entry | `InProgress` |
| Completed entry | `Completed`, with the stored response |

`CompleteAsync` and `ReleaseAsync` act only for the token that owns the in-flight entry and return `false`
otherwise, never throwing; a release removes the entry so the key starts fresh. Time is not simulated — call
`Expire(purpose, key)` to model a TTL running out. `Calls` records every call (`Member`, `Purpose`, `Key`), and
`LastTtl` / `LastRetention` the durations the caller passed. `Reset()` clears everything.

## Registration

```csharp
services.AddFakeIdempotencyStore();                               // both purposes
services.AddFakeIdempotencyStore(IdempotencyPurpose.Request);     // only the pipeline's store
```

Registers one singleton `FakeIdempotencyStore` as the keyed `IIdempotencyStore` for each purpose, **replacing** any
store already registered for it. Resolve `FakeIdempotencyStore` itself to assert.

## Example

```csharp
services.AddFakeIdempotencyStore(IdempotencyPurpose.Request);
// ... AddSharedKernelApplication(assembly, app => app.UseMediatR().WithIdempotency()), then send the same command twice ...

var store = provider.GetRequiredService<FakeIdempotencyStore>();
store.Calls.Should().Contain(c => c.Member == "CompleteAsync");  // under the caller-scoped key: a 64-char digest, not the raw key
secondResult.Should().BeEquivalentTo(firstResult);                // replayed, handler ran once
```

## Related packages

- References `SharedKernel.Idempotency.Abstractions` only.
- [`SharedKernel.Application.Testing`](../SharedKernel.Application.Testing/README.md) — its
  `AddFakeApplicationBehaviorServices()` registers this store for `IdempotencyPurpose.Request`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `TestRequestContext` to set the tenant the store keys by.
