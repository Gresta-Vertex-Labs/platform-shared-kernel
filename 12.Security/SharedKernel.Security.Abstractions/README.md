# SharedKernel.Security.Abstractions

Zero-dependency security abstractions for the SharedKernel. References only `SharedKernel.Primitives`.

## Public Surface

| Type | Kind | Purpose |
| --- | --- | --- |
| `IUserContext` | Interface | Current user identity — UserId, Email, Username, Roles, Permissions, Claims, IsAuthenticated, IdentityKind, HasRole, HasPermission |
| `ITenantProvider` | Interface | Current tenant identity — TenantId (Guid.Empty when absent) |
| `IdentityKind` | Enum | Discriminates the kind of identity behind `IUserContext` — `Anonymous`, `User`, `ServicePrincipal`, `System` |
| `AnonymousUserContext` | Sealed class | Sentinel for unauthenticated requests; always resolvable from DI |
| `SystemUserContext` | Sealed class | Sentinel for trusted, non-HTTP execution contexts (background hosts) |
| `SecurityClaimTypes` | Static class | Well-known claim type string constants (sub, tenant_id, plus legacy email/role reference constants) |

`IUserContext` also carries a step-up-authentication signal surface — `AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`/`WasAuthenticatedWith`/`IsAuthenticationFresherThan` — and a sender-constraining signal, `IsSenderConstrained`. See below.

## `IUserContext` / `ITenantProvider`

```csharp
// Application layer
public sealed class MyCommandHandler(IUserContext user, ITenantProvider tenant)
{
    public Task Handle(MyCommand command, CancellationToken ct)
    {
        if (!user.IsAuthenticated)
        {
            throw new UnauthorizedAccessException();
        }

        var tenantId = tenant.TenantId; // pass as a Guid primitive to the domain constructor
        // ...
    }
}
```

Both `IUserContext` and `ITenantProvider` are scoped — one instance per HTTP request. Register via `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` from `SharedKernel.Security.Oidc`.

## `HasRole` / `HasPermission`

`Roles` answers coarse role-membership questions ("is this an Admin"); `Permissions` answers fine-grained per-action/per-resource questions ("can this identity do `orders:write`"), typically sourced from an OAuth2 `scope` claim. Both comparisons are case-insensitive, and both are legitimate — not mutually exclusive:

```csharp
if (!user.HasRole("Admin") && !user.HasPermission("orders:write"))
{
    return Result.Failure(Error.Unauthorized("orders.write_denied", "Caller cannot write orders."));
}
```

## `IdentityKind`

`IdentityKind` corrects an over-strong invariant a plain `IsAuthenticated` boolean cannot express: a valid client-credentials (machine-to-machine) token and a rejected/unauthenticated caller both used to collapse onto the same `UserId == Guid.Empty` shape. As of `IdentityKind`, the invariant is:

- `UserId` is never `Guid.Empty` **only** when `IdentityKind == IdentityKind.User`.
- `IdentityKind.ServicePrincipal` and `IdentityKind.System` legitimately carry `IsAuthenticated == true` with `UserId == Guid.Empty` — this is the standard shape of a client-credentials token or a trusted background context, not a bug.

| Value | Meaning |
| --- | --- |
| `Anonymous` (default) | No valid authenticated principal. `default(IdentityKind)` — an uninitialized value fails safe. |
| `User` | A human subject — a parseable, non-empty `Guid` `sub` claim was present on an authenticated principal. |
| `ServicePrincipal` | A valid, authenticated token with no human subject — a client-credentials/M2M token, or a matched API key (`SharedKernel.Security.ApiKey`). |
| `System` | A trusted, non-HTTP execution context. See `SystemUserContext` below. |

```csharp
var access = user.IdentityKind switch
{
    IdentityKind.User => AccessLevel.Interactive,
    IdentityKind.ServicePrincipal => AccessLevel.MachineToMachine,
    IdentityKind.System => AccessLevel.Trusted,
    IdentityKind.Anonymous or _ => AccessLevel.None,
};
```

## Step-up authentication signals (`AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`)

PSD2/FFIEC/PCI-DSS-style regimes commonly require gating a high-risk operation (a funds transfer, a limit change, a credential rotation) on *how* and *how recently* the caller authenticated — not merely "the token is still valid." These members surface the OIDC `amr`/`acr`/`auth_time` claims for exactly that:

| Member | Meaning |
| --- | --- |
| `AuthenticationMethods` | The OIDC `amr` (Authentication Method Reference) values for the session — e.g. `"pwd"`, `"otp"`, `"mfa"`, `"hwk"`. Empty when the identity source has no authentication-context concept (API key, mTLS, `AnonymousUserContext`, `SystemUserContext`). |
| `AuthContextClassReference` | The OIDC `acr` (Authentication Context Class Reference) assurance-level claim. `null` when absent or not applicable. |
| `AuthTime` | The UTC instant authentication actually occurred (OIDC `auth_time`) — distinct from token-issued-at. `null` when absent, malformed, or not applicable. |
| `WasAuthenticatedWith(method)` | `true` when `method` is found in `AuthenticationMethods`. Case-insensitive, mirrors `HasRole` exactly. |
| `IsAuthenticationFresherThan(maxAge, now)` | `true` when `AuthTime` is present and `now - AuthTime <= maxAge`. **`now` is always supplied by the caller** (typically from its own injected `IClock`) — this member never calls `DateTimeOffset.UtcNow` internally, per the platform's injectable-time convention. |

