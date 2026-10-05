# SharedKernel.ServiceDefaults.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Test helpers for the SharedKernel host packages: an in-memory `ITenantCatalog`, a scriptable
> `ITenantResolutionStrategy`, and assertions on health-check registrations.** Tenant-status, tenant-resolution and
> readiness wiring are tested without a tenant database or a running web host.

| You get | So that |
| --- | --- |
| `InMemoryTenantCatalog` (`ITenantCatalog`) | `CatalogTenantStatusValidator`, `CachedTenantCatalog` and your own catalog readers run against seeded tenants |
| `MutateStatus(tenantId, status)` | A tenant is suspended or offboarded mid-test |
| `FakeTenantResolutionStrategy` (`ITenantResolutionStrategy`) | `TenantResolutionMiddleware` resolves the tenant you choose — fixed or computed from the `HttpContext` |
| `ShouldBeTaggedReady()` / `ShouldNotBeTaggedLive()` | A probe's placement on `/health/ready` vs `/health/live` is asserted from the registration alone |

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only** — the `TestingNeverReferencedByProduction` architecture rule fails any
production project that references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.MultiTenancy` (brings the `Microsoft.AspNetCore.App` framework), `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` |
| Namespaces | `SharedKernel.Testing.ServiceDefaults` |

## Quick start

```csharp
using Microsoft.AspNetCore.Http;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Testing.ServiceDefaults;
using Xunit;

public sealed class TenantStatusTests
{
    private static readonly TenantId Acme = new(Guid.Parse("7d1f4c7e-5a55-4f59-9d8a-1d2f0f3a9b10"));

    [Fact]
    public async Task A_suspended_tenant_is_no_longer_active()
    {
        var catalog = new InMemoryTenantCatalog();
        catalog.SeedTenant(
            new TenantDescriptor(Acme, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, "en-US",
                new Dictionary<string, string>()),
            resolutionKey: "acme");
        var validator = new CatalogTenantStatusValidator(catalog);

        Assert.True(await validator.IsActiveAsync(Acme, CancellationToken.None));

        catalog.MutateStatus(Acme, TenantStatus.Suspended);

        Assert.False(await validator.IsActiveAsync(Acme, CancellationToken.None));
        Assert.Equal(Acme, (await catalog.GetByResolutionKeyAsync("acme", CancellationToken.None))!.TenantId);
    }

    [Fact]
    public async Task The_fake_strategy_resolves_from_the_request()
    {
        var strategy = new FakeTenantResolutionStrategy((http, _) =>
            Task.FromResult<TenantId?>(http.Request.Host.Host == "acme.example.com" ? Acme : null));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.example.com");

        Assert.Equal(Acme, await strategy.TryResolveAsync(context, CancellationToken.None));
    }
}
```

## How it works

- **Catalog.** Descriptors are stored by `TenantId`; a `resolutionKey` passed to `SeedTenant` is an extra lookup key
  for `GetByResolutionKeyAsync`. Both lookups return `null` for an unknown tenant or key and never throw, like the
  real catalogs. Seeding the same tenant again replaces its descriptor. `MutateStatus` replaces the stored record
  with the new `Status` (`with` copy); for an unseeded tenant it does nothing.
- **Strategy.** `new FakeTenantResolutionStrategy()` resolves no tenant; `new FakeTenantResolutionStrategy(tenantId)`
  always resolves that one; the delegate overload computes it from the `HttpContext`. `StrategyName` defaults to
  `"Fake"` and is settable.
- **Health assertions** read a `HealthCheckRegistration`'s tags: `ShouldBeTaggedReady()` requires `"ready"`,
  `ShouldNotBeTaggedLive()` forbids `"live"` — the tags `/health/ready` and `/health/live` filter on.
- **Thread-safe.** The catalog uses concurrent dictionaries; the strategy and assertions are stateless.
- **No DI helpers.** Construct the doubles and register them yourself (see Recipes).

## Recipes

### 1. Replace the tenant catalog in a test host

```csharp
var catalog = new InMemoryTenantCatalog();
services.AddSingleton<ITenantCatalog>(catalog);   // after the production registration, so it wins
```

### 2. Drive `TenantResolutionMiddleware` with the fake strategy

The middleware looks strategies up by `StrategyName` and runs the names in `TenantResolutionOptions.StrategyOrder`,
so the fake's name must be listed there and must not collide with a registered strategy.

```csharp
services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = ["Fake"]);
services.AddSingleton<ITenantResolutionStrategy>(new FakeTenantResolutionStrategy(tenantId));
```

### 3. Assert a readiness probe is on `/health/ready` only

```csharp
services.AddReadinessProbe<OrderStoreProbe>();          // IReadinessProbe with Name "order-store"
services.AddHealthChecks().AddSharedKernelReadiness();

var registration = services.BuildServiceProvider()
    .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
    .Single(r => r.Name == "order-store");

registration.ShouldBeTaggedReady();
registration.ShouldNotBeTaggedLive();
```

`AddReadinessProbe` is in `SharedKernel.Primitives.Health`, `AddSharedKernelReadiness` in
`SharedKernel.ServiceDefaults.HealthChecks`.

## Reference

### `InMemoryTenantCatalog : ITenantCatalog`

| Member | Purpose |
| --- | --- |
| `SeedTenant(TenantDescriptor descriptor, string? resolutionKey = null)` | Add or replace a tenant, optionally with a resolution key |
| `MutateStatus(TenantId tenantId, TenantStatus status)` | Change a seeded tenant's status |
| `GetByIdAsync(TenantId tenantId, CancellationToken ct)` | The production lookup; `null` when unknown |
| `GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct)` | The production lookup; `null` when unknown |
| `Reset()` | Remove every tenant and key |

### `FakeTenantResolutionStrategy : ITenantResolutionStrategy`

| Member | Purpose |
| --- | --- |
| `FakeTenantResolutionStrategy(TenantId? fixedResult = null)` | Always returns `fixedResult` |
| `FakeTenantResolutionStrategy(Func<HttpContext, CancellationToken, Task<TenantId?>> resolver)` | Computes the tenant per request |
| `StrategyName` (get/set, default `"Fake"`) | The name `StrategyOrder` refers to |
| `TryResolveAsync(HttpContext context, CancellationToken cancellationToken)` | The production contract |

### `HealthCheckAssertionExtensions`

| Method | Throws `InvalidOperationException` when |
| --- | --- |
| `ShouldBeTaggedReady(this HealthCheckRegistration registration)` | The registration lacks the `ready` tag (the message lists its tags) |
| `ShouldNotBeTaggedLive(this HealthCheckRegistration registration)` | The registration carries the `live` tag |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.ServiceDefaults.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.ServiceDefaults.Testing/SharedKernel.ServiceDefaults.Testing.Tests),
proving each helper against the `ITenantCatalog` and `ITenantResolutionStrategy` contracts. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
— `TestRequestContext` for code that reads the resolved tenant from `IRequestContext`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Register the fake strategy without listing its name in `StrategyOrder` | Set `StrategyOrder` to include `StrategyName` | The middleware only runs strategies named in the order |
| Rename the fake to `"Header"` while the real header strategy is registered | Keep a unique name, or remove the real strategy | Strategies are keyed by name; a duplicate name fails the middleware |
| Expect `MutateStatus` to create a tenant | `SeedTenant` first | An unseeded tenant is ignored silently |
| Use `default(TenantId)` as a seeded or fixed tenant | Construct `TenantId` from a non-empty `Guid` | `TenantId` rejects `Guid.Empty` |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
