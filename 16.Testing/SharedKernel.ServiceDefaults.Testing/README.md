# SharedKernel.ServiceDefaults.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Test helpers for the SharedKernel host packages: an in-memory `ITenantCatalog`, a configurable tenant-resolution
strategy, and assertions on health-check registrations.** Tenant-status and readiness wiring can be tested without a
tenant database or a running web host.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.ServiceDefaults`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `InMemoryTenantCatalog` | `ITenantCatalog` (`SharedKernel.MultiTenancy`) | `SeedTenant(descriptor, resolutionKey?)`, `MutateStatus(tenantId, status)` to suspend or offboard a tenant mid-test, `Reset()`. `GetByIdAsync(TenantId)` and `GetByResolutionKeyAsync(key)` return `null` for an unknown tenant and never throw |
| `FakeTenantResolutionStrategy` | a tenant-resolution strategy | Returns a fixed tenant or runs a delegate over the `HttpContext`; `StrategyName` is settable. It returns `Guid?`, not the `TenantId?` of `ITenantResolutionStrategy`, so it does not implement that interface |
| `HealthCheckAssertionExtensions` | — | `registration.ShouldBeTaggedReady()` and `registration.ShouldNotBeTaggedLive()` on a `HealthCheckRegistration`, without booting a web host |

## Registration

There are no `Add*` helpers; replace the real registration:

```csharp
var catalog = new InMemoryTenantCatalog();
services.AddSingleton<ITenantCatalog>(catalog);
```

## Example

```csharp
var catalog = new InMemoryTenantCatalog();
catalog.SeedTenant(descriptor);                              // an Active tenant
catalog.MutateStatus(descriptor.TenantId, TenantStatus.Suspended);

var validator = new CatalogTenantStatusValidator(catalog);   // the code under test
(await validator.IsActiveAsync(descriptor.TenantId, ct)).Should().BeFalse();
```

```csharp
services.AddReadinessProbe<OrderStoreProbe>();              // an IReadinessProbe named "order-store"
services.AddHealthChecks().AddSharedKernelReadiness();
var provider = services.BuildServiceProvider();
var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

var check = registrations.Single(r => r.Name == "order-store");
check.ShouldBeTaggedReady();
check.ShouldNotBeTaggedLive();
```

## Related packages

- References `SharedKernel.MultiTenancy` (and through it ASP.NET Core) and
  `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `TestRequestContext` for code that reads the
  resolved tenant from `IRequestContext`.
