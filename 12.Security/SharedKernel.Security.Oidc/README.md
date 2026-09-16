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
- DPoP proof JWT parsing/verification uses `Microsoft.IdentityModel.JsonWebTokens`/`Microsoft.IdentityModel.Tokens` — already transitively present via `JwtBearer`/`Microsoft.Identity.Web`, so it carries the same partial-AOT caveat documented above for those packages; no new AOT surface is introduced.

## DPoP Quick Start (RFC 9449)

DPoP (Demonstrating Proof-of-Possession) binds an access token to the specific client that requested it — a request presenting a stolen bearer token, with no matching private key, is rejected even though the token itself is otherwise fully valid. It is this domain's **primary recommended sender-constraining mechanism** — chosen over mTLS certificate binding (`SharedKernel.Security.Mtls`) for its dramatically lower developer-integration burden: no PKI/certificate-provisioning process is required of either the client or the consuming team, matching the platform's "developer friendly" requirement. It is also the mechanism [FAPI 2.0](https://openid.net/specs/fapi-security-profile-2_0.html) (the successor to the FAPI 1.0 profile several legacy Open Banking regimes still mandate mTLS binding under) recommends as its default sender-constraining option.

DPoP is **opt-in and disabled by default** — calling `.RequireDpop<TReplayCache>()` adds zero behavior change to any consumer that never calls it.

Implement `IDpopProofReplayCache` against whatever short-lived store your service already has available — this package never dictates a storage mechanism, mirroring `IApiKeyValidator`'s "never dictates storage" precedent exactly:

```csharp
// A minimal implementation sketch backed by 02.Caching.Redis.HashStore (or any distributed cache with
// per-key TTL support). Never referenced directly by this package — bridged at the composition root.
public sealed class DistributedDpopReplayCache(IRedisHashService cache) : IDpopProofReplayCache
{
    public async Task<bool> TryConsumeAsync(string jti, DateTimeOffset proofExpiresAt, CancellationToken ct)
    {
        var ttl = proofExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return false; // already past its own freshness window — treat as replay/expired
        }

        // Returns false if the key already existed (a genuine replay); true on first insert.
        return await cache.SetIfNotExistsAsync($"dpop:jti:{jti}", "1", ttl, ct);
    }
}
```

Opt in by chaining off the builder `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` now return:

```csharp
services
    .AddSharedKernelSecurity(configuration)
    .RequireDpop<DistributedDpopReplayCache>();
```

A correctly-bound request (valid proof signature, matching `htm`/`htu`, fresh `iat`, unseen `jti`, and a `jkt` matching the access token's `cnf.jkt` claim) resolves `IUserContext.IsSenderConstrained == true`. A request presenting an ordinary, unconstrained bearer token — or any proof that fails validation — is rejected outright by the authentication pipeline; `IsSenderConstrained` only ever reads `false` for a request that *did* authenticate (e.g. via API key or mTLS, which have no DPoP concept), never as a "partially accepted" state for a failed DPoP request.

**`ath` binding (RFC 9449 §4.3, WO-060):** in addition to `htm`/`htu`/`iat`/`jkt`/`jti`, every proof must also carry an `ath` claim equal to `base64url(SHA-256(the accompanying raw bearer access token))`, compared constant-time. This is a **fifth, mandatory binding** — distinct from `jkt`, which ties the proof to the client's DPoP *key*, `ath` ties it to the *specific access token* the proof accompanies, so a proof minted for one token cannot be replayed alongside a different (even validly DPoP-bound) token from the same client. A missing, malformed, or mismatched `ath` rejects through the same failure path as every other binding — no separate error surface, no extra integration step for a consumer already implementing `IDpopProofReplayCache` correctly.

## Signing-algorithm allowlist (`Jwt.ValidAlgorithms`, FAPI 2.0 baseline)

`SecurityOptions.Jwt.ValidAlgorithms` restricts which JWS signing algorithms an incoming access token may be signed with, enforced **before** any signature or claim evaluation proceeds — an out-of-allowlist `alg` (including a crafted `"alg": "none"` token) is rejected at the earliest possible point, for both the JWT Bearer path and (via the independently-configurable `DpopOptions.ValidAlgorithms`) the DPoP proof path.

The default, `["PS256", "ES256"]`, is the [FAPI 2.0 Security Profile](https://openid.net/specs/fapi-security-profile-2_0.html) baseline — a defense against algorithm-confusion/downgrade attacks, not an arbitrary restriction. **This is a deliberately breaking default:** most real-world identity providers (Microsoft Entra ID, Auth0, Okta) sign access tokens with `RS256` by default, which is *not* in this allowlist. A consuming service on such a provider must explicitly widen it after upgrading, or every previously-valid token is rejected:

```csharp
services.Configure<SecurityOptions>(options =>
    options.Jwt.ValidAlgorithms = ["PS256", "ES256", "RS256"]);
```

Widening the allowlist is a deliberate, documented choice for a non-FAPI identity provider — it is not itself a security regression, since `RS256` remains a strong asymmetric algorithm; the default simply does not assume it. A rejection is audit-logged via `SecurityLogEvents.JwtSigningAlgorithmRejected` (EventId `12104`), naming only the rejected algorithm — never any part of the token itself.

## Token Revocation Quick Start (RFC 7662-shaped)

> **Trade-off callout:** this is the **one capability in this domain with a genuine per-request latency/availability cost.** Every other check in this package (signature, issuer, audience, lifetime, DPoP proof binding) is self-contained — a revocation check, by its nature, asks *something else* (a database, a cache, or the identity provider's own introspection endpoint) whether a token is still good, on the hot path of every authenticated request. Enable it only when your regulatory or threat model genuinely requires near-real-time revocation (immediate session kill-switch, compromised-token response) — not as a default-on hardening step.

Token revocation is **opt-in, disabled by default, and fails closed** — an unavailable or throwing `ITokenRevocationCheck` rejects the request; it never silently authenticates. Implement it against a `jti`-keyed revocation list, or an RFC 7662 introspection HTTP call to your identity provider (introspection needs the raw token, which `tokenIdentifier` supplies):

```csharp
public sealed class IntrospectionBackedRevocationCheck(HttpClient introspectionClient) : ITokenRevocationCheck
{
    public async Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
    {
        using var response = await introspectionClient.PostAsync(
            "connect/introspect",
            new FormUrlEncodedContent([new("token", tokenIdentifier)]),
            ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IntrospectionResponse>(ct);
        return result is not { Active: true }; // RFC 7662: "active": false means revoked/expired/unknown
    }

    private sealed record IntrospectionResponse(bool Active);
}
```

Opt in by chaining off the same builder — combine with `.RequireDpop<TReplayCache>()` if both are needed; registration order determines the order the two checks run in:

```csharp
services
    .AddSharedKernelSecurity(configuration)
    .WithRevocationCheck<IntrospectionBackedRevocationCheck>();
```

The check runs only *after* standard signature/issuer/audience/lifetime validation succeeds — a structurally invalid token is rejected regardless of whether revocation checking is configured or reachable. A revoked token rejects with the exact same generic authentication-failure shape as an expired or malformed token — no distinct, information-leaking error is ever surfaced to the caller, and a non-opted-in host never invokes `ITokenRevocationCheck` at all.

### Caching the revocation check (`IRevocationCheckCache`, WO-060)

A revocation check runs on every authenticated request — for most consumers, most of that traffic asks the same question about the same still-valid token repeatedly within a short window. `IRevocationCheckCache` lets you avoid the repeat round-trip without weakening the fail-closed contract above: `CachingTokenRevocationCheck` checks the cache first, calls through to your `ITokenRevocationCheck` on a miss, and populates the cache with `RevocationCheckCacheOptions.RevokedTtl` (default **5 seconds**) or `.NotRevokedTtl` (default **30 seconds**) depending on the outcome.

**Do not unify the two TTLs.** `RevokedTtl` is deliberately much shorter than `NotRevokedTtl` — a "not revoked" verdict may be cached generously, because a false negative there costs one extra introspection call, not a security gap. A "revoked" verdict must never be allowed to look "not revoked" again for long, so it is trusted for only a few seconds before the inner check is consulted again. Setting both TTLs to the same value defeats the reason this seam exists.

Like `IDpopProofReplayCache`, this package **never references `02.Caching`** — `IRevocationCheckCache` is implemented and wired entirely at your own composition root, mirroring the `IUnitOfWork`/`ITenantContextAccessor` local-seam bridge pattern already used elsewhere on the platform. Two worked examples:

**`IMemoryCache`-backed** (single-instance/dev, or as an L1 in front of a distributed store):

```csharp
public sealed class MemoryRevocationCheckCache(IMemoryCache cache) : IRevocationCheckCache
{
    public Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct) =>
        Task.FromResult(cache.TryGetValue(CacheKey(tokenIdentifier), out bool isRevoked)
            ? (bool?)isRevoked
            : null);

    public Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct)
    {
        cache.Set(CacheKey(tokenIdentifier), isRevoked, ttl);
        return Task.CompletedTask;
    }

    private static string CacheKey(string tokenIdentifier) => $"revocation:{tokenIdentifier}";
}
```

**Redis-backed**, via `02.Caching.Abstractions`'s `ICacheService` (correct for a multi-instance deployment, where an in-process `IMemoryCache` would let each pod cache a stale answer independently):

```csharp
public sealed class RedisRevocationCheckCache(ICacheService cache) : IRevocationCheckCache
{
    public async Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct) =>
        await cache.GetAsync<bool?>(CacheKey(tokenIdentifier), ct);

    public async Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct) =>
        await cache.SetAsync(CacheKey(tokenIdentifier), isRevoked, CachePolicy.For(ttl, ttl), ct);

    private static string CacheKey(string tokenIdentifier) => $"revocation:{tokenIdentifier}";
}
```

`CachePolicy.For(ttl, ttl)` pins both the L1 and L2 duration to the caller-supplied `ttl` (`RevokedTtl`/`NotRevokedTtl`, chosen by `CachingTokenRevocationCheck` per outcome) — this bridge does not introduce a second, independently-tuned TTL layer on top of the one the revocation-caching seam already controls.

Opt in by chaining `.WithRevocationCheckCaching<TCache>()` **after** `.WithRevocationCheck<TCheck>()` in the same call — calling it first throws `InvalidOperationException`, since there is nothing to cache-wrap yet:

```csharp
services
    .AddSharedKernelSecurity(configuration)
    .WithRevocationCheck<IntrospectionBackedRevocationCheck>()
    .WithRevocationCheckCaching<RedisRevocationCheckCache>();
```

A cache-lookup failure (a thrown exception from `TryGetAsync`/`SetAsync`) falls through to the inner `ITokenRevocationCheck` — it never causes a legitimately non-revoked token to be rejected merely because the cache was unreadable, and it never treats an unreadable cache as license to skip the check outright.

> For the multi-tenant trust-boundary question this recipe does not answer — whether a token's `tenant_id` claim can be trusted to name the *correct* tenant, not merely an authentically-signed one — see [Multi-tenant JWT claim trust boundary](#multi-tenant-jwt-claim-trust-boundary) below.

## Multi-tenant JWT claim trust boundary

This section states precisely what tenant-claim trust this package provides once JWT validation succeeds, and what remains your identity provider's (IdP's) own responsibility. It exists because this boundary was previously undocumented anywhere in this domain's brain or READMEs — a defensible design that read as silence during a security due-diligence review (WO-060, P-392). The full version of this content, including how it extends to `SharedKernel.Security.ApiKey`/`.Mtls`'s analogous claims, lives in `12.Security/CLAUDE.md`'s `## Domain Invariants` section; this is the README-facing summary.

**What this package guarantees**, once `SecurityOptions.Jwt` signature/issuer/audience/lifetime validation succeeds (and, as of WO-060, the [`Jwt.ValidAlgorithms` allowlist check](#signing-algorithm-allowlist-jwtvalidalgorithms-fapi-20-baseline) passes):

- The `tenant_id` claim value returned by `OidcTenantProvider.TenantId` is **exactly the value the issuing IdP placed in the token** — a bearer of the token cannot change it without invalidating the token's signature.
- Neither `OidcUserContext` nor `OidcTenantProvider` reinterprets the claim beyond a direct `Guid.TryParse` — the trust boundary is exactly "what the IdP signed," never a value this package independently derives, caches, or re-computes.
- A structurally invalid token (bad signature, wrong issuer/audience, expired, disallowed algorithm) never reaches claim resolution at all — `TenantId` only ever reflects a claim from a token this package has already cryptographically authenticated.

**What remains your IdP's own responsibility — this package cannot verify it:**

- That `tenant_id` was populated with the *correct* tenant for the authenticated principal. If a single `SecurityOptions.Jwt.Authority` genuinely serves more than one tenant (a shared multi-tenant IdP tenant, as opposed to per-tenant issuer segregation), this package has no independent way to confirm the claim's value actually names a tenant the subject is a legitimate member of — that correctness is delegated entirely to the IdP's own claim-issuance logic.
- A misconfigured IdP, or an app registration that lets a caller influence which `tenant_id` value is minted into its own token, produces a token this package will faithfully accept as validly signed — with an incorrect tenant claim. Signature validity proves authenticity of the *claim value*, not correctness of the tenant it names.

**Recommended posture:**

- **Preferred:** a per-tenant `Authority`/issuer topology, so a token minted for Tenant A's issuer cannot even pass `ValidateIssuer`/`Jwt.Authority` validation against a host configured for Tenant B — the boundary above becomes moot because cross-tenant claim confusion never reaches this package's validation pipeline.
- **When a single shared multi-tenant IdP `Authority` is unavoidable:** treat `ITenantProvider.TenantId` as *authenticated-but-not-independently-verified*, and layer an additional authoritative check (e.g. a tenant-membership lookup against your own tenant/user store) before using it for a high-consequence authorization decision. This package performs no such secondary check itself — doing so would require a data-access dependency `12.Security`'s zero-infrastructure-coupling design (may only reference `01.Core`) forbids.

This boundary applies uniformly to every trust decision this domain hands you a claim/result for — including the two other recipes from this same documentation pass: the [revocation-caching seam](#caching-the-revocation-check-irevocationcheckcache-wo-060) trusts whatever `ITokenRevocationCheck`/`IRevocationCheckCache` your composition root wires in exactly as much as it trusts the underlying token, and `SharedKernel.Security.ApiKey`'s [rotation-window recipe](../SharedKernel.Security.ApiKey/README.md#rotation-window-recipe-wo-060) trusts `ApiKeyValidationResult.ClientId`/`.Roles`/`.Permissions` exactly as much as it trusts your own `IApiKeyValidator`'s answer — in every case, the validator/IdP is the source of truth this domain surfaces, never one it independently re-derives.

## End-to-end recipes

The five recipes below bridge `IUserContext`/`ITenantProvider` into other capability domains' own locally-owned seam interfaces — each domain deliberately never references `SharedKernel.Security.Abstractions` directly (see each domain's own `CLAUDE.md` for the "why", summarized inline below). Every bridge type shown is written once, at the consuming service's composition root — none of it ships in this package.

### 1. `05.Application` — `IRequestContext` bridge (`HasRole` + `HasPermission`)

`05.Application.Behaviors`'s `AuthorizationBehavior<TRequest,TResponse>` evaluates `IAuthorizeRequest`-marked commands/queries against a locally-owned `IRequestContext` seam — never against `IUserContext` directly, mirroring the existing `IUnitOfWork`/`TransactionBehavior` bridge pattern so `05.Application` never takes a `12.Security` reference. `IRequestContext` (`SharedKernel.Application.Context`) declares:

```csharp
public interface IRequestContext
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    Guid? TenantId { get; }
    ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken);
}
```

A request declares what it needs through `IAuthorizeRequest.RequiredPermissions`, evaluated per `IAuthorizeRequest.PermissionMatch` (`All` by default, or `Any`). The behavior fails closed: an unauthenticated caller gets `Error.Unauthorized` (401), a missing permission or an empty `RequiredPermissions` gets `Error.Forbidden` (403), and both come back as a failed `Result`, never an exception.

Bridge it to `IUserContext`/`ITenantProvider`. A single permission string can name either a role or a permission, since both checks are case-insensitive membership tests:

```csharp
public sealed class UserRequestContext(IUserContext user, ITenantProvider tenantProvider) : IRequestContext
{
    public bool IsAuthenticated => user.IsAuthenticated;

    public string? UserId => user.IsAuthenticated ? user.UserId.ToString("D") : null;

    public Guid? TenantId => tenantProvider.TenantId == Guid.Empty ? null : tenantProvider.TenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(user.HasRole(permission) || user.HasPermission(permission));
}

// Composition root — register the seam before AddAuthorizationBehavior()'s Build(), which throws without it:
services.AddScoped<SharedKernel.Application.Context.IRequestContext, UserRequestContext>();
services.AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()
    .AddAuthorizationBehavior()
    .Build();
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

`07.Messaging.Abstractions`'s `TenantHeaderPropagator` (enabled via `MessagingBusBuilder.WithTenantContext<TAccessor>()`) reads tenant identity through a locally-owned `ITenantContextAccessor` seam — mirroring the `IRequestContext`/`IUnitOfWork` bridge pattern above, so `07.Messaging` never references `12.Security.Abstractions` directly:

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
// this package never bypasses authorization on its behalf. Granting a trusted background context
// every permission is a deliberate choice made by the consuming service, not this domain:
public sealed class UserRequestContext(IUserContext user, ITenantProvider tenantProvider) : IRequestContext
{
    public bool IsAuthenticated => user.IsAuthenticated;

    public string? UserId => user.IsAuthenticated ? user.UserId.ToString("D") : null;

    public Guid? TenantId => tenantProvider.TenantId == Guid.Empty ? null : tenantProvider.TenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            user.IdentityKind == IdentityKind.System || user.HasRole(permission) || user.HasPermission(permission));
}
```

`SystemUserContext.IsAuthenticated` is `true`, so this bridge lets the commands a Temporal activity (`CommandActivity<TCommand>`) or scheduled job (`ScheduledCommandJob<TCommand>`) sends pass `AuthorizationBehavior`'s authentication check. Without the registration above, the scoped factory falls back to `AnonymousUserContext` and every guarded command fails closed with `Error.Unauthorized`. Each such command runs in the activity's or job's own DI scope, so it is an outermost command for `ICommandScope` and commits its own unit of work.

`ITenantProvider` for a background host is typically supplied differently per message/activity (e.g. from the recipe-3 `ITenantContextAccessor`'s propagated header, mapped back to a request-scoped `ITenantProvider` per message) rather than a single process-wide sentinel — `SystemUserContext` only addresses the *user* identity half of the ambient context.

**A host with no `IUserContext` at all does not need this bridge.** `05.Application` ships `SystemRequestContext`, an `IRequestContext` taking an identity name and an explicit permission set, so a worker that never sees a token registers that directly and skips `SystemUserContext` entirely. Reach for the bridge above only when the same host also serves authenticated callers and you want one `IRequestContext` covering both.

### 5. Step-up authorization — gating a high-risk operation on `AuthenticationMethods`/`AuthTime`

PSD2/FFIEC/PCI-DSS-style regulatory regimes commonly require a *recently, strongly* authenticated session before allowing a high-risk operation (a funds transfer, a limit change, a credential rotation) — not merely "the caller has a still-valid token from three hours ago." `IUserContext.WasAuthenticatedWith`/`.IsAuthenticationFresherThan` exist for exactly this, wired through the same `IRequestContext` bridge recipe 1 already established:

```csharp
public sealed class StepUpRequestContext(IUserContext user, ITenantProvider tenantProvider, IClock clock)
    : IRequestContext
{
    public bool IsAuthenticated => user.IsAuthenticated;

    public string? UserId => user.IsAuthenticated ? user.UserId.ToString("D") : null;

    public Guid? TenantId => tenantProvider.TenantId == Guid.Empty ? null : tenantProvider.TenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(permission switch
        {
            // High-risk requirement: caller must have used MFA within the last 5 minutes —
            // a token's raw expiry says nothing about how recently the human actually authenticated.
            "step_up:high_risk" => user.WasAuthenticatedWith("mfa")
                && user.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), clock.UtcNow),
            _ => user.HasRole(permission) || user.HasPermission(permission),
        });
}
```

```csharp
// Application layer — a high-risk command opts into the step-up requirement via IAuthorizeRequest,
// exactly like any other AuthorizationBehavior-gated command. PermissionMatch defaults to All, so the
// caller needs both the business permission and a fresh MFA; failing either returns Error.Forbidden.
public sealed record TransferFundsCommand(Guid FromAccount, Guid ToAccount, decimal Amount)
    : ICommand, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => ["payments.transfer", "step_up:high_risk"];
}
```

`now` is always supplied explicitly from the composition root's own injected `IClock` (never `DateTimeOffset.UtcNow` inside `IsAuthenticationFresherThan` itself, per the platform's injectable-time convention) — this keeps the freshness check deterministically testable and consistent with how every other time-sensitive check in the platform is written. If the caller authenticated via `SharedKernel.Security.ApiKey`/`SharedKernel.Security.Mtls` rather than OIDC, `AuthenticationMethods` is empty and `AuthTime` is `null` — `WasAuthenticatedWith`/`IsAuthenticationFresherThan` correctly return `false` for both, so a step-up requirement naturally rejects a machine-client credential that has no authentication-context concept to satisfy it.
