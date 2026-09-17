# SharedKernel.ServiceDefaults.Localization

Precedence-ordered request-culture resolution, composed on top of ASP.NET Core's own
`RequestLocalizationMiddleware`. One of the `SharedKernel.ServiceDefaults.*` integration packages.

It decides **which culture** a request gets. It translates nothing — pair it with `01.Core`'s
`SharedKernel.Localization` and its `ILocalizationCatalog` for that.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Localization" />
```

```csharp
using SharedKernel.ServiceDefaults.Localization;

builder.AddServiceDefaults();
builder.Services.AddSharedKernelMultiTenancy();
builder.Services.AddScoped<ITenantCatalog>(sp => /* see SharedKernel.MultiTenancy's README */);

builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "preferred_culture");

var app = builder.Build();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // populates the ambient tenant id
app.UseRequestLocalization();                     // the BCL call — this package never wires it for you
```

## Resolution order

Signed signals before an unsigned header, deliberately — the same security lesson as
`SharedKernel.MultiTenancy`'s `StrategyOrder`:

1. **`UserPreference`** — the authenticated user's stored preference claim,
   `IUserContext.FindClaim(UserPreferenceClaimType)`. Skipped when `UserPreferenceClaimType` is unconfigured.
2. **`TenantDefault`** — the current tenant's `TenantDescriptor.DefaultCulture`, through an optionally
   registered `ITenantCatalog`. Skipped, never throwing, when none is registered.
3. **`AcceptLanguageHeader`** — the BCL's `AcceptLanguageHeaderRequestCultureProvider`.

## Rules

| Rule | Why |
| --- | --- |
| Configure at least one of `UserPreferenceClaimType` or an `ITenantCatalog` | With neither, both dynamic steps can never resolve and culture comes from `Accept-Language` alone — almost always by accident. A one-time startup warning (EventId `13004`) says so. |
| Place `TenantResolutionMiddleware` after `UseAuthentication()` and before `UseRequestLocalization()` | The tenant-default step reads the ambient tenant id, which that middleware populates from the authenticated user. |

## Why a separate package

It brings `SharedKernel.MultiTenancy` — for `ITenantCatalog` — and `SharedKernel.Security.Abstractions`,
neither of which a service without localization needs. The types keep their
`SharedKernel.ServiceDefaults.Localization` namespace from before the WO-084 split, and EventId `13004` is
unchanged, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base
and the full list of integration packages.
