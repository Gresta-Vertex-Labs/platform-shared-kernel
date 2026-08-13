# SharedKernel.Security.Oidc

OIDC/JWT implementation package for SharedKernel security. Provides `OidcUserContext`, `OidcTenantProvider`, `SecurityOptions` (including claim-type mapping), and DI extension methods.

## Public Surface

| Type | Kind | Purpose |
| --- | --- | --- |
| `OidcUserContext` | Sealed class | Maps `ClaimsPrincipal` to `IUserContext` via the configured `ClaimMappingOptions`; resolves `IdentityKind` (User/ServicePrincipal/Anonymous) |
| `OidcTenantProvider` | Sealed class | Resolves `TenantId` from the `tenant_id` claim; returns `Guid.Empty` when absent or unparseable |
| `SecurityOptions` | Sealed class | Options bound from the `Security` config section; startup-time validated |
| `ClaimMappingOptions` | Sealed class | Configures the claim types `OidcUserContext` reads for email/username/roles/permissions |
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
    },
    "ClaimMapping": {
      "EmailClaimType": "email",
      "NameClaimType": "name",
      "RoleClaimType": "roles",
      "PermissionClaimType": "scope"
    }
  }
}
```

`ClaimMapping` is optional — the defaults shown above (`"email"`/`"name"`/`"roles"`/`"scope"`) match the unmapped, short-name claim shape every standards-conformant OIDC issuer (Microsoft Entra ID v2.0, Auth0, Okta, Keycloak) emits by default against a .NET 8+ `JwtBearerHandler` (`MapInboundClaims = false`). If your identity provider still emits the legacy long-form `ClaimTypes.*` URIs, or you opted into `MapInboundClaims = true`, override the affected entries — e.g. `"RoleClaimType": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"` (equivalently, `SecurityClaimTypes.Role` from `SharedKernel.Security.Abstractions`).

`AddSharedKernelSecurity`/`AddAzureB2CAuthentication` both source `TokenValidationParameters.NameClaimType`/`RoleClaimType` from these same values, so ASP.NET Core's own claims machinery (`HttpContext.User.IsInRole(...)`, `[Authorize(Roles = ...)]`) never diverges from `IUserContext.HasRole`.

## `OidcUserContext` — role/permission parsing

`Roles` is resolved **defensively** — it accepts both one `Claim` per role and a single claim whose value is a JSON array of roles (e.g. `"roles": "[\"admin\",\"editor\"]"`), and never throws on either shape. `Permissions` is resolved from a single space-delimited claim value (the standard OAuth2 `scope` shape, e.g. `"orders:read orders:write"`), split into individual entries.

```csharp
// Application layer — HasRole for coarse checks, HasPermission for fine-grained ones
public sealed class ApproveOrderHandler(IUserContext user)
{
    public Task<Result> Handle(ApproveOrderCommand command, CancellationToken ct)
    {
        if (!user.HasRole("Approver") && !user.HasPermission("orders:approve"))
        {
            return Task.FromResult(Result.Failure(
                Error.Unauthorized("orders.approve_denied", "Caller cannot approve orders.")));
        }
        // ...
        return Task.FromResult(Result.Success());
    }
}
```

## DI Registration

```csharp
// Standard Entra ID / generic OIDC:
services.AddSharedKernelSecurity(configuration);

// Azure B2C / Entra External ID (requires an AzureAdB2C config section):
services.AddAzureB2CAuthentication(configuration);
```

Both extensions register JWT Bearer authentication plus the scoped `IUserContext`/`ITenantProvider` factories, with `AnonymousUserContext` as the fallback when no `HttpContext` is present. Call after `AddAuthentication()` in the host startup pipeline.

## AOT Notes

- `Microsoft.AspNetCore.Authentication.JwtBearer`: partially AOT-safe; JWT token parsing uses internal reflection in some code paths. Encapsulated behind `IUserContext` to limit the blast radius.
- `Microsoft.Identity.Web`: not fully AOT-safe; isolated to `AddAzureB2CAuthentication` only. Services using standard Entra ID can call `AddSharedKernelSecurity` to avoid this.
- The defensive role-claim reader's JSON-array-valued-claim path uses `System.Text.Json`, scoped narrowly to that one parse path.

## End-to-end recipes

The four recipes below bridge `IUserContext`/`ITenantProvider` into other capability domains' own locally-owned seam interfaces — each domain deliberately never references `SharedKernel.Security.Abstractions` directly (see each domain's own `CLAUDE.md` for the "why", summarized inline below). Every bridge type shown is written once, at the consuming service's composition root — none of it ships in this package.

### 1. `05.Application` — `IAuthorizationContext` bridge (`HasRole` + `HasPermission`)

`05.Application.Behaviors`'s `AuthorizationBehavior<TRequest,TResponse>` evaluates `IAuthorizeRequest`-marked commands/queries against a locally-owned `IAuthorizationContext` seam — never against `IUserContext` directly, mirroring the existing `IUnitOfWork`/`TransactionBehavior` bridge pattern so `05.Application` never takes a `12.Security` reference. `IAuthorizationContext` (`SharedKernel.Application.Behaviors.Authorization`) declares:

```csharp
public interface IAuthorizationContext
{
    Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken);
    Task<bool> AllOf(IEnumerable<string> requirements, CancellationToken cancellationToken);
    Task<bool> AnyOf(IEnumerable<string> requirements, CancellationToken cancellationToken);
}
```

