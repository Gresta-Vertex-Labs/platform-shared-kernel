# SharedKernel.Security.Oidc

OIDC/JWT implementation package for SharedKernel security. Provides `OidcUserContext`, `OidcTenantProvider`, `SecurityOptions`, and DI extension methods.

## Public Surface

| Type | Kind | Purpose |
|------|------|---------|
| `OidcUserContext` | Sealed class | Maps `ClaimsPrincipal` to `IUserContext`; forces `IsAuthenticated=false` if `sub` is missing/unparseable |
| `OidcTenantProvider` | Sealed class | Resolves `TenantId` from `tenant_id` claim; returns `Guid.Empty` when absent |
| `SecurityOptions` | Sealed class | Options bound from `Security` config section; startup-time validated |
| `SecurityServiceCollectionExtensions` | Static class | `AddSharedKernelSecurity` / `AddAzureB2CAuthentication` DI extension methods |

## Configuration

```json
{
  "Security": {
    "Jwt": {
      "Authority": "https://login.microsoftonline.com/{tenantId}/v2.0",
      "Audience": "api://my-api-client-id",
      "ValidateLifetime": true,
      "ClockSkewSeconds": 30
    }
  }
}
```

## DI Registration

```csharp
// Standard Entra ID / generic OIDC:
services.AddSharedKernelSecurity(configuration);

// Azure B2C / Entra External ID (requires AzureAdB2C config section):
services.AddAzureB2CAuthentication(configuration);
```

## AOT Notes

- `Microsoft.AspNetCore.Authentication.JwtBearer`: partially AOT-safe; JWT token parsing uses internal reflection in some code paths. Encapsulated behind `IUserContext` to limit the blast radius.
- `Microsoft.Identity.Web`: not fully AOT-safe; isolated to `AddAzureB2CAuthentication` only. Services using standard Entra ID can call `AddSharedKernelSecurity` to avoid this.