```csharp
// Gate a high-risk command on recent, strong authentication rather than a token's raw expiry:
if (!user.WasAuthenticatedWith("mfa") || !user.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), clock.UtcNow))
{
    return Result.Failure(Error.Unauthorized("step_up.required", "Recent MFA is required for this operation."));
}
```

See `SharedKernel.Security.Oidc/README.md`'s "Step-up authorization" recipe for a fully worked `IAuthorizationContext` bridge.

## `IsSenderConstrained`

`true` only when the current request's access token was validated as DPoP-bound (RFC 9449) for **this** request — a fresh, correctly-signed proof matching the token's `cnf.jkt` was presented. `false` for every other identity source, including a valid-but-unconstrained bearer token, an API-key-authenticated request, and an mTLS-authenticated request (mTLS is its own, distinct sender-constraining mechanism — see `SharedKernel.Security.Mtls/README.md`'s RFC 8705 section).

```csharp
if (requiresSenderConstrainedToken && !user.IsSenderConstrained)
{
    return Result.Failure(Error.Unauthorized("token.not_sender_constrained", "A DPoP-bound access token is required."));
}
```

Opt into DPoP validation via `SharedKernel.Security.Oidc`'s `.RequireDpop<TReplayCache>()` — see its README's DPoP Quick Start.

## `AnonymousUserContext`

The DI fallback so `IUserContext` is always resolvable, regardless of authentication state — the scoped factory registered by `AddSharedKernelSecurity` returns `AnonymousUserContext.Instance` when no `HttpContext` is present (background workers, console hosts, unit-test DI containers). All string properties are `null`; `Roles`/`Permissions`/`Claims` are empty; `UserId == Guid.Empty`; `IsAuthenticated == false`; `IdentityKind == IdentityKind.Anonymous`; `HasRole`/`HasPermission` always return `false`.

```csharp
IUserContext anonymous = AnonymousUserContext.Instance;
Debug.Assert(anonymous.IdentityKind == IdentityKind.Anonymous);
Debug.Assert(!anonymous.IsAuthenticated);
```

## `SystemUserContext`

A peer of `AnonymousUserContext`, not a replacement for it — represents a trusted, non-HTTP execution authority (a Temporal activity, a MassTransit consumer, a Hangfire job, a startup seeder) as opposed to a rejected or genuinely unauthenticated caller. `IdentityKind == IdentityKind.System`; `IsAuthenticated == true`; `UserId == Guid.Empty` (the standard shape for a trusted background context — not a bug, see `IdentityKind` above); `Roles`/`Permissions`/`Claims` are empty; `HasRole`/`HasPermission` always return `false` — a system context asserts trust by `IdentityKind`, never by inheriting role/permission membership it was never granted.

This package ships **no DI wiring** for `SystemUserContext` — a background-execution composition root registers it explicitly in place of the HTTP-derived `IUserContext` factory:

```csharp
services.AddScoped<IUserContext>(_ => SystemUserContext.Instance);
```

See the "Background-execution host" recipe in `SharedKernel.Security.Oidc/README.md` for a fully worked example.

## `SecurityClaimTypes`

```csharp
SecurityClaimTypes.UserId    // "sub" — always the active lookup key
SecurityClaimTypes.TenantId  // "tenant_id" — always the active lookup key
SecurityClaimTypes.Email     // legacy-shape reference constant (maps to ClaimTypes.Email)
SecurityClaimTypes.Role      // legacy-shape reference constant (maps to ClaimTypes.Role)
```

`UserId`/`TenantId` are the claim types `OidcUserContext`/`OidcTenantProvider` always read directly — `"sub"`/`"tenant_id"` are already short names, unaffected by claim-mapping configuration.

`Email`/`Role` are **legacy-shape reference constants only**. The active runtime lookup key for email/username/role/permission resolution is `SecurityOptions.ClaimMapping` (`SharedKernel.Security.Oidc`), whose short-name defaults (`"email"`/`"name"`/`"roles"`/`"scope"`) match the unmapped claim shape every standards-conformant OIDC issuer emits by default against a .NET 8+ `JwtBearerHandler` (`MapInboundClaims = false`). Use `SecurityClaimTypes.Email`/`.Role` only when configuring an explicit `ClaimMapping` override for an identity provider still emitting the legacy long-form `ClaimTypes.*` shape — see `SharedKernel.Security.Oidc/README.md`.
