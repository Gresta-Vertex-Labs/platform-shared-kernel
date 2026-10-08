# SharedKernel.ServiceDefaults.Localization

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **Decides which culture a request gets — the user's stored preference, then the tenant's default, then
> `Accept-Language` — on top of ASP.NET Core's own `RequestLocalizationMiddleware`. It translates nothing; pair it with
> `SharedKernel.Localization` for that.**

| You get | So that |
| --- | --- |
| `builder.AddSharedKernelLocalization()` | One call sets ASP.NET Core's request-culture providers in a fixed, reviewed order |
| A user-preference step from a claim | A signed-in user's chosen language wins over the browser's |
| A tenant-default step from `ITenantCatalog` | A tenant's configured culture applies to all its users without a preference |
| `Accept-Language` last | Anonymous and first-time callers still get a sensible culture |
| A startup warning when nothing dynamic can resolve | A misconfiguration that silently falls back to `Accept-Language` is visible |

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Localization" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy` (for `ITenantCatalog`), `SharedKernel.Security.Abstractions` |
| Namespaces | `SharedKernel.ServiceDefaults.Localization` |

## Quick start

```csharp
using Microsoft.AspNetCore.Builder;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Localization;
using SharedKernel.ServiceDefaults.Security;

builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "preferred_culture");

builder.Services.Configure<RequestLocalizationOptions>(o => o
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "de", "tr")
    .AddSupportedUICultures("en", "de", "tr"));

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a =>
{
    a.UseMiddleware<TenantResolutionMiddleware>();   // sets the request context's tenant
    a.UseRequestLocalization();                      // the BCL middleware; this package never wires it for you
}));
```

Placing both calls in `BeforeAuthorization` means the 401, 403 and 429 problems `UseSharedKernelWebApi()` writes are
localized too. Without `SharedKernel.Presentation.WebApi`: `UseAuthentication()`,
`UseMiddleware<TenantResolutionMiddleware>()`, `UseRequestLocalization()`, `UseAuthorization()`.

## How it works

Signed signals come before an unsigned header — the same rule as `SharedKernel.MultiTenancy`'s strategy order:

1. **`UserPreference`** — `IUserContext.FindClaim(UserPreferenceClaimType)`. Skipped when the claim type is not
   configured or the claim is absent.
2. **`TenantDefault`** — `TenantDescriptor.DefaultCulture` of the current tenant, through an `ITenantCatalog` if one
   is registered. The tenant is `IRequestContext.TenantId` (`RequestContextScope.Current`, else the registered
   `IRequestContext`). Skipped, never throwing, when there is no tenant, no catalog or no default culture.
3. **`AcceptLanguageHeader`** — ASP.NET Core's `AcceptLanguageHeaderRequestCultureProvider`.

The first provider that returns a culture ASP.NET Core accepts (one of the supported cultures) wins; otherwise the
default culture applies.

## Configuration

Set in code on `AddSharedKernelLocalization(o => …)` (`LocalizationResolutionOptions`); nothing is bound from
configuration.

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `UserPreferenceClaimType` | `string?` | `null` | The claim holding the user's culture; `null` disables the `UserPreference` step |
| `StrategyOrder` | `IReadOnlyList<LocalizationResolutionStrategy>` | empty → `DefaultStrategyOrder` (`UserPreference`, `TenantDefault`, `AcceptLanguageHeader`) | The steps to run, in order; a non-empty list replaces the default |

Supported cultures and the default culture are ASP.NET Core's `RequestLocalizationOptions`, configured as usual.

## Reference

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelLocalization(Action<LocalizationResolutionOptions>?)` | Sets `RequestLocalizationOptions.RequestCultureProviders` to the configured steps |
| `LocalizationResolutionOptions` | `UserPreferenceClaimType`, `StrategyOrder`, `DefaultStrategyOrder` |
| `LocalizationResolutionStrategy` | `UserPreference`, `TenantDefault`, `AcceptLanguageHeader` |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13004 | Warning | Neither `UserPreferenceClaimType` nor a registered `ITenantCatalog` is configured — the `UserPreference` and `TenantDefault` steps can never resolve a culture |

## Testing

- Set the user's claim with [`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Security/SharedKernel.Security.Testing/README.md)'s
  `FakeUserContext` (`Claims = [new("preferred_culture", "de")]`).
- Give a tenant a default culture with [`SharedKernel.ServiceDefaults.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing/README.md)'s
  `InMemoryTenantCatalog.SeedTenant(descriptor)` and a `FakeTenantResolutionStrategy`.
- In a `WebApplicationFactory<Program>` test, assert `CultureInfo.CurrentUICulture` in an endpoint or the language of a
  localized problem response.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Forget `app.UseRequestLocalization()` | Call it after `TenantResolutionMiddleware` | This package only configures the providers; the BCL middleware applies them |
| Leave `SupportedCultures` at the default | Add every culture you translate | ASP.NET Core ignores a resolved culture that is not supported |
| Run `UseRequestLocalization()` before tenant resolution | Place it after `TenantResolutionMiddleware` | The `TenantDefault` step reads the tenant from the request context |
| Configure neither a claim type nor a catalog | Set `UserPreferenceClaimType` or register `ITenantCatalog` | Culture would come from `Accept-Language` alone (EventId 13004 warns) |
| Put `AcceptLanguageHeader` first in `StrategyOrder` | Keep signed signals first | A header is caller-controlled and would override the user's preference |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