Bridge it to `IUserContext.HasRole`/`.HasPermission` — a single `requirement` string can name either a role or a permission, since both checks are case-insensitive membership tests:

```csharp
public sealed class UserContextAuthorizationAdapter(IUserContext user) : IAuthorizationContext
{
    public Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken) =>
        Task.FromResult(user.HasRole(requirement) || user.HasPermission(requirement));

    public Task<bool> AllOf(IEnumerable<string> requirements, CancellationToken cancellationToken) =>
        Task.FromResult(requirements.All(r => user.HasRole(r) || user.HasPermission(r)));

    public Task<bool> AnyOf(IEnumerable<string> requirements, CancellationToken cancellationToken) =>
        Task.FromResult(requirements.Any(r => user.HasRole(r) || user.HasPermission(r)));
}

// Composition root:
services.AddScoped<SharedKernel.Application.Behaviors.Authorization.IAuthorizationContext, UserContextAuthorizationAdapter>();
```

### 2. `06.Persistence` — `TenantedDbContext` wiring via `ITenantProvider`

`06.Persistence.EfCore`'s `TenantedDbContext` (`SharedKernel.Persistence.EfCore.MultiTenancy`) takes `ITenantProvider` directly in its constructor and applies a global `HasQueryFilter` per `IHasTenant` entity, built with expression trees so EF Core rebinds the filter to whichever `TenantedDbContext` instance actually executes a given query (never a captured, possibly-stale `ITenantProvider` instance):

```csharp
public sealed class OrderDbContext(
    DbContextOptions<OrderDbContext> options,
    AuditInterceptor auditInterceptor,
    SoftDeleteInterceptor softDeleteInterceptor,
    ConcurrencyInterceptor concurrencyInterceptor,
    ITenantProvider tenantProvider)
    : TenantedDbContext(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor, tenantProvider)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // applies the global tenant filter first
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
    }
}

// Composition root — ITenantProvider resolves from 12.Security.Oidc's scoped factory automatically:
services
    .AddSharedKernelEfCore<OrderDbContext>(o => o.UseNpgsql(connectionString))
    .WithMultiTenancy()
    .Build();
```

No explicit `IUserContext`/`ITenantProvider` wiring is needed here beyond calling `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` earlier in the composition root — `TenantedDbContext`'s constructor parameter resolves the same scoped `ITenantProvider` this package already registers.

### 3. `07.Messaging` — `ITenantContextAccessor` bridge

`07.Messaging.Abstractions`'s `TenantHeaderPropagator` (enabled via `MessagingBusBuilder.WithTenantContext<TAccessor>()`) reads tenant identity through a locally-owned `ITenantContextAccessor` seam — mirroring the `IAuthorizationContext`/`IUnitOfWork` bridge pattern above, so `07.Messaging` never references `12.Security.Abstractions` directly:

```csharp
public interface ITenantContextAccessor
{
    Guid? TenantId { get; }
}
```

Bridge it to `ITenantProvider`, treating `Guid.Empty` (no tenant claim present) as "no tenant" rather than a real tenant id:

```csharp
public sealed class SecurityTenantContextAccessor(ITenantProvider tenantProvider) : ITenantContextAccessor
{
    public Guid? TenantId => tenantProvider.TenantId == Guid.Empty ? null : tenantProvider.TenantId;
}

// Composition root:
services.AddSharedKernelMessaging(configuration)
    .WithAmbientCorrelationPropagation()
    .WithTenantContext<SecurityTenantContextAccessor>()
    .Build();
```

Every outbound message published through this bus now carries the current request's tenant id as a transport header, sourced from whichever `ITenantProvider` `12.Security` has registered (`OidcTenantProvider` for an HTTP request, or `SystemUserContext`'s bridge — see recipe 4 — inside a background execution context).

### 4. Background-execution host — registering `SystemUserContext`

A Temporal activity host, a MassTransit consumer host, or any hosted-service startup path runs outside an HTTP request, so the scoped `IUserContext`/`ITenantProvider` factories `AddSharedKernelSecurity` registers (which resolve `IHttpContextAccessor.HttpContext`) always fall back to `AnonymousUserContext`/`Guid.Empty` there — indistinguishable from a genuinely rejected caller. Register `SystemUserContext` explicitly instead, **after** calling `AddSharedKernelSecurity` so this registration wins:

```csharp
// Composition root for a worker/background host (no HTTP pipeline):
services.AddSharedKernelSecurity(configuration); // still needed for JWT Bearer config validation, if any
services.AddScoped<IUserContext>(_ => SystemUserContext.Instance);

// A consuming service's own authorization bridge (recipe 1) decides what IdentityKind.System means —
// this package never bypasses authorization on its behalf:
public sealed class UserContextAuthorizationAdapter(IUserContext user) : IAuthorizationContext
{
    public Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken) =>
        Task.FromResult(user.IdentityKind == IdentityKind.System || user.HasRole(requirement) || user.HasPermission(requirement));
    // AllOf/AnyOf follow the same IdentityKind.System short-circuit if the host wants a trusted-background
    // context to bypass fine-grained checks — a deliberate choice made by the consuming service, not this domain.
}
```

`ITenantProvider` for a background host is typically supplied differently per message/activity (e.g. from the recipe-3 `ITenantContextAccessor`'s propagated header, mapped back to a request-scoped `ITenantProvider` per message) rather than a single process-wide sentinel — `SystemUserContext` only addresses the *user* identity half of the ambient context.
