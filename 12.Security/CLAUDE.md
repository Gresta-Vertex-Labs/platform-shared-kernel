# 12.Security — Identity & Access Brain

## What This Domain Is

The security abstractions and OIDC/B2C wiring layer. Downstream microservices depend on `SharedKernel.Security.Abstractions` to access the current user identity and tenant context — never on the concrete OIDC implementation. The concrete `SharedKernel.Security.Oidc` package wires JWT validation, Azure B2C / Microsoft Entra External ID authentication, and maps claims to the abstraction contracts.

Philosophy: **Thin abstractions. Claims-first. No domain coupling. Request-scoped identity.**

> `12.Security` may only reference `01.Core`. It must never reference `03.Domain`, `06.Persistence`, or any other capability domain.

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Security.Abstractions` | `IUserContext`, `ITenantProvider` interfaces, `IdentityKind`, `AnonymousUserContext`/`SystemUserContext` sentinels — the only types application and domain-adjacent code should ever inject | `SharedKernel.Primitives` |
| `SharedKernel.Security.Oidc` | Concrete JWT/OIDC implementation: Azure B2C / Entra External ID wiring, configurable claims-to-`IUserContext` mapping, `ITenantProvider` claim resolution, structured security-audit logging, DI extensions | `SharedKernel.Security.Abstractions`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Identity.Web` |
| `SharedKernel.Security.ApiKey` | Pre-shared-key / machine-client authentication scheme — `IApiKeyValidator` extensibility point, constant-time key comparison, composes alongside JWT Bearer via a policy/forwarding scheme; never OIDC | `SharedKernel.Security.Abstractions`, `SharedKernel.Cryptography` (`01.Core`) |
| `SharedKernel.Security.Mtls` *(WO-058)* | Mutual-TLS client-certificate authentication scheme for regulated Open Banking/PSD2-style external APIs — `IMtlsCertificateValidator` extensibility point, `MtlsUserContext`, `MtlsAuthenticationHandler`, plus RFC 8705 certificate-bound (`cnf.x5t#S256`) access-token validation; a sibling to `.ApiKey`, never OIDC, never references `.Oidc`/`.ApiKey` | `SharedKernel.Security.Abstractions`, `SharedKernel.Cryptography` (`01.Core`), `Microsoft.AspNetCore.Authentication.Certificate` |
| `SharedKernel.Security.Totp` *(WO-069, P-452, SHIPPED)* | Second-factor (TOTP) enrollment/challenge orchestration plus an `IClaimsTransformation`-based step-up wiring that makes a successful TOTP verification observable through `IUserContext.WasAuthenticatedWith`/`AuthenticationMethods` with zero changes required in `.Oidc` or `14.Presentation` — `TotpEnrollmentService`, `ITotpChallengeStore` extensibility point, `TotpChallengeService`, `TotpStepUpClaimsTransformation`; a fifth sibling to `.Oidc`/`.ApiKey`/`.Mtls`, never references any of them | `SharedKernel.Security.Abstractions`, `SharedKernel.Cryptography` (`01.Core`) |

All five target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — only `SharedKernel.Primitives` project reference. `SharedKernel.Security.ApiKey`, `SharedKernel.Security.Mtls`, and `SharedKernel.Security.Totp` are sibling provider packages to `SharedKernel.Security.Oidc` and to **each other** — a machine-client credential scheme, a certificate-based scheme, and a second-factor step-up scheme are none of them OIDC concepts, so none lives inside `.Oidc`, and sibling provider packages never reference one another (each references only `.Abstractions` + whatever `01.Core` primitives it needs). `SharedKernel.Security.Totp` additionally never references `.ApiKey`/`.Mtls` — its `IClaimsTransformation`-based composition mechanism reaches `.Oidc`'s `OidcUserContext` indirectly, through the shared `ClaimsPrincipal`, never through a type reference (see the dedicated `SharedKernel.Security.Totp` section below).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Identity abstractions | Pure C# 13 — zero external NuGet dependencies |
| JWT Bearer validation | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Azure B2C / Entra External ID | `Microsoft.Identity.Web` |
| Claims parsing | `System.Security.Claims` (BCL — no NuGet) |
| DI wiring | `Microsoft.Extensions.DependencyInjection.Abstractions` (transitive via ASP.NET Core) |
| API key authentication *(WO-057)* | `Microsoft.AspNetCore.Authentication.Abstractions` (`AuthenticationHandler<TOptions>`, framework-provided, not a NuGet dependency) |
| Constant-time key comparison *(WO-057)* | `01.Core/SharedKernel.Cryptography` existing primitives — never a bespoke comparison routine |
| Structured security-audit logging *(WO-057)* | `Microsoft.Extensions.Logging.Abstractions` `[LoggerMessage]` source generator (already a transitive ASP.NET Core dependency in `.Oidc`/`.ApiKey`; never added to `.Abstractions`) |
| DPoP proof JWT parsing/verification *(WO-058)* | `Microsoft.IdentityModel.JsonWebTokens`/`Microsoft.IdentityModel.Tokens` — already a transitive dependency of `Microsoft.AspNetCore.Authentication.JwtBearer`/`Microsoft.Identity.Web` in `.Oidc`; no new top-level NuGet reference. `JsonWebKey.ComputeJwkThumbprint()` (RFC 7638) and `Base64UrlEncoder` handle `jkt` computation/encoding — no hand-rolled crypto |
| Mutual-TLS client-certificate authentication *(WO-058)* | `Microsoft.AspNetCore.Authentication.Certificate` — **CORRECTED at Core-phase implementation time**: this is a standalone NuGet `PackageReference` (pinned `10.0.0`, matching `.Oidc`'s `JwtBearer` pin), NOT part of the `Microsoft.AspNetCore.App` shared framework — verified directly against the installed shared-framework directories on disk (no `Certificate.dll` present in any of them). The original "framework-provided handler" framing was wrong; `SharedKernel.Security.Mtls.csproj` carries both the `PackageReference` and a `FrameworkReference` to `Microsoft.AspNetCore.App` (for `Http`/`DependencyInjection`/`Logging.Abstractions` only) |
| Certificate/JWK thumbprinting *(WO-058)* | `01.Core/SharedKernel.Cryptography`'s `IContentHasher`/SHA-256 primitives — never a bespoke hashing routine, mirrors the constant-time-comparison precedent from `.ApiKey`. `SharedKernel.Security.Mtls` has no `Microsoft.IdentityModel` reference to source a base64url encoder from (unlike `.Oidc`), so it uses a small hand-rolled one (`Convert.ToBase64String(...).TrimEnd('=').Replace('+','-').Replace('/','_')`) |
| Token revocation/introspection transport (RFC 7662) *(WO-058)* | Consumer-supplied — this package ships only the `ITokenRevocationCheck` seam, never an HTTP client or store of its own |
| TOTP/HOTP algorithm, Base32, provisioning URI, replay-verified challenge *(WO-069, P-452, SHIPPED)* | Delegated entirely to `01.Core/SharedKernel.Cryptography`'s `ITotpGenerator`/`TotpVerifier`/`Base32`/`TotpProvisioningUri`/`RecoveryCodeGenerator` (P-451) — `SharedKernel.Security.Totp` never reimplements any RFC 6238/4226 primitive, mirroring the "algorithm lives in `01.Core`, ASP.NET Core-facing wiring lives here" split already ratified for this WO |
| Step-up claim propagation *(WO-069, P-452, SHIPPED)* | `Microsoft.AspNetCore.Authentication.IClaimsTransformation` — genuinely framework-provided via a bare `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, CONFIRMED by listing the installed shared-framework directory (`Microsoft.AspNetCore.Authentication.Abstractions.dll` is present) before writing the csproj — no separate `PackageReference` was needed this time, UNLIKE the `.Mtls`/`Microsoft.AspNetCore.Authentication.Certificate` correction (WO-058), where the identical "framework-provided" assumption turned out wrong. Two data points now exist in this domain for the same category of claim (once wrong, once right) — the lesson is to always verify directly against the installed SDK's shared-framework directory, never assume either way regardless of which way a prior case went. Scheme-agnostic, runs during `AuthenticateAsync` before any `IUserContext` DI factory first resolves |
| TOTP challenge/step-up backing store *(WO-069, P-452, SHIPPED)* | Consumer-supplied — this package ships only the `ITotpChallengeStore` seam, never a Redis/database client or store of its own, mirroring `IDpopProofReplayCache`/`ITokenRevocationCheck`'s precedent exactly |

---

## Interface Contracts

### `SharedKernel.Security.Abstractions` — public surface

#### User context (`Abstractions/`)

```text
IUserContext
    .UserId                                                     → Guid
    .Email                                                      → string?
    .Username                                                   → string?
    .Roles                                                      → IReadOnlyCollection<string>
    .Permissions                                                → IReadOnlyCollection<string>              [WO-057, P-368]
    .Claims                                                     → IReadOnlyDictionary<string, string>
    .IsAuthenticated                                            → bool
    .IdentityKind                                               → IdentityKind                             [WO-057, P-367]
    .HasRole(string role)                                       → bool
        Returns true when Roles contains the given role (case-insensitive).
    .HasPermission(string permission)                           → bool                                     [WO-057, P-368]
        Returns true when Permissions contains the given permission (case-insensitive). Mirrors HasRole exactly.
        Use HasRole for coarse role-membership checks; use HasPermission for fine-grained per-action/per-resource
        checks (e.g. "orders:write") sourced from the OAuth2 scope claim. Both are legitimate, not mutually exclusive.
    .AuthenticationMethods                                      → IReadOnlyCollection<string>               [WO-058, P-375]
        The OIDC "amr" (Authentication Method Reference) values for the current session — e.g. "pwd", "otp",
        "mfa", "hwk". Empty when the identity source carries no authentication-context concept (API key, mTLS,
        Anonymous, System).
    .AuthContextClassReference                                  → string?                                   [WO-058, P-375]
        The OIDC "acr" (Authentication Context Class Reference) assurance-level claim. Null when absent or
        not applicable to the identity source.
    .AuthTime                                                    → DateTimeOffset?                           [WO-058, P-375]
        The UTC instant the authentication event actually occurred (the OIDC "auth_time" claim) — distinct
        from token-issued-at. Null when absent, malformed, or not applicable to the identity source.
    .IsSenderConstrained                                         → bool                                      [WO-058, P-376]
        True only when the current request's access token was validated as DPoP-bound (RFC 9449) for THIS
        request — a fresh, correctly-signed proof matching the token's cnf.jkt was presented. False for every
        other identity source, including a valid-but-unconstrained bearer token.
    .WasAuthenticatedWith(string method)                        → bool                                      [WO-058, P-375]
        Returns true when AuthenticationMethods contains the given method (case-insensitive). Mirrors HasRole's
        shape exactly. Lets calling code ask "was MFA actually used for this session" without hand-parsing amr.
    .IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) → bool                                 [WO-058, P-375]
        Returns true when AuthTime is present AND (now - AuthTime.Value) <= maxAge; false when AuthTime is null.
        The caller supplies `now` explicitly (from its own injected IClock/TimeProvider) — this member NEVER
        calls DateTimeOffset.UtcNow internally, per the platform's injectable-time convention. Use this to gate
        step-up-required operations (wire transfers, limit changes, credential rotation) on freshly-verified
        authentication rather than a token's raw expiry.
    NOTE: IUserContext is request-scoped. UserId must never be Guid.Empty when IdentityKind == User.
          IdentityKind.ServicePrincipal and IdentityKind.System legitimately carry IsAuthenticated = true with
          UserId == Guid.Empty — this is the corrected invariant as of WO-057 (P-367); it replaced the old rule
          "UserId is never Guid.Empty when IsAuthenticated == true", which was too strong once service-principal/
          system identities were recognized as first-class authenticated states.
          Inject IUserContext only in application-layer code — never in domain or infrastructure repositories.

IdentityKind  (enum)                                                                                        [WO-057, P-367]
    .Anonymous        = 0   — default(IdentityKind); no valid authenticated principal
    .User                   — a human subject (a parseable Guid subject/"sub" claim was present)
    .ServicePrincipal       — a valid, authenticated token with no human subject (client-credentials/M2M shape)
    .System                 — a trusted non-HTTP execution context (SystemUserContext) — see below
    NOTE: Anonymous is the enum's default value so an uninitialized/default IdentityKind fails safe.
          Detection of ServicePrincipal must be IdP-agnostic — never hardcode one vendor's claim names
          (e.g. Entra's idtyp/azp) as the sole mechanism.

AnonymousUserContext  (sealed class, implements IUserContext)
    — sentinel for unauthenticated requests; all string properties null; Roles/Permissions/Claims empty collections
    — UserId = Guid.Empty; IsAuthenticated = false; IdentityKind = IdentityKind.Anonymous
    — HasRole/HasPermission always return false
    — AuthenticationMethods empty; AuthContextClassReference null; AuthTime null; IsSenderConstrained false;
      WasAuthenticatedWith/IsAuthenticationFresherThan always return false                                  [WO-058, P-375/P-376]
    NOTE: Registered as fallback so IUserContext is always resolvable regardless of auth state.
          Infrastructure and application code must check IsAuthenticated before using UserId.

SystemUserContext  (sealed class, singleton instance, implements IUserContext)                              [WO-057, P-369]
    — sentinel for trusted, non-HTTP execution authority: a Temporal activity, a MassTransit consumer,
      a Hangfire job, a startup seeder — as opposed to a rejected or genuinely unauthenticated caller
    — IdentityKind = IdentityKind.System; IsAuthenticated = true; UserId = Guid.Empty
    — Roles/Permissions/Claims empty; HasRole/HasPermission always return false
      (a system context asserts trust by IdentityKind, not by inheriting role/permission membership it was
      never granted — a consuming service's own authorization bridge decides what IdentityKind.System means)
    — AuthenticationMethods empty; AuthContextClassReference null; AuthTime null; IsSenderConstrained false —
      a background-execution context has no HTTP-derived authentication-context signal to report            [WO-058, P-375/P-376]
    NOTE: A peer of AnonymousUserContext, not a replacement for it. Ships with NO DI wiring in this package —
          a background-execution composition root (a 17.Workflows activity host, a 07.Messaging consumer host,
          a hosted-service startup path) is responsible for registering it in place of the HTTP-derived
          IUserContext factory. This domain does not itself decide whether IdentityKind.System bypasses
          authorization — that is the consuming service's own IAuthorizationContext bridge's explicit choice.
```

#### Tenant provider (`Abstractions/`)

```text
ITenantProvider
    .TenantId                                                   → Guid
    NOTE: ITenantProvider is request-scoped. Returns Guid.Empty when no tenant claim is present.
          Application layer resolves TenantId and passes it as a Guid primitive to domain constructors.
          Domain code (03.Domain) must never reference ITenantProvider — it receives tenantId as a primitive.
          Infrastructure code (06.Persistence TenantedDbContext) uses ITenantProvider via DI to apply
          global tenant filters — this is the only infrastructure coupling allowed.
```

#### Well-known claim type constants (`Claims/`)

```text
SecurityClaimTypes  (static class — well-known claim type string constants)
    SecurityClaimTypes.UserId                                   → string  ("sub" — unaffected by WO-057; already a short name)
    SecurityClaimTypes.TenantId                                 → string  ("tenant_id" — unaffected by WO-057; already a short name)
    SecurityClaimTypes.Email                                    → string  (legacy reference constant; maps to ClaimTypes.Email)
    SecurityClaimTypes.Role                                     → string  (legacy reference constant; maps to ClaimTypes.Role)
    SecurityClaimTypes.AuthenticationMethod                     → string  ("amr" — SHIPPED, WO-069/P-452)
    NOTE: As of WO-057 (P-366), Email/Role are LEGACY-SHAPE REFERENCE CONSTANTS ONLY — the active lookup key
          OidcUserContext uses at runtime is SecurityOptions.ClaimMapping (SharedKernel.Security.Oidc), whose
          short-name defaults ("email"/"name"/"roles"/"scope") match the unmapped claim shape every standards-
          conformant OIDC issuer emits by default against a .NET 8+ JwtBearerHandler (MapInboundClaims = false).
          UserId/TenantId are unaffected — "sub" and "tenant_id" are already short names.
          Never use raw string literals for any of these — always the named constant or the configured option.
    NOTE (WO-069, P-452, SHIPPED): AuthenticationMethod ("amr") is a NEW shared constant closing a
          pre-existing duplicate-literal gap — .Oidc's ClaimMappingOptions.AmrClaimType already defaults to
          "amr" as an independently-typed literal (shipped WO-058); this constant becomes that default's single
          source of truth, and SharedKernel.Security.Totp's TotpStepUpOptions.AmrClaimType also defaults to it,
          so the claim type SharedKernel.Security.Totp's IClaimsTransformation stamps and the claim type
          OidcUserContext's defensive amr reader consumes can never silently drift apart. Purely additive —
          no existing member changes.
```

---

### `SharedKernel.Security.Oidc` — public surface

#### Claims-to-context mapper (`Mapping/`)

```text
OidcUserContext  (sealed class, implements IUserContext)
    — ctor(ClaimsPrincipal, ClaimMappingOptions, ILogger<OidcUserContext>? = null)                          [ctor updated WO-057]
      the DI factory resolves ClaimMappingOptions from IOptions<SecurityOptions>.Value.ClaimMapping; the
      optional ILogger (default null, guarded at every call site) carries structured security-audit
      events (P-371) and is omittable for direct/non-DI construction (e.g. unit tests)
    — UserId resolved from SecurityClaimTypes.UserId ("sub") claim; parsed to Guid
    — Email/Username resolved from SecurityOptions.ClaimMapping.EmailClaimType/NameClaimType                [WO-057, P-366]
      (short-name defaults "email"/"name" — NOT the legacy SecurityClaimTypes.Email/ClaimTypes.Name lookup)
    — Roles resolved defensively from SecurityOptions.ClaimMapping.RoleClaimType — handles BOTH one Claim
      per role AND a single claim whose value is a JSON array of roles, never throws on either shape        [WO-057, P-366]
    — Permissions resolved from SecurityOptions.ClaimMapping.PermissionClaimType ("scope"/"scp"), a single
      space-delimited claim value split into individual entries                                             [WO-057, P-368]
    — Claims resolved as IReadOnlyDictionary<string, string> (first value per claim type)
    — IsAuthenticated delegates to ClaimsPrincipal.Identity?.IsAuthenticated
    — AuthenticationMethods resolved defensively from SecurityOptions.ClaimMapping.AmrClaimType ("amr") —
      handles BOTH one Claim per method AND a single claim whose value is space-delimited, never throws on
      either shape (mirrors the P-366 role-claim reader exactly)                                             [WO-058, P-375]
    — AuthContextClassReference resolved from SecurityOptions.ClaimMapping.AcrClaimType ("acr"), first value  [WO-058, P-375]
    — AuthTime resolved from SecurityOptions.ClaimMapping.AuthTimeClaimType ("auth_time") — a NumericDate
      (Unix seconds) per the OIDC spec, parsed via DateTimeOffset.FromUnixTimeSeconds; absent or unparseable
      → null, never throws                                                                                   [WO-058, P-375]
    — IsSenderConstrained set true only after DpopProofValidator confirms a fresh, correctly-bound DPoP proof
      for the current request; false for an ordinary bearer token even when DPoP is enabled host-wide         [WO-058, P-376]
    — IdentityKind resolution (WO-057, P-367):
        • User             — ClaimsPrincipal.Identity.IsAuthenticated == true AND a parseable Guid human
                              subject ("sub") claim is present
        • ServicePrincipal — ClaimsPrincipal.Identity.IsAuthenticated == true but NO parseable human subject
                              (the standard client-credentials/M2M token shape); IsAuthenticated stays true,
                              UserId stays Guid.Empty — this is NOT the same branch as a rejected token
        • Anonymous        — ClaimsPrincipal.Identity.IsAuthenticated != true (the only case that still
                              forces IsAuthenticated = false)
      Detection is IdP-agnostic — it never keys off one vendor's proprietary claim names.

OidcTenantProvider  (sealed class, implements ITenantProvider)
    — ctor(ClaimsPrincipal, ILogger<OidcTenantProvider>? = null)                                            [ctor updated WO-057]
    — resolves TenantId from SecurityClaimTypes.TenantId claim on ClaimsPrincipal
    — returns Guid.Empty when claim is absent or cannot be parsed — logs TenantClaimResolutionFailed
      (Warning, EventId 12101) only when the underlying principal IS authenticated (an unauthenticated
      request resolving to Guid.Empty is expected, not a signal worth a log line)
    — the "tenant_id" claim name itself is unaffected by WO-057 — already a short name
```

#### DI registration (`Extensions/`)

```text
AddSharedKernelSecurity(IConfiguration config)  →  IServiceCollection
    Registers:
      — JWT Bearer middleware with options bound from config section "Security:Jwt"
      — TokenValidationParameters.NameClaimType/RoleClaimType from SecurityOptions.ClaimMapping           [WO-057, P-366]
      — IUserContext as Scoped → OidcUserContext (resolved from IHttpContextAccessor + IOptions<SecurityOptions>)
      — ITenantProvider as Scoped → OidcTenantProvider (resolved from IHttpContextAccessor)
      — AnonymousUserContext registered as fallback when no HTTP context is present
    NOTE: Call after AddAuthentication() in the host startup pipeline.

AddAzureB2CAuthentication(IConfiguration config)  →  IServiceCollection
    Wraps AddSharedKernelSecurity with Azure B2C / Entra External ID specific Authority/Audience
    bound from config section "AzureAdB2C". Uses Microsoft.Identity.Web under the hood.
    Also configures TokenValidationParameters.NameClaimType/RoleClaimType from SecurityOptions.ClaimMapping,
    identically to AddSharedKernelSecurity — no divergence between the two entry points.               [WO-057, P-366]
    NOTE: Use this instead of AddSharedKernelSecurity when the identity provider is Azure B2C.

Both AddSharedKernelSecurity and AddAzureB2CAuthentication return a SecurityAuthenticationBuilder        [WO-058, P-376/P-379]
(rather than a bare IServiceCollection) enabling optional, chainable opt-ins with NO change in behavior
for callers that ignore the return value:

SecurityAuthenticationBuilder  (sealed class)                                                            [WO-058, P-376/P-379]
    .RequireDpop<TReplayCache>()          → SecurityAuthenticationBuilder   where TReplayCache : class, IDpopProofReplayCache
        Opt-in DPoP (RFC 9449) sender-constrained access-token validation. Disabled by default — adds
        no behavior until called. See "DPoP (RFC 9449)" below.
    .WithRevocationCheck<TCheck>()        → SecurityAuthenticationBuilder   where TCheck : class, ITokenRevocationCheck
        Opt-in post-validation revocation/introspection check. Disabled by default. See "Token Revocation"
        below.

SecurityOptions  (sealed class — Options-pattern, bound via AddValidatedOptions)
    .Jwt.Authority                                              → string  (required)
    .Jwt.Audience                                               → string  (required)
    .Jwt.ValidateLifetime                                       → bool    (default: true)
    .Jwt.ClockSkewSeconds                                       → int     (default: 30)
    .Jwt.ValidAlgorithms                                        → IReadOnlyCollection<string>  (default: ["PS256", "ES256"])  [WO-060, P-387]
    .ClaimMapping.EmailClaimType                                → string  (default: "email")            [WO-057, P-366]
    .ClaimMapping.NameClaimType                                 → string  (default: "name")              [WO-057, P-366]
    .ClaimMapping.RoleClaimType                                 → string  (default: "roles")             [WO-057, P-366]
    .ClaimMapping.PermissionClaimType                           → string  (default: "scope")             [WO-057, P-368]
    .ClaimMapping.AmrClaimType                                  → string  (default: "amr")               [WO-058, P-375]
    .ClaimMapping.AcrClaimType                                  → string  (default: "acr")               [WO-058, P-375]
    .ClaimMapping.AuthTimeClaimType                             → string  (default: "auth_time")         [WO-058, P-375]
    NOTE: Defaults match modern short-name OIDC claim conventions — the unmapped shape every standards-
          conformant issuer (Entra ID v2.0, Auth0, Okta, Keycloak) emits by default against a .NET 8+
          JwtBearerHandler (MapInboundClaims = false). NOT the legacy ClaimTypes.* long-form URIs the
          pre-WO-057 defaults hardcoded. A consuming service on an IdP still emitting the legacy shape
          (or one that has opted into MapInboundClaims = true) overrides these to the ClaimTypes.*
          equivalents. Both AddSharedKernelSecurity and AddAzureB2CAuthentication source
          TokenValidationParameters.NameClaimType/RoleClaimType from these SAME values — never divergent
          from what OidcUserContext reads. Amr/Acr/AuthTime are standard OIDC claim names ("amr"/"acr"/
          "auth_time") and are already short-form — no legacy-vs-short-name distinction applies to them.
    NOTE (WO-060, P-387): Jwt.ValidAlgorithms is wired into TokenValidationParameters.ValidAlgorithms by
          BOTH AddSharedKernelSecurity and AddAzureB2CAuthentication, restricting accepted JWS signing
          algorithms to the FAPI 2.0 Security Profile baseline (PS256/ES256) as a defense against
          algorithm-confusion/downgrade attacks — including a crafted "alg": "none" token, rejected
          before any signature or claim evaluation proceeds. THIS IS A DELIBERATE BREAKING DEFAULT: many
          real-world IdPs (Entra ID, Auth0, Okta) sign access tokens with RS256 by default, which is NOT
          in the default allowlist — a consuming service on such an IdP MUST explicitly widen
          Jwt.ValidAlgorithms to include "RS256" (or its own IdP's actual signing algorithm) after
          upgrading, or every previously-valid token will be rejected. This is intentional: FAPI 2.0
          conformance requires restricting to strong asymmetric algorithms as a baseline control, and a
          domain whose WO-058 pass was explicitly scoped "fintech/FAPI 2.0-grade" should not ship a
          silently permissive default. SHIPPED (WO-060, C-40) — wired into TokenValidationParameters by
          both AddSharedKernelSecurity and AddAzureB2CAuthentication; an out-of-allowlist rejection is
          audit-logged via SecurityLogEvents.JwtSigningAlgorithmRejected (EventId 12104, C-42).
```

#### DPoP sender-constrained tokens (`Dpop/`) *(WO-058, P-376)*

RFC 9449. Opt-in via `SecurityAuthenticationBuilder.RequireDpop<TReplayCache>()`. Disabled by default —
zero behavior change for a non-opted-in consumer.

```text
IDpopProofReplayCache                                                                                    [WO-058, P-376]
    .TryConsumeAsync(string jti, DateTimeOffset proofExpiresAt, CancellationToken ct) → Task<bool>
    NOTE: The sole consumer-supplied extensibility point. Returns false when jti has already been seen
          (replay — reject the request); true on first use (record it, whatever the backing store).
          This package NEVER references 02.Caching or dictates a storage mechanism — mirrors
          IApiKeyValidator's "never dictates storage" precedent exactly.

DpopProofValidator  (internal)                                                                            [WO-058, P-376]
    — runs inside JwtBearerEvents.OnTokenValidated, AFTER standard signature/issuer/audience/lifetime
      validation succeeds, and ONLY when RequireDpop<TReplayCache>() was called
    — REJECTS the proof JWT outright if its own signing algorithm is not in DpopOptions.ValidAlgorithms
      (default ["PS256", "ES256"]) — checked BEFORE embedded-jwk signature verification proceeds, so an
      alg-confusion/downgrade attempt (including a crafted "alg": "none") never reaches signature
      evaluation at all                                                                    [WO-060, P-387, SHIPPED]
    — parses the request's DPoP header as a "typ: dpop+jwt" proof JWT; verifies its embedded jwk signature
    — validates htm (HTTP method) and htu (HTTP URI, without query/fragment) match the current request
    — validates iat falls within a configurable freshness window (default: 60 seconds)
    — computes jkt = base64url(SHA-256(canonical JWK)) and compares against the access token's cnf.jkt
      confirmation claim — a mismatch, malformed proof, or absent header all reject the request
    — computes ath = base64url(SHA-256(raw bearer access token)) and compares CONSTANT-TIME against the
      proof JWT's own "ath" claim (RFC 9449 §4.3) — a fifth binding, alongside htm/htu/iat/jkt/jti, that
      cryptographically ties the DPoP proof to the SPECIFIC access token it accompanies (distinct from jkt,
      which ties the proof to the client's DPoP key, not to any one token); a missing, malformed, or
      mismatched ath rejects through the SAME DpopProofRejected (EventId 12102, failureReason
      "AthMismatch") path as every other binding failure — no new rejection surface   [WO-060, P-385, SHIPPED]
    — on success, calls IDpopProofReplayCache.TryConsumeAsync(jti, ...) — false rejects as replay
    — on any failure, context.Fail(...) — the request never reaches application code; IUserContext
      resolves IsSenderConstrained = false only because authentication itself failed, not as a partial state
    NOTE: Chosen over mTLS sender-constraining (SharedKernel.Security.Mtls, P-377) as this domain's
          PRIMARY recommended sender-constraining mechanism — no PKI/certificate-provisioning burden on
          each consuming team, matching the platform's "developer friendly" requirement for this domain.
          Both remain available; a consumer may require either or both depending on regulatory regime.
    NOTE (WO-060, P-385): Before this fix, ath was never read anywhere in this type — a real, previously-
          invisible RFC 9449 §4.3 compliance gap in already-shipped, 94/94-tests-green production code.
          SHIPPED (C-38/C-41) — ath is computed via System.Security.Cryptography.CryptographicOperations.
          FixedTimeEquals (constant-time), and the algorithm allowlist check runs BEFORE any other DPoP
          binding, immediately after the proof JWT is parsed.

DpopOptions  (sealed class)
    .ValidAlgorithms                                            → IReadOnlyCollection<string>       [WO-060, P-387, SHIPPED]
        Default ["PS256", "ES256"] — the FAPI 2.0 Security Profile baseline. Independently configurable
        from SecurityOptions.Jwt.ValidAlgorithms because a DPoP proof's signing key is CLIENT-generated
        and need not use the same algorithm as the IdP's own token-signing key.
```

#### Token revocation / introspection check (`Revocation/`) *(WO-058, P-379)*

RFC 7662-shaped. Opt-in via `SecurityAuthenticationBuilder.WithRevocationCheck<TCheck>()`. Disabled by
default — this is the one capability in this domain with a genuine per-request latency/availability cost,
so it must never become a silent default.

```text
ITokenRevocationCheck                                                                                     [WO-058, P-379]
    .IsRevokedAsync(string tokenIdentifier, CancellationToken ct)  →  Task<bool>
    NOTE: tokenIdentifier is the RAW bearer token string — supports both a jti-keyed revocation-list
          lookup and an RFC 7662 introspection HTTP call to the IdP (introspection requires the raw
          token). The sole extensibility point; this package never dictates Redis/database/introspection-
          endpoint specifics — the consumer wires the actual store/call at its own composition root
          (e.g. via 02.Caching), never a direct 12.Security → 02.Caching reference.

Execution/failure contract:                                                                               [WO-058, P-379]
    — Runs ONLY AFTER standard signature/issuer/audience/lifetime validation succeeds — a structurally
      invalid token is rejected regardless of whether the revocation check is configured or reachable.
    — A revoked token (IsRevokedAsync returns true) rejects with the SAME generic authentication-failure
      shape as an expired/malformed token — no distinct, information-leaking error is ever surfaced.
    — An unavailable or throwing revocation-check dependency FAILS CLOSED (rejects) — never silently
      fails open. A consumer accepting this capability accepts its latency/availability trade-off.

Caching seam (opt-in, mitigates the per-request cost acknowledged above)                    [WO-060, P-388, SHIPPED]

IRevocationCheckCache                                                                                    [WO-060, P-388]
    .TryGetAsync(string tokenIdentifier, CancellationToken ct)         → Task<bool?>
    .SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct) → Task
    NOTE: The sole consumer-supplied cache extensibility point — mirrors IDpopProofReplayCache's
          "never dictates storage" precedent exactly. This package NEVER references 02.Caching; the
          consumer bridges to their real cache (IMemoryCache, 02.Caching.Abstractions, Redis, ...) at
          their own composition root, mirroring the IUnitOfWork/ITenantContextAccessor local-seam
          bridge pattern already established in 05.Application/07.Messaging.

RevocationCheckCacheOptions                                                                              [WO-060, P-388]
    .NotRevokedTtl                                                     → TimeSpan  (default: 30 seconds)
    .RevokedTtl                                                        → TimeSpan  (default: 5 seconds)
    NOTE: RevokedTtl is DELIBERATELY SHORT — a genuine revocation must never be masked by a stale
          "not revoked" cache entry for long. A positive "not revoked" result may be cached more
          generously since a false-negative there only costs one extra introspection call, not a
          security gap.

CachingTokenRevocationCheck  (sealed, decorates ITokenRevocationCheck)                                    [WO-060, P-388]
    — checks IRevocationCheckCache first; on a cache hit, returns the cached bool without invoking the
      inner ITokenRevocationCheck at all
    — on a cache miss, calls through to the inner ITokenRevocationCheck and populates the cache with
      RevokedTtl or NotRevokedTtl according to the outcome
    — inherits the SAME fail-closed contract as the base check — a cache-lookup failure falls through
      to the inner check, never silently treats an unreadable cache as "not revoked"

SecurityAuthenticationBuilder.WithRevocationCheckCaching<TCache>()  → SecurityAuthenticationBuilder        [WO-060, P-388]
    where TCache : class, IRevocationCheckCache
    MUST be chained AFTER .WithRevocationCheck<TCheck>() in the same fluent call — throws
    InvalidOperationException at registration time if called first (there is nothing to cache-wrap yet).
    Opt-in, disabled by default, matching the base revocation check's own opt-in posture — a consumer
    who calls only .WithRevocationCheck<TCheck>() gets today's uncached behavior unchanged.
```

**Status note:** every member above tagged `[WO-060, P-388, SHIPPED]` is implemented — `CachingTokenRevocationCheck.IsRevokedAsync` does a real cache-first lookup (falling through to the inner `ITokenRevocationCheck` on a miss OR a throwing/failing cache read — a broken cache must never itself become a fail-open or fail-closed surface distinct from the inner check's own contract) and populates the cache with `RevokedTtl`/`NotRevokedTtl` per outcome via a best-effort, non-fatal write. `SecurityAuthenticationBuilder.WithRevocationCheckCaching<TCache>()` throws `InvalidOperationException` when called before `.WithRevocationCheck<TCheck>()`, otherwise decorates the already-registered `ITokenRevocationCheck` via the same `ServiceDescriptor`-capture technique `.ApiKey`/`.Mtls` already use for `IUserContext` (`ActivatorUtilities.CreateInstance` builds the inner instance so its own constructor DI still resolves) (C-43/C-44).

---

### `SharedKernel.Security.ApiKey` — public surface *(WO-057, Core-implemented)*

Pre-shared-key / machine-client authentication. A sibling provider to `.Oidc`, never a dependent of it — API-key auth is not an OIDC concept.

#### Key validation extensibility point (`Validation/`)

```text
IApiKeyValidator
    .ValidateAsync(string presentedKey, CancellationToken ct)   → Task<ApiKeyValidationResult>
    NOTE: The sole consumer-supplied extensibility point. This package never dictates a storage mechanism
          for keys (configuration, database, secret store — the consuming service decides). The actual
          presented-key-vs-stored-secret comparison happens inside the consumer's own implementation,
          entirely opaque to this package.

ApiKeyValidationResult
    .IsValid                                                    → bool
    static .Invalid                                              → ApiKeyValidationResult (shared singleton failure)
    static .Valid(string? clientId = null, IReadOnlyCollection<string>? roles = null,
                  IReadOnlyCollection<string>? permissions = null) → ApiKeyValidationResult
    .ClientId                                                   → string?
    .Roles / .Permissions                                       → IReadOnlyCollection<string>? (optional passthrough)

ApiKeyUserContext  (sealed class, implements IUserContext)
    — maps the ClaimsPrincipal produced by ApiKeyAuthenticationHandler on a successful match
    — IdentityKind.ServicePrincipal, IsAuthenticated = true, UserId = Guid.Empty always
    — Username carries ApiKeyValidationResult.ClientId (via a ClaimTypes.NameIdentifier claim)
    — Roles/Permissions round-trip via SecurityClaimTypes.Role / an internal "permission" claim type
    — AuthenticationMethods empty; AuthContextClassReference null; AuthTime null; IsSenderConstrained
      false — no authentication-context concept for a pre-shared key                                       [WO-058, P-375/P-376]

ApiKeyAuthenticationHandler  (AuthenticationHandler<ApiKeyAuthenticationOptions>)
    — reads the presented key from the configured header/query parameter
    — CONSTANT-TIME COMPARISON SCOPE: when BOTH the header and the configured query parameter are
      present on the same request, the handler compares the two PRESENTED values against each other
      (never a stored secret — this package holds none) via ConstantTimeKeyComparer, built on
      SharedKernel.Cryptography's IHmacSigner (HMAC-based equality — never string.Equals/==/
      SequenceEqual) — a mismatch fails authentication as a possible credential-confusion attack,
      logged as "AmbiguousCredential". The actual secret-vs-stored-value comparison is entirely
      IApiKeyValidator's own concern, outside this package's visibility.
    — on a successful IApiKeyValidator match, produces an authenticated ClaimsPrincipal mappable to
      ApiKeyUserContext (IdentityKind.ServicePrincipal, IsAuthenticated = true)
    — on an invalid, absent, or ambiguous key, authentication fails — never falls through to an
      authenticated context

ApiKeyRotationComparer.AnyMatch(string presented, IReadOnlyList<string> candidates)  → bool  (public)       [WO-060, P-389, SHIPPED]
    NOTE: A NEW PUBLIC type, distinct from the existing INTERNAL ConstantTimeKeyComparer (which stays
          internal — it is scoped to ApiKeyAuthenticationHandler's own header-vs-query ambiguity check
          and is not meant for consumer use). ApiKeyRotationComparer is the sanctioned public entry
          point for an IApiKeyValidator implementation — necessarily authored in the CONSUMING service's
          own assembly, which has no access to this package's internal types — that needs to validate
          against MORE THAN ONE currently-active key per client (a rotation grace window: issue a new
          key, keep the old key valid for a bounded overlap period, then revoke it). Built on the SAME
          IHmacSigner-based constant-time technique as the internal comparer. ALWAYS iterates and
          compares EVERY candidate — never short-circuits on an early match — so elapsed comparison time
          never correlates with which key, or how many keys, were checked. This package still never
          dictates storage: the caller supplies the candidate key list from wherever it stores active
          keys per client (a two-row table, a JSON array column, ...); AnyMatch only closes the "every
          consuming team reinvents this and likely gets the timing side-channel wrong" gap.
    SHIPPED (C-45) — builds its own internal HmacSha256Signer (zero constructor dependencies, so this
    static helper needs no DI container), iterates every candidate via ConstantTimeKeyComparer.AreEqual
    and accumulates with `matched |= isMatch` — never short-circuits. A worked, non-production
    IApiKeyValidator sample demonstrating this pattern ships at
    Samples/RotationWindowApiKeyValidatorSample.cs (internal, doc-flagged as a recipe, C-46).
    SHIPPED (T-38, WO-060) — additionally gained an internal AnyMatch(string, IReadOnlyList<string>,
    IHmacSigner) overload; the public overload above now delegates to it with a real HmacSha256Signer.
    Purely a testability seam (reachable from SharedKernel.Security.ApiKey.Tests via the package's
    existing InternalsVisibleTo grant) — the public contract, default behavior, and always-evaluate-
    every-candidate guarantee are all unchanged. Added because the public-only signature offered no way
    for a test to prove "never short-circuits" without either this seam or a flaky timing measurement.
```

#### DI registration (`ApiKey/Extensions/`)

```text
AddApiKeyAuthentication<TValidator>(Action<ApiKeyAuthenticationOptions>? configureOptions = null,
                                     string fallbackAuthenticationScheme = "Bearer")  →  IServiceCollection
    where TValidator : class, IApiKeyValidator
    Registers the API-key authentication scheme and composes it alongside an already-registered JWT
    Bearer scheme via a policy/forwarding scheme (ApiKeyAuthenticationOptions.CompositeSchemeName) — a
    host can accept EITHER credential type on the same set of endpoints without one scheme silently
    shadowing the other. fallbackAuthenticationScheme exists because this package cannot reference
    JwtBearerDefaults.AuthenticationScheme without taking a Microsoft.AspNetCore.Authentication.JwtBearer
    NuGet dependency it deliberately excludes — override it only if AddSharedKernelSecurity registered
    the Bearer scheme under a non-default name.
    IUserContext COMPOSITION MECHANISM: this package cannot reference SharedKernel.Security.Oidc (sibling
    providers never reference each other), so it cannot construct OidcUserContext directly. Instead it
    captures the ServiceDescriptor of whatever IUserContext factory is already registered (typically by
    AddSharedKernelSecurity/AddAzureB2CAuthentication, called first) BEFORE registering its own scheme-
    aware scoped factory: the new factory returns ApiKeyUserContext when the current HttpContext.User's
    ClaimsIdentity.AuthenticationType equals ApiKeyAuthenticationOptions.DefaultScheme, otherwise invokes
    the captured previous factory delegate (or AnonymousUserContext.Instance if none was captured) — a
    decorator built purely from ServiceDescriptor introspection, not a type reference.
    NOTE: This scheme is for pre-shared-key/machine-client scenarios only. It does not attempt key
          issuance, rotation, or storage — those remain the consuming service's own concern.
```

---

### `SharedKernel.Security.Mtls` — public surface *(WO-058, P-377)*

Mutual-TLS client-certificate authentication for regulated Open Banking/PSD2-style external APIs (QWAC/QSEAL
certificate-based mTLS demanded regardless of whatever internal service-mesh mTLS already authenticates
pod-to-pod traffic). A fourth sibling provider to `.Oidc`/`.ApiKey`, never a dependent of either — a
certificate-based credential scheme is not an OIDC concept, and sibling provider packages never reference
one another.

#### Certificate validation extensibility point (`Validation/`)

```text
IMtlsCertificateValidator                                                                                  [WO-058, P-377]
    .ValidateAsync(X509Certificate2 certificate, CancellationToken ct)  →  Task<MtlsValidationResult>
    NOTE: The sole consumer-supplied extensibility point — mirrors IApiKeyValidator exactly. This package
          performs NO CA/chain/revocation (CRL/OCSP) validation of its own beyond what is delegated to it;
          the consuming service decides trust-store and revocation policy entirely.

MtlsValidationResult                                                                                        [WO-058, P-377]
    .IsValid                                                        → bool
    static .Invalid                                                  → MtlsValidationResult (shared singleton failure)
    static .Valid(string? clientId = null, IReadOnlyCollection<string>? roles = null,
                  IReadOnlyCollection<string>? permissions = null) → MtlsValidationResult
    .ClientId                                                       → string?
    .Roles / .Permissions                                           → IReadOnlyCollection<string>? (optional passthrough)
    NOTE: Shape is a deliberate mirror of ApiKeyValidationResult — the same result contract for the same
          category of machine-client credential scheme.

MtlsUserContext  (sealed class, implements IUserContext)                                                    [WO-058, P-377]
    — maps a successful IMtlsCertificateValidator match to a ClaimsPrincipal
    — IdentityKind.ServicePrincipal, IsAuthenticated = true, UserId = Guid.Empty always — identical shape
      to ApiKeyUserContext
    — AuthenticationMethods/AuthContextClassReference/AuthTime/IsSenderConstrained: empty/null/null/false —
      no OIDC authentication-context concept for a certificate-based credential

MtlsAuthenticationOptions  (sealed class)
    .AllowedCertificateTypes                                    → CertificateTypes  (default: Chained)      [WO-060, P-386, SHIPPED — CORRECTED from All]
    .RevocationMode                                             → X509RevocationMode  (default: Offline)    [WO-060, P-386, SHIPPED — CORRECTED from NoCheck]
    NOTE (WO-060, P-386): Shipped Core-phase code (WO-058) defaulted these to CertificateTypes.All /
          X509RevocationMode.NoCheck — WEAKER than plain ASP.NET Core's own CertificateAuthenticationOptions
          framework defaults (Chained / Online). A consumer following this package's own documented common
          path (AddMtlsAuthentication<TValidator>(), no options override) ended up with a materially
          weaker starting posture than using the framework directly, with IMtlsCertificateValidator never
          even getting a chance to reject a self-signed certificate at the framework's own pre-filter
          stage. CORRECTED default: AllowedCertificateTypes = Chained (self-signed rejected unless
          explicitly opted into); RevocationMode = Offline, chosen over Online because this is a
          shared-kernel library shipped to services with varying network egress — Offline checks locally
          cached CRL data with no live network round-trip, materially stronger than NoCheck without
          imposing a hidden outbound-network dependency as the default for every consumer. A consumer with
          a legitimate need for the old permissive behavior (e.g. a private-PKI Open Banking QWAC/QSEAL
          trust chain that is not a standard public CA) opts in explicitly via configureOptions — both
          properties' XML docs state IN CAPITALS that overriding them weakens trust validation.
    SHIPPED (C-39) — both properties' XML docs carry the CAPS-worded warning; AddMtlsAuthentication's
    existing options-copy already forwards whatever AllowedCertificateTypes/RevocationMode the instance
    carries into certificateOptions, so no DI-wiring code change was needed beyond the corrected defaults.

Certificate-bound access token check (RFC 8705)                                                             [WO-058, P-377]
    — when BOTH a client certificate and a bearer token are presented on the same request, computes the
      SHA-256 thumbprint of the ACTUALLY-PRESENTED certificate (via 01.Core/SharedKernel.Cryptography)
      and compares constant-time against the bearer principal's cnf.x5t#S256 confirmation claim, read
      generically off ClaimsPrincipal — NEVER a SharedKernel.Security.Oidc type reference; the binding
      check is decoupled entirely through claims inspection, matching this domain's sibling-packages-
      never-reference-each-other rule
    — a mismatch rejects the request even when the underlying bearer token is otherwise fully valid
    NOTE: This is the FAPI 1.0-era sender-constraining mechanism several regional Open Banking regimes
          still mandate today, alongside or instead of DPoP (SharedKernel.Security.Oidc, P-376).
```

#### DI registration (`Mtls/Extensions/`)

```text
AddMtlsAuthentication<TValidator>(Action<MtlsAuthenticationOptions>? configureOptions = null)  →  IServiceCollection   [WO-058, P-377]
    where TValidator : class, IMtlsCertificateValidator
    Registers Microsoft.AspNetCore.Authentication.Certificate and composes it alongside an already-
    registered JWT Bearer/API-key scheme via the SAME ServiceDescriptor-capture decorator mechanism
    AddApiKeyAuthentication already established — this package cannot reference SharedKernel.Security.Oidc
    or SharedKernel.Security.ApiKey (sibling providers never reference each other), so it captures whatever
    IUserContext factory is already registered and layers its own certificate-aware resolution on top.
    NOTE: This scheme is for mutual-TLS client-certificate scenarios only. It does not perform certificate
          issuance, CA management, or revocation checking (CRL/OCSP) — those remain the consuming service's
          own concern, identical in spirit to .ApiKey's key-issuance/rotation/storage disclaimer.
```

---

### `SharedKernel.Security.Totp` — public surface *(WO-069, P-452, SHIPPED)*

Second-factor (TOTP) enrollment and challenge orchestration, plus the ASP.NET Core-facing step-up wiring
that makes a successful TOTP verification observable through `IUserContext.WasAuthenticatedWith`/
`AuthenticationMethods`. A fifth sibling provider to `.Oidc`/`.ApiKey`/`.Mtls`, never a dependent of any
of them — TOTP/HOTP is not an OIDC concept, and sibling provider packages never reference one another.
Unlike `.ApiKey`/`.Mtls`, this package does NOT authenticate a new primary identity — it AUGMENTS an
already-authenticated identity's authentication-method signal, layered on top of whatever primary scheme
(`.Oidc` only — see the scope-boundary Domain Invariant below) established that identity for the request.

#### Enrollment (`Enrollment/`)

```text
TotpEnrollment  (sealed record — SHIPPED, WO-069/P-452)
    .Secret                                                      → byte[]
    .SecretBase32                                                → string   (RFC 4648 Base32, unpadded — for manual entry)
    .ProvisioningUri                                             → Uri      (otpauth://totp/... "Key Uri Format")
    .RecoveryCodes                                                → IReadOnlyList<string>   (plaintext, shown once)

TotpEnrollmentService  (sealed class — SHIPPED, WO-069/P-452)
    GenerateEnrollment(string issuer, string accountName, int secretLengthBytes = 20,
                        int recoveryCodeCount = 10)               → TotpEnrollment
    — SYNCHRONOUS, side-effect-free: composes 01.Core/SharedKernel.Cryptography's ISecureRandomGenerator
      (secret bytes) + Base32.Encode + TotpProvisioningUri.Build + RecoveryCodeGenerator.GenerateCodes
      (all P-451). NEVER persists anything — the raw secret must be encrypted at rest via 01.Core's
      ISymmetricEncryptionService and each recovery code hashed at rest via 01.Core's IOneWayHasher,
      entirely the consuming service's own responsibility (documented as a README recipe, not shipped
      code — 12.Security has zero persistence coupling by design).
```

#### Challenge (`Challenge/`)

```text
ITotpChallengeStore  (SHIPPED, WO-069/P-452)
    RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default) → Task
    TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default)  → Task<DateTimeOffset?>
    NOTE: The sole consumer-supplied extensibility point — mirrors IDpopProofReplayCache's/
          ITokenRevocationCheck's "never dictates storage" precedent exactly (in-memory for single-instance
          dev, Redis/database-backed for production multi-replica). identityKey uses the SAME string shape
          01.Core's TotpVerifier/ITotpReplayGuard already key on — never a divergent Guid-keyed contract.
          This package NEVER references 02.Caching or 06.Persistence.

TotpChallengeService  (sealed class — SHIPPED, WO-069/P-452)
    VerifyAsync(Guid userId, byte[] secret, string code, CancellationToken ct = default)     → Task<bool>
        Composes 01.Core's TotpVerifier.VerifyAsync (identityKey = a canonical string form of userId);
        on a true result, additionally calls ITotpChallengeStore.RecordSuccessfulChallengeAsync so the
        step-up wiring below can observe it. Rejects userId == Guid.Empty BEFORE any store/verifier call —
        never treats an empty identity as a valid step-up subject.
    RecordStepUpAsync(Guid userId, CancellationToken ct = default)                            → Task
        Lets a consumer's OWN recovery-code verification (IOneWayHasher.Verify against its own stored
        hashes — not this package's concern) feed the SAME freshness mechanism without a live TOTP code.
        Same Guid.Empty guard as VerifyAsync.
    NOTE: The Guid→identityKey formatting is done via exactly ONE shared internal helper
          (TotpIdentityKeyFormatter, internal), reused by both this type (writer) and
          TotpStepUpClaimsTransformation (reader) below — never two independently-formatted conversions
          that could silently miss each other on lookup.
    NOTE (SHIPPED): VerifyAsync/RecordStepUpAsync expose Task<bool>/Task even though 01.Core's
          TotpVerifier.VerifyAsync itself returns ValueTask<bool> — a deliberate async-signature
          normalization at this package's own public boundary, not a functional difference.
```

#### Step-up wiring (`StepUp/`)

```text
TotpStepUpOptions  (sealed class — SHIPPED, WO-069/P-452)
    .AmrClaimType                                                → string   (default: SecurityClaimTypes.AuthenticationMethod, "amr")
    .AmrValue                                                    → string   (default: "otp")
    .ChallengeFreshnessWindow                                    → TimeSpan (default: 15 minutes)
    NOTE: A plain configure-delegate-populated POCO — NOT AddValidatedOptions-bound (no required external
          value), mirroring ApiKeyAuthenticationOptions/MtlsAuthenticationOptions's own un-validated shape,
          not SecurityOptions's Authority/Audience-validated shape.

TotpStepUpClaimsTransformation  (sealed class, implements Microsoft.AspNetCore.Authentication.IClaimsTransformation — SHIPPED, WO-069/P-452)
    TransformAsync(ClaimsPrincipal principal)                    → Task<ClaimsPrincipal>
    — guards principal.Identity?.IsAuthenticated == true AND a parseable, non-Guid.Empty "sub" claim
      BEFORE any store lookup — never looks up an anonymous or Guid.Empty principal (this is also what
      naturally excludes .ApiKey/.Mtls machine-credential identities, which always carry UserId = Guid.Empty)
    — idempotent: skips if a claim of type AmrClaimType with value AmrValue is already present, since
      ASP.NET Core may invoke a registered IClaimsTransformation more than once per request
    — on a fresh hit (TryGetLastSuccessfulChallengeAsync within ChallengeFreshnessWindow), adds EXACTLY
      ONE new claim to a NEW ClaimsIdentity — never mutates the original ClaimsIdentity instance in place
    NOTE — THE COMPOSITION MECHANISM: ASP.NET Core invokes every registered IClaimsTransformation during
          AuthenticateAsync, BEFORE any IUserContext DI factory first resolves for the request. So
          .Oidc's EXISTING defensive amr-claim reader (OidcUserContext, shipped WO-058/P-375) picks up
          the claim stamped here with ZERO code change in .Oidc itself — this is the SAME sanctioned
          "stamp a synthetic marker claim during/after authentication, read it generically downstream"
          mechanism this domain already established for DPoP's IsSenderConstrained (WO-058), applied via
          a framework-provided, scheme-agnostic hook instead of a JwtBearerEvents.OnTokenValidated hook
          (which only .Oidc itself may register, since only .Oidc owns JwtBearerOptions). This is the
          domain's SECOND working example of the "stamp now, read later, zero coupling" pattern, and the
          first to use a scheme-agnostic ASP.NET Core hook (IClaimsTransformation, reachable by ANY
          registered scheme) rather than a JwtBearerEvents hook only .Oidc itself could register.
    NOTE (SHIPPED) — HOW "never mutates in place" IS ACTUALLY ENFORCED: the replacement identity is built
          via `new ClaimsIdentity(principal.Identity, [newClaim])` — the BCL copy-plus-add-claims
          constructor overload, which deep-clones every existing claim onto the new identity (via
          Claim.Clone(this)) and copies AuthenticationType/NameClaimType/RoleClaimType/Label/
          BootstrapContext/Actor from the source — NOT `principal.AddIdentity(...)`, which would instead
          mutate the SAME principal instance's Identities collection in place. A brand-new ClaimsPrincipal
          is returned; the original principal/identity passed in remains completely untouched (T-47).
```

#### DI registration (`Extensions/`)

```text
AddTotpStepUp<TChallengeStore>(Action<TotpStepUpOptions>? configureOptions = null)  →  IServiceCollection    [WO-069/P-452, SHIPPED]
    where TChallengeStore : class, ITotpChallengeStore
    Registers TChallengeStore as scoped ITotpChallengeStore, TotpStepUpOptions, IClaimsTransformation →
    TotpStepUpClaimsTransformation, and TotpChallengeService/TotpEnrollmentService.
    NOT chained onto SecurityAuthenticationBuilder — that type is owned by .Oidc, and this package cannot
    reference .Oidc under the sibling-packages-never-reference-each-other rule. A plain IServiceCollection
    extension, mirroring AddApiKeyAuthentication/AddMtlsAuthentication's own independent-extension shape.
    NOTE: Call after AddSharedKernelSecurity/AddAzureB2CAuthentication (so a JWT Bearer principal exists to
          transform) AND after AddSharedKernelCryptography (so TotpVerifier/ITotpGenerator are registered).
```

**Domain Invariants for this package** (see the dedicated `## Domain Invariants` section below for the
full rationale):

1. TOTP step-up composes ONLY with `.Oidc`'s `OidcUserContext` — `.ApiKey`/`.Mtls` primary identities are
   out of scope by construction (they never read amr claims, and always carry `UserId = Guid.Empty`, which
   the `Guid.Empty` guard above skips).
2. A TOTP step-up NEVER modifies `AuthTime`/`IsAuthenticationFresherThan` — only `AuthenticationMethods`/
   `WasAuthenticatedWith`. `[RequireFreshAuthentication]` and `[RequireAuthenticationMethod("otp")]` stay
   two independent gates, never conflated.

---

## Logging Conventions *(WO-057, P-371 — this domain's first structured-logging retrofit, Core-implemented)*

`12.Security` reserves EventId range **`12000`–`12999`** in `01.Core`'s platform-wide registry, subdivided per package in declaration order. Shipped events, all `internal static partial class SecurityLogEvents`:

| EventId | Package | Method | Level | Fires when |
|---------|---------|--------|-------|-------------|
| `12100` | `SharedKernel.Security.Oidc` | `ServicePrincipalRecognized(ILogger, bool subjectClaimPresent)` | Debug | `OidcUserContext` resolves `IdentityKind.ServicePrincipal` — an authenticated principal with no (or an unparseable) human subject claim; expected/routine, not an error |
| `12101` | `SharedKernel.Security.Oidc` | `TenantClaimResolutionFailed(ILogger, string tenantClaimType)` | Warning | `OidcTenantProvider` falls back to `Guid.Empty` on an *authenticated* principal (never logged for a genuinely unauthenticated request) |
| `12102` | `SharedKernel.Security.Oidc` | `DpopProofRejected(ILogger, string failureReason)` *(WO-058, P-376)* | Warning | `DpopProofValidator` rejects a DPoP proof — `failureReason` structured (`"MissingProof"`, `"JktMismatch"`, `"Expired"`, `"Replayed"`, plus `"AthMismatch"` and `"AlgorithmNotAllowed"` added WO-060/P-385/P-387, shipped), never the raw proof/token |
| `12103` | `SharedKernel.Security.Oidc` | `TokenRevocationRejected(ILogger, bool checkAvailable)` *(WO-058, P-379)* | Warning | `ITokenRevocationCheck` reports a token revoked, OR the check itself was unavailable/threw and failed closed — `checkAvailable` distinguishes the two without leaking which |
| `12104` | `SharedKernel.Security.Oidc` | `JwtSigningAlgorithmRejected(ILogger, string presentedAlgorithm)` *(WO-060, P-387, shipped)* | Warning | The JWT Bearer (non-DPoP) path rejects a token signed with an algorithm outside `Jwt.ValidAlgorithms` — `presentedAlgorithm` is the algorithm NAME only (e.g. `"HS256"`, `"none"`), never any part of the token itself; wired via `ConfigureAlgorithmRejectionLogging` into `JwtBearerEvents.OnAuthenticationFailed` on `SecurityTokenInvalidAlgorithmException`, the one path in this phase with no existing failure-audit hook to reuse |
| `12200` | `SharedKernel.Security.ApiKey` | `ApiKeyValidationFailed(ILogger, string failureReason)` | Warning | `ApiKeyAuthenticationHandler` rejects a key — `failureReason` is a structured string (`"Rejected"`, `"AmbiguousCredential"`), never the raw presented key |
| `12300` | `SharedKernel.Security.Mtls` | `MtlsCertificateRejected(ILogger, string failureReason)` *(WO-058, P-377)* | Warning | `IMtlsCertificateValidator` rejects a certificate, or the RFC 8705 `cnf.x5t#S256` binding check mismatches — `failureReason` structured, never the raw certificate/thumbprint |
| `12400` | `SharedKernel.Security.Totp` | `TotpChallengeRejected(ILogger, string failureReason)` *(WO-069, P-452, SHIPPED)* | Warning | `TotpChallengeService.VerifyAsync` rejects a code — `failureReason` structured (`"InvalidCode"`, `"ReplayedCode"`), never the raw presented code, secret, or recovery code |
| `12401` | `SharedKernel.Security.Totp` | `TotpStepUpClaimApplied(ILogger)` *(WO-069, P-452, SHIPPED)* | Debug | `TotpStepUpClaimsTransformation` stamps the configured AMR claim onto the current principal after a fresh challenge hit — routine/informational, not an error |

`12000`–`12099` (`SharedKernel.Security.Abstractions`) remains reserved but unused — this package stays logging-free (zero-NuGet-dependency rule forbids referencing `Microsoft.Extensions.Logging.Abstractions`). `12300`–`12399` is reserved for `SharedKernel.Security.Mtls` (WO-058) — the fourth per-package sub-block within the domain's `12000`–`12999` range; `12400`–`12499` is now reserved for `SharedKernel.Security.Totp` (WO-069) — the fifth. No log statement in any package ever includes a raw claim value, token content, API key, certificate/thumbprint, TOTP code, TOTP secret, or recovery code — only structured, safe fields. `12104` (WO-060) is the first NEW EventId minted by that phase — every other WO-060 rejection reuses an existing EventId with a widened `failureReason` vocabulary, per that phase's own "no new rejection surface" design constraint (P-385).

Rules (mirroring the platform's WO-041 logging convention):
- Every log statement uses the `[LoggerMessage]` source-generated partial-method pattern with an explicit `EventId` — never a direct `ILogger.LogXxx()` call, never hand-written `LoggerMessage.Define`.
- **Never log raw claim values, raw token content, or a raw API key.** Only structured, safe fields: failure-reason enums, claim-type names (not values), boolean outcomes. This is the platform's highest-value domain for exactly this discipline — a security log line that leaks a token or key defeats its own purpose.
- Expected/routine outcomes (e.g. successful service-principal recognition on a normal service-to-service call) log at `Debug`/`Information`; malformed-token and validation-failure cases log at `Warning` — a spike here is a real security-monitoring signal.

---

## Implementation Rules

- `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — references only `SharedKernel.Primitives`.
- `IUserContext` and `ITenantProvider` are **scoped** — one instance per HTTP request. Never register as singleton.
- `AnonymousUserContext` is the registered fallback — `IUserContext` is always resolvable; callers must check `IsAuthenticated` before consuming `UserId`.
- `IUserContext.UserId` must **never return `Guid.Empty`** when `IdentityKind == IdentityKind.User` — if the `sub` claim is absent or unparseable and the principal is otherwise unauthenticated, the implementation must set `IsAuthenticated = false`/`IdentityKind = Anonymous`. **Corrected invariant (WO-057, P-367):** `IdentityKind.ServicePrincipal` and `IdentityKind.System` legitimately carry `IsAuthenticated = true` with `UserId == Guid.Empty` — this is not a bug, it is the standard shape of a client-credentials/M2M token or a trusted background-execution context. The old, stronger rule ("`UserId` never `Guid.Empty` when `IsAuthenticated == true`") was replaced because it could not distinguish those two legitimate states from a rejected token.
- `ITenantProvider.TenantId` returns `Guid.Empty` when no tenant claim is present — callers must handle this case (unauthenticated or system-level requests).
- Application and domain-adjacent code must **inject `IUserContext`** — never inject `IHttpContextAccessor`, `ClaimsPrincipal`, or `HttpContext` directly. Those are infrastructure details.
- Domain code (`03.Domain`) must **never reference `ITenantProvider`** — the application layer resolves `TenantId` and passes it as a `Guid` primitive to aggregate constructors.
- Infrastructure (`06.Persistence`) may inject `ITenantProvider` **only** for global tenant filter application in `TenantedDbContext`. No other infrastructure component should depend on `ITenantProvider`.
- `SecurityClaimTypes` constants are `const string` fields in a static class — not enums. Consuming services may define additional local constants. **As of WO-057 (P-366), `Email`/`Role` are legacy-shape reference constants only** — the active lookup key at runtime is `SecurityOptions.ClaimMapping`, whose short-name defaults match the unmapped claim shape every standards-conformant OIDC issuer emits by default (`MapInboundClaims = false` since .NET 8). `UserId`/`TenantId` (`"sub"`/`"tenant_id"`) are unaffected — already short names.
- Claim-type resolution for Email/Name/Role/Permission is **configurable per identity provider** via `SecurityOptions.ClaimMapping` — never hardcoded to one convention. `TokenValidationParameters.NameClaimType`/`RoleClaimType` must always be sourced from the same configured values `OidcUserContext` reads; the two must never diverge (WO-057, P-366).
- Role-claim parsing must be **defensive to both real-world shapes** — one `Claim` per role, and a single claim whose value is a JSON array of roles — and must never throw on either (WO-057, P-366).
- `OidcUserContext` and `OidcTenantProvider` are constructed lazily per-request — no caching across requests.
- `SecurityOptions` must use `AddValidatedOptions` from `SharedKernel.Configuration` — misconfigured apps must fail at startup, not at first authentication attempt.
- JWT Bearer configuration must set `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true` by default. Any relaxation must be explicit and documented.
- `HasRole(string role)` and `HasPermission(string permission)` comparisons are **case-insensitive** — role/permission names may arrive from different identity providers with varying casing (WO-057, P-368 for `HasPermission`).
- `IUserContext.Claims` is a **read-only dictionary keyed by claim type** (first value wins for multi-value claims) — for roles use `IUserContext.Roles`, for scopes use `IUserContext.Permissions`.
- Service-principal (`IdentityKind.ServicePrincipal`) detection must be **IdP-agnostic** — never hardcode a single vendor's claim names (e.g. Entra's `idtyp`/`azp`) as the sole detection mechanism, since this package supports any standards-compliant OIDC issuer (WO-057, P-367).
- `SystemUserContext` ships with **no DI wiring** in `SharedKernel.Security.Abstractions` — a background-execution composition root is responsible for registering it in place of the HTTP-derived `IUserContext` factory. This domain does not itself decide whether `IdentityKind.System` bypasses authorization (WO-057, P-369).
- `SharedKernel.Security.ApiKey` key comparison must be **constant-time**, delegating to `SharedKernel.Cryptography`'s existing primitives — never `string.Equals`/`==` (timing-attack surface) (WO-057, P-370).
- `SharedKernel.Security.ApiKey` never dictates a key storage mechanism — `IApiKeyValidator` is the sole consumer-supplied extensibility point; this package does not attempt key issuance, rotation, or storage (WO-057, P-370).
- `AddApiKeyAuthentication` composes alongside `AddSharedKernelSecurity`'s JWT Bearer scheme via a policy/forwarding scheme selector — a service may accept either credential type on the same host without one scheme silently shadowing the other (WO-057, P-370).
- Security-relevant log events use the `[LoggerMessage]` source-generated pattern with an explicit `EventId` in `12000`–`12999` — never a direct `ILogger.LogXxx()` call. **Never log raw claim values, raw token content, or a raw API key** — only structured, safe fields (WO-057, P-371).
- No static mutable state anywhere in this domain.
- `IUserContext.IsAuthenticationFresherThan` must **never call `DateTimeOffset.UtcNow` internally** — the caller always supplies `now` explicitly (typically from its own injected `IClock`/`TimeProvider`), per the platform's injectable-time convention (WO-058, P-375).
- `amr` (authentication-method) claim parsing must be **defensive to both real-world shapes** — one `Claim` per method, and a single space-delimited claim value — and must never throw on either, mirroring the P-366 role-claim reader discipline; `auth_time` parsing must never throw on an absent or malformed value, resolving to `AuthTime = null` instead (WO-058, P-375).
- DPoP validation (`SharedKernel.Security.Oidc`) is **opt-in and disabled by default** — `RequireDpop<TReplayCache>()` must add zero behavior change for a consumer that never calls it. The replay-check seam (`IDpopProofReplayCache`) is the sole extensibility point; this package never references `02.Caching` or dictates a storage mechanism, mirroring `IApiKeyValidator`'s "never dictates storage" precedent (WO-058, P-376).
- The token-revocation seam (`ITokenRevocationCheck`, `SharedKernel.Security.Oidc`) is **opt-in, disabled by default, and must fail closed** — an unavailable or throwing revocation check rejects the request; it must never silently authenticate. It runs only after standard signature/issuer/audience/lifetime validation succeeds, never in place of it, and a revoked token surfaces the same generic authentication-failure shape as an expired token — no information-leaking distinction (WO-058, P-379).
- `SharedKernel.Security.Mtls` never dictates a CA trust store, revocation-check (CRL/OCSP) mechanism, or certificate storage — `IMtlsCertificateValidator` is the sole consumer-supplied extensibility point, mirroring `IApiKeyValidator`'s exact shape; this package does not attempt certificate issuance or CA management (WO-058, P-377).
- `SharedKernel.Security.Mtls`'s RFC 8705 `cnf.x5t#S256` certificate-bound-token check must use a **constant-time comparison** via `01.Core/SharedKernel.Cryptography` — never `string.Equals`/`==`, same timing-attack discipline as `.ApiKey` (WO-058, P-377).
- Sibling provider packages (`.Oidc`, `.ApiKey`, `.Mtls`) **never reference one another** — each references only `SharedKernel.Security.Abstractions` and whatever `01.Core` primitives it needs; cross-scheme composition (e.g. `.Mtls`'s RFC 8705 binding check reading a `cnf` claim produced by `.Oidc`) happens exclusively through `ClaimsPrincipal`/claim inspection or the `ServiceDescriptor`-capture decorator pattern, never a type reference (WO-058, extends the WO-057 `.ApiKey` precedent to a third sibling).
- Any code registered via `IServiceCollection.AddOptions<TOptions>(name).Configure(...)` that needs a **request-scoped** dependency (e.g. `IDpopProofReplayCache`, `ITokenRevocationCheck`) must resolve it from `HttpContext.RequestServices` **inside** the per-request delegate body (e.g. inside `JwtBearerEvents.OnTokenValidated`) — never via a captured `IServiceProvider` injected into the `Configure`/`Configure<TDep>` delegate itself, which resolves against the **root** container at options-configuration time and will throw or silently misbehave for a Scoped service (WO-058, `DpopProofValidator`/`RevocationCheckRunner`).
- Composing a second/third opt-in `JwtBearerEvents.OnTokenValidated` handler (`SecurityAuthenticationBuilder.RequireDpop`/`.WithRevocationCheck`) must always capture and invoke the **previously-registered** handler first, then short-circuit if `context.Result` is already non-null (an earlier handler already terminated the request) before running its own seam — never overwrite `OnTokenValidated` outright (WO-058).
- A synthetic, package-owned claim type (e.g. `Dpop.DpopClaimTypes.SenderConstrained`) is the sanctioned mechanism for signaling a fact established during token validation (`OnTokenValidated`) forward into `IUserContext` construction, which happens later from the same `ClaimsPrincipal` via the DI factory — stamp the claim onto the principal's `ClaimsIdentity` on success; never invent a second, parallel side-channel (WO-058).
- **(WO-060, P-385)** The DPoP `ath` (access-token-hash) check is a **mandatory fifth binding**, not an optional enhancement — RFC 9449 §4.3 requires it whenever a DPoP proof accompanies an access token. It must reuse the existing `DpopProofRejected` failure/audit-logging path (a new `failureReason` value, never a new `EventId` or a new rejection code path) — a security compliance fix is not license to fragment the failure surface.
- **(WO-060, P-386)** `MtlsAuthenticationOptions` and any future machine-client-credential options type in this domain must default to a posture **no weaker than the equivalent ASP.NET Core framework default** — a shared-kernel library's own convenience wrapper must never be the reason a consumer ends up less secure than using the framework directly. Any deliberately permissive default (e.g. a private-PKI trust chain) is an explicit, separately-named opt-in, never the shipped default.
- **(WO-060, P-387)** `SecurityOptions.Jwt.ValidAlgorithms`/`DpopOptions.ValidAlgorithms` allowlists are enforced **before** any signature or claim evaluation proceeds — an out-of-allowlist `alg` (including `"none"`) is a hard, early rejection, not a downstream validation failure. Defaults follow the FAPI 2.0 Security Profile baseline (PS256/ES256); widening the allowlist for a non-FAPI IdP (e.g. adding `"RS256"`) is the consuming service's own explicit, documented configuration choice.
- **(WO-060, P-388)** The revocation-check caching seam (`IRevocationCheckCache`) must **never let a "revoked" outcome be masked by a stale cached "not revoked" entry** beyond `RevocationCheckCacheOptions.RevokedTtl` — a cache-lookup failure or an unreadable cache entry falls through to the inner `ITokenRevocationCheck`, inheriting its fail-closed contract; the cache must never itself become a new fail-open surface. This package never references `02.Caching` — the seam is consumer-implemented, mirroring `IDpopProofReplayCache`'s precedent.
- **(WO-060, P-389)** A multi-candidate key/credential comparison (`ApiKeyRotationComparer.AnyMatch` and any future analogue) must **always evaluate every candidate** — never short-circuit on the first match — so elapsed comparison time never correlates with which, or how many, candidates matched. This is the same timing-attack discipline the single-key `ConstantTimeKeyComparer`/RFC 8705 thumbprint comparer already apply, extended to the N-candidate case. `ApiKeyRotationComparer` must be **public** — unlike the internal `ConstantTimeKeyComparer`, it is the sanctioned entry point for a consumer's own `IApiKeyValidator` implementation, necessarily authored outside this package's assembly.
- **(WO-069, P-452, SHIPPED)** `SharedKernel.Security.Totp` must never reimplement any RFC 6238/4226 primitive — the algorithm, Base32 codec, provisioning-URI builder, and replay-verified challenge composition all live in `01.Core/SharedKernel.Cryptography` (P-451); this package only orchestrates them and wires the result into ASP.NET Core.
- **(WO-069, P-452, SHIPPED)** `ITotpChallengeStore` never dictates a storage mechanism — the sole consumer-supplied extensibility point, mirroring `IDpopProofReplayCache`/`ITokenRevocationCheck` exactly; this package never references `02.Caching` or `06.Persistence`.
- **(WO-069, P-452, SHIPPED)** `TotpChallengeService.VerifyAsync`/`.RecordStepUpAsync` and `TotpStepUpClaimsTransformation` must **hard-reject `Guid.Empty`/an anonymous principal before any store or verifier call** — an empty identity or unauthenticated principal is never a valid step-up subject. This is also the mechanism that structurally excludes `.ApiKey`/`.Mtls` machine-credential identities (always `UserId = Guid.Empty`) from ever triggering a store lookup.
- **(WO-069, P-452, SHIPPED)** `TotpStepUpClaimsTransformation` must be **idempotent** — ASP.NET Core may invoke a registered `IClaimsTransformation` more than once per request; it must check whether its configured AMR claim/value is already present before adding it again, and must never mutate the original `ClaimsIdentity` instance in place (add a new claim to a new identity instead).
- **(WO-069, P-452, SHIPPED)** A TOTP step-up must **never modify `AuthTime`/`IsAuthenticationFresherThan`** — it only augments `AuthenticationMethods`/`WasAuthenticatedWith`. `[RequireFreshAuthentication]` stays keyed solely on the primary authentication's own recency; `[RequireAuthenticationMethod("otp")]` is the correct, independent gate for "was TOTP verified," and its own freshness is enforced entirely by `ChallengeFreshnessWindow` at claim-stamping time.
- **(WO-069, P-452, SHIPPED)** `SharedKernel.Security.Totp` never references `.Oidc`/`.ApiKey`/`.Mtls` — its composition mechanism reaches `.Oidc`'s `OidcUserContext` indirectly, through the shared `ClaimsPrincipal` an `IClaimsTransformation` populates before any `IUserContext` DI factory resolves, never through a type reference — extending the sibling-packages-never-reference-each-other rule to a fourth sibling.
- **(WO-069, P-452, SHIPPED)** A `Guid` UserId must be formatted to `ITotpChallengeStore`'s `string identityKey` via **exactly one shared internal helper**, reused by both `TotpChallengeService` (writer) and `TotpStepUpClaimsTransformation` (reader) — never two independently-formatted conversions that could silently miss each other on lookup.

---

## Domain Invariants

### Multi-tenant JWT claim trust boundary (WO-060, P-392, D-42)

This section states precisely what tenant-claim trust `SharedKernel.Security.Oidc` provides once JWT
signature validation succeeds, and what remains the issuing identity provider's (IdP's) own
responsibility. It exists because this boundary was previously undocumented anywhere in this domain's
brain or READMEs — a defensible design that read as silence during a security due-diligence review.

**What this library guarantees**, once `SecurityOptions.Jwt` signature/issuer/audience/lifetime
validation succeeds (and, as of WO-060, the JWS algorithm allowlist check passes):

- The `tenant_id` claim value returned by `OidcTenantProvider.TenantId` is **exactly the value the
  issuing IdP placed in the token** — it has not been altered, forged, or substituted in transit. A
  bearer of the token cannot change the claim's value without invalidating the token's signature.
- `OidcUserContext`/`OidcTenantProvider` perform no reinterpretation of the claim beyond a direct
  `Guid.TryParse` — the trust boundary is exactly "what the IdP signed", never a value this library
  independently derives, caches, or re-computes.
- A structurally invalid token (bad signature, wrong issuer/audience, expired, disallowed algorithm)
  never reaches claim resolution at all — `TenantId` only ever reflects a claim from a token this
  library has already cryptographically authenticated.

**What remains the issuing IdP's own responsibility — this library cannot verify it:**

- That the `tenant_id` claim was populated with the CORRECT tenant for the authenticated principal.
  If a single `SecurityOptions.Jwt.Authority` genuinely serves more than one tenant (a shared
  multi-tenant IdP tenant, as opposed to per-tenant issuer segregation), this library has no
  independent way to confirm the claim's value actually corresponds to a tenant the subject is a
  legitimate member of — that correctness is delegated entirely to the IdP's own claim-issuance logic
  (its user-tenant membership store, its token-minting pipeline, its app-registration/audience scoping).
- A misconfigured IdP, or an app registration that allows a caller to influence which `tenant_id` value
  is minted into its own token, produces a token this library will faithfully accept as validly signed
  — with an incorrect tenant claim. Signature validity proves authenticity of the CLAIM VALUE, not
  correctness of the tenant it names.

**Recommended posture for a consuming service:**

- **Preferred:** a per-tenant `Authority`/issuer topology, so a token minted for Tenant A's issuer
  cannot even pass `ValidateIssuer`/`SecurityOptions.Jwt.Authority` validation against a host configured
  for Tenant B — the trust boundary above becomes moot because cross-tenant claim confusion cannot
  reach this library's validation pipeline at all.
- **When a single shared multi-tenant IdP `Authority` is unavoidable:** treat `ITenantProvider.TenantId`
  as authenticated-but-not-independently-verified, and layer an additional authoritative check (e.g. a
  tenant-membership lookup against the service's own tenant/user store) before using it for a
  high-consequence authorization decision. `SharedKernel.Security.Oidc` performs no such secondary
  check itself — doing so would require a data-access dependency this package's zero-infrastructure-
  coupling design (`12.Security` may only reference `01.Core`) forbids.

This is a documentation-only clarification — no code contract changes. It also governs
`SharedKernel.Security.Mtls`'s and `SharedKernel.Security.ApiKey`'s analogous claims (`Roles`/
`Permissions`/`ClientId` sourced from `ApiKeyValidationResult`/`MtlsValidationResult`): the same
"authenticated does not imply independently re-verified" boundary applies to any claim value this
domain surfaces from a signed token or a consumer-supplied validator result — the validator/IdP is
always the source of truth this library trusts, never re-derives.

STATUS: SHIPPED (WO-060, DOC-19). This content is adapted into `SharedKernel.Security.Oidc/README.md`'s
own `## Multi-tenant JWT claim trust boundary` section, cross-linked with the DOC-17 revocation-caching
recipe (same README) and the DOC-18 API-key rotation-window recipe (`SharedKernel.Security.ApiKey/README.md`)
from the same documentation pass.

### TOTP step-up scope boundary and `AuthTime` non-interaction (WO-069, P-452, D-53/D-54)

`SharedKernel.Security.Totp` deliberately narrows its own applicability rather than trying to generalize
across every primary credential type this domain ships. Two boundaries are locked here so a future phase
does not accidentally widen them without re-deriving the reasoning:

**1. TOTP step-up composes ONLY with `.Oidc`'s `OidcUserContext`.** `TotpStepUpClaimsTransformation`
stamps a synthetic `amr`-shaped claim onto the current `ClaimsPrincipal`; only `OidcUserContext`'s already-
shipped defensive `amr` reader (WO-058/P-375) ever consults that claim type. `ApiKeyUserContext` and
`MtlsUserContext` hardcode `AuthenticationMethods` to empty by design (WO-058) — they have no OIDC
authentication-context concept for a pre-shared key or a client certificate — so stamping an `amr` claim
in front of either has no observable effect on `IUserContext.WasAuthenticatedWith`/`AuthenticationMethods`.
This is intentional, not a gap: `.ApiKey`/`.Mtls` identities are `IdentityKind.ServicePrincipal` machine
credentials, never the target audience for a HUMAN second factor. The `Guid.Empty` guard in
`TotpStepUpClaimsTransformation`/`TotpChallengeService` reinforces this structurally — `.ApiKey`/`.Mtls`
identities always carry `UserId = Guid.Empty`, so a step-up lookup for either is skipped before it could
ever run, without needing to special-case `IdentityKind` explicitly.

**2. A TOTP step-up never touches `AuthTime`/`IsAuthenticationFresherThan`.** These two members remain
scoped exclusively to the PRIMARY authentication event's own recency (WO-058, P-375) — a TOTP challenge
augments `AuthenticationMethods`/`WasAuthenticatedWith` only. This keeps `[RequireFreshAuthentication(maxAge)]`
and `[RequireAuthenticationMethod("otp")]` as two independent, non-conflated gates: the former always asks
"how long ago did the primary sign-in happen," the latter asks "was a second factor presented," and its
own recency is enforced entirely by `TotpStepUpOptions.ChallengeFreshnessWindow` at the point the
`IClaimsTransformation` stamps the claim — not by borrowing `AuthTime`'s semantics. A future phase wanting
"require BOTH a fresh primary sign-in AND a fresh TOTP step-up" composes the two existing attributes
together at the call site; it does not require either member to change meaning.

STATUS: `●` SHIPPED (WO-069, P-452) — verified against real, tested code (D-53/D-54, T-43/T-44/T-46 for
boundary 1; D-54, no test contradicts it — no code path anywhere in `SharedKernel.Security.Totp` reads
or writes `AuthTime` — for boundary 2), per this domain's own established discipline for every prior
Domain Invariant. Both boundaries hold exactly as designed, no drift.

**Zero cross-domain `IUserContext` fallout, for the first time in this domain's WO-057→WO-069 lineage.**
Every prior work order in this domain (WO-057/P-367-368, WO-058/P-375-376, WO-060 touched no interface but
WO-057/058 did) added new `IUserContext` members, which required grepping the whole repo for every
implementer and fixing each one outside `12.Security` (`06.Persistence.EfCore`'s `NoOpUserContext` and
test fixtures, `16.Testing`'s `FakeUserContext`, etc. — see the Core-phase memory notes for WO-057/WO-058).
WO-069 added no `IUserContext`/`ITenantProvider` member at all — `SecurityClaimTypes.AuthenticationMethod`
is a new `const string` on an unrelated static class, not an interface change — so the "always grep the
whole repo for implementers" rule simply did not apply this round. This is worth remembering as a contrast:
absence of interface fallout is not evidence the rule was skipped, it is evidence no interface changed.

---

## DI Registration (expected shape)

```csharp
// Standard JWT / Entra ID (non-B2C):
services.AddSharedKernelSecurity(configuration);

// Azure B2C / Entra External ID:
services.AddAzureB2CAuthentication(configuration);

// ITenantProvider is registered automatically by both methods above.
// To access current user identity in application code:
// → inject IUserContext (scoped)
// To resolve tenant for aggregate construction in application code:
// → inject ITenantProvider (scoped), extract TenantId, pass as Guid to domain constructor

// Machine-client / pre-shared-key callers (WO-057, additive, composes alongside the above):
services.AddApiKeyAuthentication<MyDatabaseBackedApiKeyValidator>();
// A request presenting a valid API key resolves IUserContext.IdentityKind == ServicePrincipal;
// a request presenting a valid JWT still resolves via OidcUserContext as before — the policy/
// forwarding scheme selector picks whichever credential the request actually presented.

// Certificate-based (mTLS) machine clients, e.g. regulated Open Banking TPPs (WO-058, P-377,
// additive, composes alongside the above):
services.AddMtlsAuthentication<MyCertificateWhitelistValidator>();

// Background-execution hosts (17.Workflows activities, 07.Messaging consumers, hosted services)
// register SystemUserContext explicitly in place of the HTTP-derived IUserContext factory:
services.AddScoped<IUserContext>(_ => SystemUserContext.Instance);

// Opt-in DPoP sender-constraining and/or opt-in revocation checking (WO-058, P-376/P-379),
// plus opt-in revocation-check caching (WO-060, P-388 — chains AFTER WithRevocationCheck, throws
// InvalidOperationException otherwise) — chained off the builder AddSharedKernelSecurity/AddAzureB2CAuthentication
// return; a consumer that ignores the return value gets today's unconstrained-bearer behavior
// unchanged:
services
    .AddSharedKernelSecurity(configuration)
    .RequireDpop<MyDistributedDpopReplayCache>()
    .WithRevocationCheck<MyIntrospectionBackedRevocationCheck>()
    .WithRevocationCheckCaching<MyMemoryCacheBackedRevocationCache>();

// Widening the JWS signing-algorithm allowlist for a non-FAPI IdP that signs with RS256
// (WO-060, P-387 — the shipped default is PS256/ES256 only; omitting this override
// for an RS256-signing IdP after upgrading rejects every previously-valid token):
services.Configure<SecurityOptions>(options =>
    options.Jwt.ValidAlgorithms = ["PS256", "ES256", "RS256"]);

// Opting an mTLS host BACK IN to accepting self-signed certificates (WO-060, P-386 —
// the corrected default rejects them; only opt in for a real reason, e.g. a private-PKI QWAC/
// QSEAL trust chain that is not a standard public CA):
services.AddMtlsAuthentication<MyPrivatePkiCertificateValidator>(options =>
{
    options.AllowedCertificateTypes = CertificateTypes.All; // WEAKENS TRUST VALIDATION — see XML docs
});

// Validating against multiple simultaneously-active API keys during a rotation window
// (WO-060, P-389 — inside a consumer's own IApiKeyValidator implementation, in the
// consumer's OWN assembly — ApiKeyRotationComparer is public precisely so this compiles there):
// var candidates = await _store.GetActiveKeysAsync(clientId, ct); // e.g. [oldKey, newKey]
// return ApiKeyRotationComparer.AnyMatch(presentedKey, candidates)
//     ? ApiKeyValidationResult.Valid(clientId)
//     : ApiKeyValidationResult.Invalid;

// Step-up authorization in application code — no new DI registration required, just the
// richer IUserContext surface (WO-058, P-375):
// if (!userContext.WasAuthenticatedWith("mfa")
//     || !userContext.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), clock.UtcNow))
// {
//     return Result.Failure(Error.Unauthorized("StepUpRequired", "..."));
// }

// TOTP second-factor step-up (WO-069, P-452, SHIPPED).
// Requires AddSharedKernelCryptography (for TotpVerifier/ITotpGenerator, 01.Core P-451) and
// AddSharedKernelSecurity/AddAzureB2CAuthentication (for a principal to transform) to have run first:
services.AddSharedKernelCryptography(configuration);
services
    .AddSharedKernelSecurity(configuration)
    .RequireDpop<MyDistributedDpopReplayCache>();
services.AddTotpStepUp<MyRedisBackedTotpChallengeStore>();
// After a caller's own challenge endpoint calls TotpChallengeService.VerifyAsync(...) and it returns
// true, EVERY subsequent request from that principal (within TotpStepUpOptions.ChallengeFreshnessWindow)
// automatically resolves IUserContext.WasAuthenticatedWith("otp") == true — no 14.Presentation change,
// no .Oidc change, required. [RequireAuthenticationMethod("otp")] on a 14.Presentation endpoint just works.
```

`SharedKernel.Security.Abstractions` ships **no DI extensions** — it is a pure abstraction library (this includes `SystemUserContext`, which a consuming composition root wires up itself). All HTTP-pipeline registration (including the opt-in DPoP/revocation-check builder chain) lives in `SharedKernel.Security.Oidc`; machine-client registration lives in `SharedKernel.Security.ApiKey` (pre-shared key) and `SharedKernel.Security.Mtls` (client certificate); second-factor step-up registration lives in `SharedKernel.Security.Totp` (SHIPPED, WO-069, P-452) — `AddTotpStepUp<TChallengeStore>()` is a plain `IServiceCollection` extension, never chained onto `.Oidc`'s `SecurityAuthenticationBuilder`.

---

## AOT Compatibility

- `IUserContext`, `ITenantProvider`, `AnonymousUserContext`, `SystemUserContext`, `IdentityKind` are sealed/interface/enum types — AOT-safe.
- `SecurityClaimTypes` is a static class of string constants — AOT-safe.
- `OidcUserContext` and `OidcTenantProvider` read from `ClaimsPrincipal` — `ClaimsPrincipal.Claims` iteration is AOT-safe (no reflection on user types). The defensive role-claim reader (JSON-array-valued claim shape, WO-057) uses `System.Text.Json` — source-generated where practical, otherwise scoped narrowly to that one parse path.
- `SecurityOptions`/`ClaimMappingOptions` use `AddValidatedOptions` from `SharedKernel.Configuration` — `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+; verify on each upgrade.
- `Microsoft.AspNetCore.Authentication.JwtBearer` — AOT support is partial; JWT token parsing uses internal reflection in some code paths. Encapsulating it behind `IUserContext` limits the AOT blast radius to the registration path only.
- `Microsoft.Identity.Web` — not fully AOT-safe; document this explicitly and keep it isolated to `AddAzureB2CAuthentication`. A swap to a lighter JWT-only path is possible without changing abstractions.
- `Microsoft.AspNetCore.Authentication.Abstractions`'s `AuthenticationHandler<TOptions>` (`SharedKernel.Security.ApiKey`, WO-057) is BCL/framework-provided and AOT-safe; the constant-time key comparison delegates to `SharedKernel.Cryptography`, already AOT-preferred.
- `Guid.Parse` on claim values — AOT-safe (BCL).
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in hot paths.
- `DateTimeOffset.FromUnixTimeSeconds` (`auth_time` parsing, WO-058, P-375) — AOT-safe (BCL).
- DPoP proof JWT parsing/verification (`SharedKernel.Security.Oidc`, WO-058, P-376) uses `Microsoft.IdentityModel.JsonWebTokens`/`Microsoft.IdentityModel.Tokens` — already transitively present via `Microsoft.AspNetCore.Authentication.JwtBearer`/`Microsoft.Identity.Web`, so it carries the SAME partial-AOT caveat already documented above for those packages; no new AOT surface is introduced, only a new call path into an existing dependency.
- `Microsoft.AspNetCore.Authentication.Certificate` (`SharedKernel.Security.Mtls`, WO-058, P-377) is a framework-provided handler; its AOT status must be verified against the target .NET 10 SDK at implementation time and documented here — do not assume full AOT compatibility without checking.
- The RFC 8705 `cnf.x5t#S256` thumbprint check (`SharedKernel.Security.Mtls`, WO-058, P-377) delegates hashing/comparison to `01.Core/SharedKernel.Cryptography`, already AOT-preferred — no new reflection surface.
- The DPoP `ath` check (WO-060, P-385) computes a SHA-256 hash of the raw bearer token string via the BCL — no new AOT surface beyond what the existing `jkt`/RFC 8705 thumbprint checks already use.
- `SecurityOptions.Jwt.ValidAlgorithms`/`DpopOptions.ValidAlgorithms` (WO-060, P-387) are `IReadOnlyCollection<string>` allowlists consulted via simple set-membership checks — AOT-safe, no reflection.
- `IRevocationCheckCache`/`CachingTokenRevocationCheck` (WO-060, P-388) introduce no new AOT surface — a plain interface/decorator pair with no third-party cache dependency of their own.
- `ApiKeyRotationComparer.AnyMatch` (WO-060, P-389) is a simple loop over the same technique the existing internal single-key comparer uses — no new AOT surface.
- **(WO-069, P-452, SHIPPED)** `TotpEnrollmentService`/`TotpChallengeService` delegate every RFC 6238/4226-adjacent operation to `01.Core/SharedKernel.Cryptography` (`Base32`, `TotpProvisioningUri`, `RecoveryCodeGenerator`, `TotpVerifier`), already AOT-preferred per that domain's own notes — no new reflection surface introduced here.
- **(WO-069, P-452, SHIPPED)** `Microsoft.AspNetCore.Authentication.IClaimsTransformation` is a plain interface; `TotpStepUpClaimsTransformation`'s implementation is ordinary claims-collection manipulation — AOT-safe. CONFIRMED reachable purely via the bare `Microsoft.AspNetCore.App` `FrameworkReference` — no separate `PackageReference` needed, verified directly against the installed shared-framework directory (`Microsoft.AspNetCore.Authentication.Abstractions.dll` present) before writing the csproj, unlike the `.Mtls`/`Microsoft.AspNetCore.Authentication.Certificate` correction precedent (WO-058), where the identical "framework-provided" assumption was wrong. The lesson generalizes either way — always verify against the installed SDK, never assume.

---

## Test Rules

- Unit tests for `SharedKernel.Security.Abstractions` live in `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.Tests/`.
- Unit tests for `SharedKernel.Security.Oidc` live in `12.Security/SharedKernel.Security.Oidc/SharedKernel.Security.Oidc.Tests/`.
- Unit tests for `SharedKernel.Security.ApiKey` live in `12.Security/SharedKernel.Security.ApiKey/SharedKernel.Security.ApiKey.Tests/`.
- Unit tests for `SharedKernel.Security.Mtls` live in `12.Security/SharedKernel.Security.Mtls/SharedKernel.Security.Mtls.Tests/` *(WO-058)*.
- `AnonymousUserContext`: all properties return correct sentinel values; `HasRole`/`HasPermission` always return `false`; `IsAuthenticated == false`; `UserId == Guid.Empty`; `IdentityKind == Anonymous`.
- `SystemUserContext`: `IdentityKind == System`, distinguishable from `AnonymousUserContext.IdentityKind == Anonymous` by every consumer that pattern-matches on `IdentityKind` (WO-057, P-369).
- `OidcUserContext`: valid `ClaimsPrincipal` with all claims maps correctly; missing `sub` claim on an otherwise-unauthenticated principal → `IsAuthenticated = false`/`IdentityKind = Anonymous`; missing `sub` on an otherwise-authenticated principal → `IsAuthenticated = true`/`IdentityKind = ServicePrincipal` (WO-057, P-367); missing role claims → empty `Roles`; `HasRole`/`HasPermission` case-insensitive.
- **Claim-shape realism test (WO-057, P-366, GATING):** a `ClaimsPrincipal` built from unmapped short-name claims (`"email"`, `"name"`, `"roles"` as both a JSON-array-valued claim and discrete per-role claims) with no `MapInboundClaims` override — the realistic default shape against a .NET 8+ `JwtBearerHandler` — must prove `Email`/`Username`/`Roles`/`HasRole` all resolve correctly. This test is required to fail against the pre-fix shipped code and pass after, per the phase's own acceptance criteria; a regression test for the legacy `ClaimTypes.*`-mapped shape must also still pass.
- Service-principal test (WO-057, P-367): a client-credentials-shaped token (no human subject claim, valid signature/issuer/audience) resolves `IsAuthenticated = true`, `IdentityKind = ServicePrincipal`.
- Permission test (WO-057, P-368): a multi-scope claim value (`"orders:read orders:write"`) parses into two independent `Permissions` entries; an absent scope claim yields an empty collection, never a throw.
- API-key test (WO-057, P-370): an invalid or absent key never resolves to an authenticated context; a valid key resolves correctly regardless of which scheme (JWT Bearer or API key) the request actually presented; the comparison path is proven timing-safe (no early-exit short-circuit).
- Structured-logging test (WO-057, P-371): `[LoggerMessage]` events fire at the correct level/`EventId` for malformed-token, service-principal-recognized, tenant-resolution-failure, and API-key-validation-failure scenarios, asserted via `16.Testing`'s in-memory `ILogger` double; no claim/token/key value present in any logged field.
- `OidcTenantProvider`: valid tenant claim parses to correct `Guid`; absent claim → `Guid.Empty`; malformed claim → `Guid.Empty`.
- `SecurityOptions` validation: valid config registers without throw; missing `Authority` throws at `IHost.StartAsync()`; missing `Audience` throws at startup.
- DI registration tests: `AddSharedKernelSecurity` registers `IUserContext` as scoped; registers `ITenantProvider` as scoped; `IUserContext` is resolvable without an active HTTP context (returns `AnonymousUserContext` fallback); `TokenValidationParameters.NameClaimType`/`RoleClaimType` are set correctly from `SecurityOptions.ClaimMapping` (WO-057, P-366).
- Role-based tests: `HasRole` with exact case match returns `true`; `HasRole` with differing case returns `true`; unknown role returns `false`. Same three cases apply to `HasPermission` (WO-057, P-368).
- **Test construction pattern — authenticated principal:** `new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))` — passing an `authenticationType` string makes `IsAuthenticated = true`.
- **Test construction pattern — unauthenticated principal:** `new ClaimsPrincipal(new ClaimsIdentity(claims))` — omitting `authenticationType` makes `IsAuthenticated = false`.
- **Recommended pattern for a CONSUMING service's own test suites:** `16.Testing/SharedKernel.Testing`'s `SecurityTestContextBuilder` (`SharedKernel.Testing.Security`) produces both a `ClaimsPrincipal` (`.Build()`) and an `IUserContext` (`.BuildUserContext()`) from one fluent construction — the sanctioned alternative to hand-assembling a `ClaimsIdentity` by hand in a downstream service's own test project. `12.Security` sits *below* `16.Testing` in the layering order and cannot reference it, so this guidance is for consuming services' test projects, never for this domain's own fixtures — the hand-rolled `ClaimsPrincipal` patterns above remain how this domain's own `.Tests` projects construct fixtures.
- **DI registration tests** use `IServiceCollection` / `ServiceCollection` directly with `BuildServiceProvider()` — no `WebApplicationFactory` or test host required for unit-level DI verification.
- **`AddAzureB2CAuthentication` DI tests must additionally register `IConfiguration` directly in the `ServiceCollection`** (`services.AddSingleton<IConfiguration>(config)`) before calling `AddAzureB2CAuthentication(config)` — confirmed at WO-057/T-09 implementation time: `Microsoft.Identity.Web`'s `SetIdentityModelLogger` resolves `IConfiguration` from the container itself, not merely from the `configuration` parameter passed into the extension method. Every real ASP.NET Core host registers `IConfiguration` automatically via `WebApplicationBuilder`; a bare `ServiceCollection`-based unit test does not and must do so explicitly or `BuildServiceProvider()`/first options resolution throws `InvalidOperationException`.
- **Testing `ConstantTimeKeyComparer` directly** (an `internal` type in `SharedKernel.Security.ApiKey`) requires `SharedKernel.Security.ApiKey.csproj` to declare `[assembly: InternalsVisibleTo("SharedKernel.Security.ApiKey.Tests")]` (via an `AssemblyAttribute` `ItemGroup`, mirroring the pattern already used across `02.Caching`/`04.Contracts`/`06.Persistence`/`07.Messaging`/`11.Communication`) — added WO-057/T-16 so the timing-safe compare path can be proven directly (via a hand-rolled `IHmacSigner` test double showing the outcome is dictated entirely by the injected `Verify` result, never an independent string-comparison shortcut) rather than only indirectly through `ApiKeyAuthenticationHandler`.
- **`SharedKernel.Security.Oidc.Tests` references `16.Testing/SharedKernel.Testing`** (WO-057/T-17) — the first `12.Security` test project to do so — for `InMemoryLogger<TCategoryName>`/`LoggerAssertions` to prove `OidcUserContext`/`OidcTenantProvider`'s structured security-audit `[LoggerMessage]` events (`12100`/`12101`) fire at the correct level with no raw claim value in the rendered message. Adding this reference required bumping `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Hosting` from `9.0.5` to `10.0.9` to avoid an `NU1605` downgrade (mirrors the identical pin already carried by `SharedKernel.Security.ApiKey.Tests`, which references `16.Testing` for the same reason).
- Step-up authentication tests (WO-058, P-375): a multi-value `amr` claim (discrete claims AND a single space-delimited value) parses correctly into `AuthenticationMethods`; `WasAuthenticatedWith` matches case-insensitively; an absent/malformed `auth_time` claim yields `AuthTime = null` without throwing; `IsAuthenticationFresherThan` is tested by passing an explicit `now` value in the test (never relying on real wall-clock time) to prove both the fresh-accept and stale-reject branches deterministically.
- DPoP tests (WO-058, P-376): a correctly-bound proof (valid `jwk` signature, matching `htm`/`htu`, fresh `iat`, unseen `jti` via a test double `IDpopProofReplayCache`) is accepted and resolves `IsSenderConstrained = true`; each failure mode (missing/malformed proof, `jkt` mismatch, expired `iat`, replayed `jti`) is tested independently; a non-opted-in host's existing bearer-only tests must be proven unaffected — run the pre-existing Oidc test suite unchanged against a host that never calls `RequireDpop<TReplayCache>()`.
- Mtls tests (WO-058, P-377): a validator-accepted certificate resolves `MtlsUserContext` correctly (`IdentityKind.ServicePrincipal`, `IsAuthenticated = true`, `UserId = Guid.Empty`); a rejected/absent certificate never resolves an authenticated context; a `cnf.x5t#S256` mismatch is rejected even when the underlying bearer token is otherwise valid — construct the mismatch scenario with two distinct test certificates, never a hand-computed "wrong" thumbprint string.
- Revocation tests (WO-058, P-379): a non-revoked token with the seam enabled still succeeds; a revoked token is rejected with the exact same failure shape/status code as an expired token (assert on the response, not an internal flag); a non-opted-in host is proven to never invoke `ITokenRevocationCheck` (e.g. via a test double that throws if called, asserting it is never hit); a throwing/timing-out revocation check is proven to fail closed (reject), not fail open.
- DPoP `ath` tests (WO-060, P-385): a proof otherwise valid but bound to a DIFFERENT access token's hash is rejected; a missing/malformed `ath` claim is rejected; the pre-existing T-22 correctly-bound-proof fixture still passes once `ath` validation is added — a regression against the four already-shipped bindings would be a real defect, not an acceptable trade-off.
- mTLS secure-default tests (WO-060, P-386, SHIPPED T-32/T-33): a default-constructed `MtlsAuthenticationOptions` rejects a self-signed certificate BEFORE `IMtlsCertificateValidator` ever runs (proves the framework-level pre-filter, not just the validator, now does real work); an explicitly opted-in options instance still accepts a self-signed certificate the validator approves, proving the opt-out path works. **Proving the ordering (not just the outcome) requires driving the REAL `Microsoft.AspNetCore.Authentication.Certificate` handler** — `MtlsAuthenticationHandler.HandleCertificateValidatedAsync` is only invoked from inside the framework's own `OnCertificateValidated` event, which never fires until the framework's `AllowedCertificateTypes`/chain-building pre-filter already passed; a test that calls `HandleCertificateValidatedAsync` directly (as `MtlsAuthenticationHandlerTests` does) *cannot* prove this ordering, since it bypasses the pre-filter entirely. The pattern that works, with no `TestServer`/Kestrel host needed: build a `ServiceCollection` via `AddMtlsAuthentication<TValidator>()` (overriding the registered validator afterward with a call-counting double — last DI registration wins), construct a bare `DefaultHttpContext`, set **`httpContext.Request.Scheme = "https"`** (the handler unconditionally `NoResult()`s a non-HTTPS request before it even inspects `Connection.ClientCertificate` — discovered empirically, not documented anywhere; omitting this makes a test pass for the WRONG reason since `AuthenticateResult.NoResult()` and `.Fail()` both read as `Succeeded == false`), assign `httpContext.Connection.ClientCertificate = cert` (the setter transparently creates and populates an `ITlsConnectionFeature`, so no live TLS handshake is needed), then call `httpContext.AuthenticateAsync(MtlsAuthenticationOptions.DefaultScheme)` directly — see `SharedKernel.Security.Mtls.Tests/Extensions/MtlsAuthenticationDefaultsTests.cs`.
- Algorithm-allowlist tests (WO-060, P-387, SHIPPED T-34/T-35): a token/proof signed with an algorithm outside the default allowlist — including a crafted `alg: none` token — is rejected before any signature/claim evaluation proceeds, for BOTH the JWT Bearer path and the DPoP proof path; a PS256/ES256-signed token/proof is accepted unchanged (no regression for the FAPI-compliant default case). For the JWT Bearer path, clone the REAL `TokenValidationParameters` wired by `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` (extracted via `IOptionsMonitor<JwtBearerOptions>`), neutralize only `ValidateIssuer`/`ValidateAudience` (which need live OIDC discovery, unavailable in a unit test), and drive the clone through `JsonWebTokenHandler.ValidateTokenAsync` directly — proves the actual production wiring, not a reimplementation of the allowlist check. Assert on `result.Exception is SecurityTokenInvalidAlgorithmException or SecurityTokenSignatureKeyNotFoundException`, not a single exact type: `Microsoft.IdentityModel.Tokens` may reject an out-of-allowlist algorithm during signing-key resolution (no candidate key is considered valid for a disallowed `alg`) rather than during the explicit algorithm-validation step, depending on internal validation order — both outcomes equally prove the token never reached genuine HMAC/signature verification.
- Revocation-caching tests (WO-060, P-388, SHIPPED T-36/T-37): a cached "not revoked" result avoids a second `IsRevokedAsync` call within `NotRevokedTtl`; a revoked result is cached under the shorter `RevokedTtl`, not the longer `NotRevokedTtl`, so it is never allowed to go as stale as a "not revoked" verdict; `WithRevocationCheckCaching<TCache>()` called before `WithRevocationCheck<TCheck>()` throws `InvalidOperationException` at registration time. Drive time via an injectable `Func<DateTimeOffset> now` closure captured by a hand-rolled `IRevocationCheckCache` double (mutate the closed-over variable between assertions) — never `Task.Delay` against a real clock.
- API-key rotation tests (WO-060, P-389, SHIPPED T-38/T-39): `ApiKeyRotationComparer.AnyMatch` against a multi-candidate key set is proven not to leak timing correlated with which key, or how many keys, matched — mirrors T-16's `ConstantTimeKeyComparer` verification technique (a hand-rolled `IHmacSigner` double proving every candidate is always compared); a rotation-window scenario (old key still valid, new key valid, both simultaneously accepted) resolves correctly. Unlike `ConstantTimeKeyComparer`, `ApiKeyRotationComparer` is public — its test does not need an `InternalsVisibleTo` grant. **The shipped `AnyMatch(string, IReadOnlyList<string>)` public overload took no injectable `IHmacSigner`** (unlike the internal `ConstantTimeKeyComparer.AreEqual`), making the "always evaluate every candidate, never short-circuit" property structurally unverifiable by a test without either a flaky wall-clock timing measurement (explicitly disallowed) or a testability seam. Resolved by adding a minimal, additive `internal AnyMatch(string, IReadOnlyList<string>, IHmacSigner)` overload that the public method now delegates to (passing a real `HmacSha256Signer`) — reachable from `SharedKernel.Security.ApiKey.Tests` via the package's existing `InternalsVisibleTo` grant, zero change to the public contract or default behavior. **This is the sanctioned pattern going forward**: when a static/stateless security-critical comparator needs its internal iteration/timing behavior proven and hardcodes its own cryptographic primitive, add a package-`internal` overload accepting that primitive rather than leaving the property unverifiable or resorting to timing measurement.
- Unit tests for `SharedKernel.Security.Totp` live in `12.Security/SharedKernel.Security.Totp/SharedKernel.Security.Totp.Tests/` *(WO-069, P-452, SHIPPED)*.
- Enrollment tests (WO-069, P-452, SHIPPED — T-40): `TotpEnrollmentService.GenerateEnrollment` produces a secret of the requested byte length, a `SecretBase32` that round-trips through `01.Core`'s `Base32.Decode`, a `ProvisioningUri` matching the expected `otpauth://totp/...` shape, and the requested count of unique recovery codes.
- Challenge tests (WO-069, P-452, SHIPPED — T-41/T-42): a valid, freshly-generated TOTP code (via `01.Core`'s `ITotpGenerator.GenerateCode` explicit-timestamp overload, never real wall-clock time) succeeds and records a challenge; an invalid code fails and records nothing; a replayed code fails on its second submission via `TotpVerifier`'s own composed replay check (never reimplemented in this package's own tests); `Guid.Empty` is rejected before any store/verifier call, proven via a call-counting double.
- Step-up tests (WO-069, P-452, SHIPPED — T-43–T-47): `TotpStepUpClaimsTransformation` skips an anonymous or `Guid.Empty`-subject principal without any store call; a principal with a fresh challenge gains the configured AMR claim, a stale or absent one does not; calling `TransformAsync` twice does not duplicate the claim (idempotency); the ORIGINAL `ClaimsIdentity` instance is proven unmutated — only the returned principal carries the new claim.
- Cross-package interop test (WO-069, P-452, SHIPPED — T-46): a `ClaimsPrincipal` transformed by `TotpStepUpClaimsTransformation`, fed into `.Oidc`'s REAL `OidcUserContext` constructor, resolves `WasAuthenticatedWith("otp") == true`. **`SharedKernel.Security.Totp.Tests` may reference `SharedKernel.Security.Oidc` for this one test** — a documented, test-project-only exception to the sibling-packages-never-reference-each-other rule (which governs production code, not test-project references), mirroring how `SharedKernel.Security.Oidc.Tests` already references `16.Testing` for a cross-cutting concern the main `.Oidc` project itself does not need.

---

## Documentation Plan

### WO-057 (P-372) — shipped, kept for reference

- **Domain-root `README.md`** (`12.Security/README.md`): `Overview` (thin abstractions / claims-first / no domain coupling philosophy) → `Packages` table → `Quick Start` → links to each package's own `README.md`. **Shipped** — populated per this structure (DOC-04).
- **`SharedKernel.Security.Abstractions/README.md`**: usage example per public type — `IUserContext`/`HasRole`/`HasPermission`, `ITenantProvider`, `IdentityKind`, `AnonymousUserContext`, `SystemUserContext`, `SecurityClaimTypes`. **Shipped** (DOC-05).
- **`SharedKernel.Security.Oidc/README.md`**: usage example per public type, plus the `ClaimMapping` sub-options and the four end-to-end recipes (`05.Application` `IAuthorizationContext` bridge, `06.Persistence` `TenantedDbContext` wiring, `07.Messaging` `ITenantContextAccessor` bridge, background-execution `SystemUserContext` registration). **Shipped** (DOC-05/DOC-07).
- **`SharedKernel.Security.ApiKey/README.md`**: pre-shared-key/machine-client disclaimer. **Shipped** (DOC-06).

### WO-058 (P-375/P-376/P-377/P-379) — shipped, kept for reference

- **`SharedKernel.Security.Oidc/README.md` — step-up authorization recipe**: a fifth end-to-end recipe alongside the existing four — gating a high-risk operation (e.g. a funds-transfer command) on `WasAuthenticatedWith("mfa")` + `IsAuthenticationFresherThan(...)`, wired through the same `IAuthorizationContext` bridge the first WO-057 recipe already established. **Shipped** (DOC-10).
- **`SharedKernel.Security.Oidc/README.md` — DPoP Quick Start**: opt-in `.RequireDpop<TReplayCache>()`, a minimal `IDpopProofReplayCache` implementation sketch, and explicit FAPI 2.0 framing (why DPoP over/alongside mTLS sender-constraining for this platform's "developer friendly" requirement). **Shipped** (DOC-11).
- **`SharedKernel.Security.Oidc/README.md` — revocation-check Quick Start**: opt-in `.WithRevocationCheck<TCheck>()` with an explicit latency/availability trade-off callout block — this is the one capability in the domain with a genuine per-request cost. **Shipped** (DOC-12).
- **`SharedKernel.Security.Mtls/README.md`**: states up front, mirroring `.ApiKey`'s disclaimer pattern, that this package performs no certificate issuance, CA management, or revocation checking (CRL/OCSP) — those remain the consuming service's own concern. **Shipped** (DOC-09) — rewritten end to end, replacing the stale Scaffold-era stub.
- **Domain-root `12.Security/README.md` refresh**: Packages table gains the fourth `SharedKernel.Security.Mtls` row; Quick Start gains an `AddMtlsAuthentication<TValidator>()` snippet alongside the existing three. **Shipped** (DOC-13).
- **`SharedKernel.Security.Abstractions/README.md` refresh**: usage example for the new step-up members (`AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`/`WasAuthenticatedWith`/`IsAuthenticationFresherThan`) and `IsSenderConstrained`. **Shipped** alongside DOC-08–DOC-13 (not its own DOC-ID, folded into the same docs pass).

### WO-060 (P-385/P-386/P-387/P-388/P-389/P-392) — shipped, kept for reference

- **`SharedKernel.Security.Oidc/README.md` — DPoP `ath` note**: the existing DPoP Quick Start section (DOC-11) gained a paragraph documenting the `ath` binding as a fifth check alongside `htm`/`htu`/`iat`/`jkt`/`jti` — no new section, folded into the existing one. A companion `## Signing-algorithm allowlist` section was added alongside it, documenting `Jwt.ValidAlgorithms` with the same widening guidance as the CLAUDE.md note below. **Shipped** (DOC-14).
- **`SharedKernel.Security.Mtls/README.md` + `12.Security/CLAUDE.md` — corrected defaults**: `AllowedCertificateTypes = Chained`/`RevocationMode = Offline` documented as the shipped defaults, replacing the prior `All`/`NoCheck` documentation, with a "Breaking change in `2.0.0`" callout, the explicit opt-out path, and its CAPS-worded trust-weakening warning. **Shipped** (DOC-15) — `12.Security/CLAUDE.md`'s own `MtlsAuthenticationOptions`/D-36/C-39 content already carried this from the Core phase; only the README needed the fix.
- **`12.Security/CLAUDE.md` — `ValidAlgorithms` FAPI 2.0 baseline note**: already documented the allowlist as a FAPI 2.0 baseline control from the Core phase, with explicit guidance for a non-FAPI/RS256-signing IdP consumer on widening the allowlist after upgrading — verified accurate, no edit needed. **Shipped** (DOC-16).
- **`SharedKernel.Security.Oidc/README.md` — revocation-caching recipe**: a worked `IMemoryCache`-backed example and a Redis-backed example (via `02.Caching.Abstractions`'s `ICacheService`/`CachePolicy.For`, at the consumer's own composition root), both implementing `IRevocationCheckCache`, added to the existing Token Revocation Quick Start (DOC-12) — with an explicit callout against unifying `RevokedTtl`/`NotRevokedTtl`. **Shipped** (DOC-17).
- **`SharedKernel.Security.ApiKey/README.md` — rotation-window recipe**: issue new key → dual-valid window (`ApiKeyRotationComparer.AnyMatch` against both keys) → revoke old key, as a new section referencing the shipped `Samples/RotationWindowApiKeyValidatorSample.cs` (C-46) rather than inventing a divergent second version. **Shipped** (DOC-18).
- **Multi-tenant JWT claim trust-boundary explanation**: content locked at Design phase (WO-060, D-42) in the `## Domain Invariants` section above, adapted into `SharedKernel.Security.Oidc/README.md`'s own `## Multi-tenant JWT claim trust boundary` section, cross-linked with the DOC-17/DOC-18 recipes from the same documentation pass rather than shipped as three separate NuGet repacks. **Shipped** (DOC-19).

### WO-069 (P-452) — shipped, kept for reference

- **XML doc coverage** on every new `SharedKernel.Security.Totp` public type plus the new `SecurityClaimTypes.AuthenticationMethod` constant. **Shipped** (DOC-20).
- **New `SharedKernel.Security.Totp/README.md`**: Quick Start for `AddTotpStepUp<TChallengeStore>()` (including the required call ordering relative to `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` and `AddSharedKernelCryptography`), the `.Oidc`-only scope boundary, and the `AuthTime` non-interaction rule — both adapted from the `## Domain Invariants` content locked at D-53/D-54. **Shipped** (DOC-21).
- **Enrollment recipe** (`SharedKernel.Security.Totp/README.md`): `TotpEnrollmentService.GenerateEnrollment` → encrypt the raw secret at rest via `01.Core`'s `ISymmetricEncryptionService` → hash each recovery code at rest via `01.Core`'s `IOneWayHasher` — the explicit "documented recipe for wiring backup/recovery codes from P-451" acceptance criterion from P-452 itself. **Shipped** (DOC-22).
- **Challenge recipe** (`SharedKernel.Security.Totp/README.md`): a worked, non-production sample (`Samples/TotpChallengeRecipeSample.cs`) wiring `TotpChallengeService.VerifyAsync` to a consumer's own decrypted-secret lookup, plus a recovery-code fallback calling `RecordStepUpAsync` after the consumer's own `IOneWayHasher.Verify` match — mirrors the `.ApiKey`'s `Samples/RotationWindowApiKeyValidatorSample.cs` non-production-sample precedent (C-46) rather than inventing a divergent shape. **Shipped** (DOC-23).
- **Domain-root `12.Security/README.md` refresh**: Packages table gains the fifth `SharedKernel.Security.Totp` row; Quick Start gains an `AddTotpStepUp<TChallengeStore>()` snippet alongside the existing four; Ordering rules gained a step covering `AddTotpStepUp`'s placement. **Shipped** (DOC-24).

---

## Changelog

> Maintained by the security domain agent. One line per significant change.

- [2026-06-02] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules
- [2026-06-02] SK.12.Core complete — test construction patterns added to Test Rules (authenticated vs unauthenticated ClaimsPrincipal; DI via ServiceCollection) (security-phase-implementer)
- [2026-06-02] SK.12.Published complete — no brain changes warranted; Published phase was verification-only (build, pack, test all green) (sync-brain)
- [2026-08-13] WO-057 (P-366–P-372) dispatched — brain refreshed to describe post-phase state (not yet implemented, all `○`/`◐` in state-map): (1) claim-type resolution corrected to match modern short-name OIDC defaults via new `SecurityOptions.ClaimMapping`, with `SecurityClaimTypes.Email`/`.Role` demoted to legacy-shape reference constants and a defensive role-claim reader added (P-366); (2) `IdentityKind` enum (`Anonymous`/`User`/`ServicePrincipal`/`System`) added to `IUserContext`, correcting the `UserId`/`IsAuthenticated` invariant so a valid client-credentials token is no longer conflated with a rejected one (P-367, breaking interface addition); (3) `Permissions`/`HasPermission` fine-grained scope surface added, mirroring `Roles`/`HasRole` (P-368, breaking interface addition); (4) `SystemUserContext` sentinel added as a peer to `AnonymousUserContext` for background-execution hosts, shipping no DI wiring (P-369); (5) new sibling provider package `SharedKernel.Security.ApiKey` for pre-shared-key/machine-client authentication, built on `01.Core/SharedKernel.Cryptography`'s constant-time primitives, composing alongside JWT Bearer via a policy/forwarding scheme (P-370); (6) this domain's first `[LoggerMessage]`-authored structured logging under the platform's WO-041 convention, `EventId` range `12000`–`12999` (P-371); (7) domain-root `README.md` and package README refresh queued (P-372). Packages table, Technology Stack, Interface Contracts (`IUserContext`/`AnonymousUserContext`/new `IdentityKind`/`SystemUserContext`/`SecurityClaimTypes`/`OidcUserContext`/`SecurityOptions`/new `SharedKernel.Security.ApiKey` section), a new Logging Conventions section, Implementation Rules, DI Registration, AOT Compatibility, and Test Rules all updated (security-arch-planner, WO-057)
- [2026-08-13] SK.12.Tests (WO-057, T-07–T-17) closed — three new Test Rules patterns documented: `AddAzureB2CAuthentication` DI tests need `IConfiguration` registered directly in the container, `ConstantTimeKeyComparer` needs `InternalsVisibleTo` to test directly, `SharedKernel.Security.Oidc.Tests` now references `16.Testing` for logging assertions (security-phase-implementer)
- [2026-08-13] SK.12.Docs (WO-057, DOC-03–DOC-07) closed — docs-only session, zero `.cs` changes. DOC-03 confirmed full XML doc coverage already existed for every WO-057 public type across all three packages (no gaps found). DOC-04 authored the previously-empty `12.Security/README.md` domain-root orientation. DOC-05 refreshed `SharedKernel.Security.Abstractions/README.md` (added `IdentityKind`, `Permissions`/`HasPermission`, `SystemUserContext`, the `SecurityClaimTypes` legacy-vs-active-lookup-key callout) and `SharedKernel.Security.Oidc/README.md` (added the `ClaimMapping` config section and role/permission-parsing usage notes). DOC-06 authored the new `SharedKernel.Security.ApiKey/README.md`. DOC-07 authored all four end-to-end recipes inside `SharedKernel.Security.Oidc/README.md` — `05.Application`'s `IAuthorizationContext` bridge (`SharedKernel.Application.Behaviors.Authorization.IAuthorizationContext`: `IsAuthorizedAsync`/`AllOf`/`AnyOf`, no shipped in-repo bridge implementation existed to reuse), `06.Persistence`'s `TenantedDbContext` wiring (5-arg protected ctor incl. `ITenantProvider`; the global tenant filter is applied via `Expression.Constant(this, GetType())` so EF Core rebinds it per executing instance, never a captured stale provider), `07.Messaging`'s `ITenantContextAccessor` bridge (single `Guid? TenantId` property, wired via `MessagingBusBuilder.WithTenantContext<TAccessor>()`; the recipe mirrors the interface's own XML-doc-embedded example), and a background-execution host recipe registering `SystemUserContext.Instance` after `AddSharedKernelSecurity`. All three cross-domain interfaces were verified against real shipped source (direct file reads) before any recipe code was written — never assumed from memory or another domain's prose. All three packages re-built 0 warnings/0 errors after every doc edit (security-phase-implementer, state-map-phase)
- [2026-08-13] SK.12.Core (WO-057, C-10–C-24) implemented — brain corrected to match shipped code rather than the original target-state draft: `OidcUserContext`/`OidcTenantProvider` constructor signatures documented precisely (`ClaimMappingOptions` resolved from `IOptions<SecurityOptions>.Value.ClaimMapping` by the DI factory, plus an optional `ILogger<T>` for P-371 audit logging — not the originally-drafted bare `IOptions<SecurityOptions>`); `SharedKernel.Security.ApiKey` section expanded with the real `ApiKeyValidationResult`/`ApiKeyUserContext` shapes, the constant-time comparison's actual scope clarified (header-vs-query PRESENTED-value agreement, via `ConstantTimeKeyComparer`/`IHmacSigner` — never a stored secret, which this package never holds), and `AddApiKeyAuthentication`'s real `IUserContext` composition mechanism documented (a `ServiceDescriptor`-capture decorator over whatever `IUserContext` factory `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` already registered — never a `SharedKernel.Security.Oidc` type reference — plus a `fallbackAuthenticationScheme` parameter working around this package's deliberate exclusion of the `Microsoft.AspNetCore.Authentication.JwtBearer` NuGet dependency); Logging Conventions table now names the three shipped `SecurityLogEvents` methods with their exact signatures. Cross-domain fallout (four other `IUserContext` implementers outside this domain updated for the two new interface members) is out of this brain's scope — recorded in `06.Persistence`/`16.Testing`'s own state-maps instead. 107 tests passing (29 Abstractions + 46 Oidc + 32 ApiKey) (security-phase-implementer, sync-brain)
- [2026-08-13] WO-058 (P-375, P-376, P-377, P-379) dispatched — brain refreshed to describe post-phase state (not yet implemented, all `○`/`◐` in state-map): (1) a step-up-authentication signal surface — `IUserContext.AuthenticationMethods`/`.AuthContextClassReference`/`.AuthTime` (mapping OIDC `amr`/`acr`/`auth_time`) plus `WasAuthenticatedWith`/`IsAuthenticationFresherThan` convenience members, the latter deliberately taking `now` as an explicit parameter rather than calling `DateTimeOffset.UtcNow` internally — for PSD2/FFIEC/PCI-DSS-style high-risk-operation gating (P-375, additive-but-breaking `IUserContext` members, same treatment as `IdentityKind`/`Permissions`); (2) opt-in DPoP (RFC 9449) sender-constrained access-token validation in `.Oidc` via a new `SecurityAuthenticationBuilder.RequireDpop<TReplayCache>()` entry point (both `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` now return this builder instead of a bare `IServiceCollection`, backward-compatible for ignored return values), a consumer-pluggable `IDpopProofReplayCache` seam mirroring `IApiKeyValidator`'s never-dictates-storage precedent, and a new `IUserContext.IsSenderConstrained` member (P-376, chosen as this domain's PRIMARY sender-constraining mechanism over mTLS for its lower developer-integration burden — matching the platform's "developer friendly" requirement); (3) a fourth sibling provider package, `SharedKernel.Security.Mtls`, for regulated Open Banking/PSD2-style mutual-TLS client-certificate authentication (`IMtlsCertificateValidator`, `MtlsValidationResult`, `MtlsUserContext` — deliberate mirrors of the `.ApiKey` shapes) plus RFC 8705 certificate-bound access tokens (`cnf.x5t#S256` check, decoupled from `.Oidc` via claims inspection only, never a type reference) (P-377); (4) an opt-in, fail-closed `ITokenRevocationCheck` seam (RFC 7662-shaped) via the same `SecurityAuthenticationBuilder.WithRevocationCheck<TCheck>()`, disabled by default because it is the one capability in this domain with a genuine per-request latency/availability cost (P-379). Packages table (`.Mtls` row), Technology Stack, Interface Contracts (`IUserContext` step-up/sender-constrained members, sentinel updates across `AnonymousUserContext`/`SystemUserContext`/`ApiKeyUserContext`, new `SecurityAuthenticationBuilder`/DPoP/Revocation subsections, new `SharedKernel.Security.Mtls` public-surface section), Logging Conventions (new EventIds `12102`/`12103`/`12300`, `12300`–`12399` reserved for Mtls), Implementation Rules (6 new rules), DI Registration (new snippets), AOT Compatibility (5 new notes), Test Rules (4 new coverage-requirement bullets + new Mtls test-project line), and Documentation Plan (restructured into a shipped-WO-057 subsection plus a new WO-058 subsection for DOC-08–DOC-13) all updated (security-arch-planner, WO-058)
- [2026-08-14] SK.12.Core + SK.12.Tests (WO-058, C-25–C-37 / T-18–T-28) implemented — all four `design-locked` markers removed throughout the file now that every P-375/P-376/P-377/P-379 member/type is real, compiled, tested code. **Genuine correction found during implementation, not assumed from the pre-written draft:** `Microsoft.AspNetCore.Authentication.Certificate` is NOT part of the `Microsoft.AspNetCore.App` shared framework — verified directly against the installed shared-framework directories on disk (no `Certificate.dll` in any of them); `SharedKernel.Security.Mtls.csproj` now carries a real `PackageReference` (pinned `10.0.0`, matching `.Oidc`'s `JwtBearer` pin) alongside the `Microsoft.AspNetCore.App` `FrameworkReference` (kept only for `Http`/`DependencyInjection`/`Logging.Abstractions`); Technology Stack table corrected accordingly. `SecurityAuthenticationBuilder` (new, `Extensions/`) implements `IServiceCollection` by forwarding to the wrapped collection — a source-compatible superset of the prior bare-`IServiceCollection` return type. `DpopProofValidator`/`RevocationCheckRunner` (new, `Dpop/`/`Revocation/`) both resolve every DI dependency from `HttpContext.RequestServices` inside the per-request `OnTokenValidated` delegate body — never from the root-scoped `IServiceProvider` a `Configure<TDep>` overload would inject, which would misresolve the Scoped `IDpopProofReplayCache`/`ITokenRevocationCheck` seams. `IsSenderConstrained` is bridged from DPoP validation (which runs inside the JWT Bearer pipeline) into `OidcUserContext` construction (which happens later, from the same principal, via the DI factory) by a new synthetic marker claim, `Dpop.DpopClaimTypes.SenderConstrained`, stamped onto the validated `ClaimsIdentity` on success. `SharedKernel.Security.Mtls` built out in full — `MtlsAuthenticationHandler` wraps `Microsoft.AspNetCore.Authentication.Certificate`'s `OnCertificateValidated` event, relaxing the framework's own default `AllowedCertificateTypes`/`RevocationMode` policy (`All`/`NoCheck`) so full trust/revocation control genuinely lands with the injected `IMtlsCertificateValidator`; the RFC 8705 binding check uses a new `ConstantTimeThumbprintComparer` (`IHmacSigner`-based), mirroring `.ApiKey`'s `ConstantTimeKeyComparer` byte-for-byte. New `InternalsVisibleTo` grants added to both `.Oidc.csproj`/`.Mtls.csproj` (mirroring the pre-existing `.ApiKey` one) so `DpopProofValidator`/`RevocationCheckRunner`/`MtlsAuthenticationHandler`/`ConstantTimeThumbprintComparer` can be tested directly. Three new Implementation Rules added documenting the request-scoped-DI-resolution pitfall, the wrap-previous-handler `OnTokenValidated` chaining pattern, and the synthetic-marker-claim bridging technique. Cross-domain `IUserContext` fallout (six new interface members) fixed in `06.Persistence.EfCore` (`NoOpUserContext` + two test fixtures) and `16.Testing` (`FakeUserContext`, `TestSharedKernelDbContext`'s private `NoOpUserContext`) — out of this brain's own scope, recorded in those domains' own state-maps. 196/196 tests passing across all four `12.Security` packages (41 Abstractions + 49 ApiKey + 94 Oidc + 12 Mtls), plus 358/358 `06.Persistence.EfCore.Tests` and 904/904 `16.Testing.SelfTests` confirming zero regressions (security-phase-implementer, sync-brain)
- [2026-08-14] SK.12.Docs (WO-058, DOC-08–DOC-13) closed — docs-only session, zero `.cs` changes. DOC-08 re-verified full XML doc coverage on every new WO-058 public type across all four packages, no gaps found. DOC-09 rewrote `SharedKernel.Security.Mtls/README.md` end to end, replacing the stale Scaffold-era stub. DOC-10–DOC-12 added a fifth end-to-end recipe (step-up authorization via `WasAuthenticatedWith`/`IsAuthenticationFresherThan`) plus DPoP and token-revocation Quick Starts (the latter led with an explicit latency/availability trade-off callout) to `SharedKernel.Security.Oidc/README.md`. DOC-13 updated the domain-root `README.md` for the fourth sibling provider package. `SharedKernel.Security.Abstractions/README.md` also refreshed with the step-up/`IsSenderConstrained` members. The "Documentation Plan" section's WO-058 subsection re-headed from "design for the queued Docs-phase content" to "shipped, kept for reference", mirroring the WO-057 subsection. 196/196 tests passing, 0 regressions. `SK.12.Docs` now 13/13 `●` (security-phase-implementer, sync-brain)
- [2026-08-14] SK.12.Published (WO-058, PUB-12–PUB-16) closed — verification/packaging-only, no new types/DI patterns/test patterns/AOT constraints. `SharedKernel.Security.Abstractions`/`SharedKernel.Security.Oidc` bumped `2.0.0`→`3.0.0` (major, breaking — six new `IUserContext` step-up members: `AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`/`IsSenderConstrained`/`WasAuthenticatedWith`/`IsAuthenticationFresherThan`); `SharedKernel.Security.ApiKey` bumped `1.0.0`→`1.1.0` (minor, non-breaking — `ApiKeyUserContext` implements the new members as empty/null/false); `SharedKernel.Security.Mtls` stays `1.0.0` (first release, already shipped every WO-058 member from Core). All four packages build 0 errors in Release, pack clean to `.nupkg`+`.snupkg` (no `NU5039`), full suite green 196/196 (41 Abstractions + 94 Oidc + 49 ApiKey + 12 Mtls). No package pushed to a NuGet feed — local pack artifacts only. Closes WO-058 (P-375/P-376/P-377/P-379) end to end — all six `SK.12.*` phase keys are now `●` for both WO-057 and WO-058 (security-phase-implementer, sync-brain)
- [2026-08-17] WO-060 (P-385, P-386, P-387, P-388, P-389, P-392) dispatched — brain refreshed to describe post-phase state (not yet implemented, all `○`/`◐` in state-map, every new member tagged `○ QUEUED` inline). A direct source audit of the shipped, 196/196-tests-green WO-058 output found six real gaps, none hypothetical: (1) `DpopProofValidator` never reads the RFC 9449 §4.3-mandated `ath` (access-token-hash) claim despite faithfully implementing `htm`/`htu`/`iat`/`jkt`/`jti` — a fifth binding added, reusing the existing `DpopProofRejected` (EventId 12102) failure path with a new `"AthMismatch"` `failureReason` rather than a new rejection surface (P-385); (2) `MtlsAuthenticationOptions` shipped defaulting `AllowedCertificateTypes = All`/`RevocationMode = NoCheck` — WEAKER than plain ASP.NET Core's own `CertificateAuthenticationOptions` defaults (`Chained`/`Online`) — corrected to `Chained`/`Offline` (the latter chosen over `Online` to avoid imposing a hidden outbound-network dependency as a shared-kernel default), with an explicit, CAPS-documented opt-out for a genuine private-PKI need (P-386); (3) neither the JWT Bearer nor DPoP validation path restricted accepted JWS signing algorithms — a real, undocumented FAPI 2.0 baseline-control gap for a domain whose WO-058 pass was explicitly scoped "fintech/FAPI 2.0-grade"; new `SecurityOptions.Jwt.ValidAlgorithms`/`DpopOptions.ValidAlgorithms` (both default `["PS256", "ES256"]`) close it — DELIBERATELY BREAKING, since most real-world IdPs (Entra ID, Auth0, Okta) sign with RS256 by default and a consumer must now explicitly widen the allowlist after upgrading (P-387); (4) `ITokenRevocationCheck` has zero caching despite the domain's own design docs already flagging it as "the one capability with a genuine per-request cost" — new opt-in `IRevocationCheckCache`/`RevocationCheckCacheOptions`/`CachingTokenRevocationCheck` decorator seam added, never referencing `02.Caching`, with a deliberately short `RevokedTtl` (5s) so a genuine revocation is never masked for long (P-388); (5) `SharedKernel.Security.ApiKey` offered no guidance for key rotation without a hard-cutover outage — a new PUBLIC `ApiKeyRotationComparer.AnyMatch` type added (deliberately distinct from the existing INTERNAL `ConstantTimeKeyComparer`, which a consumer's own `IApiKeyValidator` assembly cannot reach) that always evaluates every candidate key, never short-circuiting, so comparison timing never leaks which/how many keys matched (P-389); (6) the multi-tenant JWT claim trust boundary is genuinely undocumented anywhere in this domain's brain or READMEs — a docs-only pass bundling the trust-boundary explanation with the P-388/P-389 recipes into one documentation session rather than three separate repacks (P-392). Packages table row notes (`.Oidc`/`.ApiKey`/`.Mtls`), Interface Contracts (new `MtlsAuthenticationOptions` subsection with the corrected-defaults NOTE, `DpopOptions.ValidAlgorithms`, the `ath` binding inside `DpopProofValidator`, the full Revocation caching-seam subsection, `SecurityOptions.Jwt.ValidAlgorithms`, `ApiKeyRotationComparer.AnyMatch`), Logging Conventions (new EventId `12104`, widened `12102` `failureReason` vocabulary), Implementation Rules (5 new rules), DI Registration (4 new usage snippets), AOT Compatibility (5 new notes), Test Rules (5 new coverage-requirement bullets), and Documentation Plan (new WO-060 subsection for DOC-14–DOC-19) all updated. Version-bump plan recorded: `SharedKernel.Security.Oidc` `3.0.0`→`4.0.0` and `SharedKernel.Security.Mtls` `1.0.0`→`2.0.0` both breaking (the first time this domain's breaking-bump policy applies to a DEFAULT-VALUE tightening rather than an added `IUserContext` interface member), `SharedKernel.Security.ApiKey` `1.1.0`→`1.2.0` additive, `SharedKernel.Security.Abstractions` untouched at `3.0.0` — the first WO in this domain's history shipping zero `IUserContext`-breaking additions (security-arch-planner)
- [2026-08-18] SK.12.Scaffold (WO-060, SC-25–SC-30) closed — no brain edits warranted. All six shipped stubs (`DpopProofValidator`'s new `ath`-binding branch/skeleton method, `IRevocationCheckCache`, `RevocationCheckCacheOptions`, `CachingTokenRevocationCheck`, `SecurityOptions.Jwt.ValidAlgorithms`, `DpopOptions.ValidAlgorithms`, `ApiKeyRotationComparer.AnyMatch`) match the shapes already fully documented (with correct `○ QUEUED`/"not yet implemented" status wording) in Interface Contracts/Implementation Rules/AOT Compatibility/Test Rules by `security-arch-planner`'s WO-060 dispatch — verified line-by-line against the shipped `.cs` files, not assumed. No new interface shape, DI pattern, JWT/OIDC config decision, test pattern, or AOT constraint was introduced this session: `IsRevokedAsync`/`AnyMatch` throw `NotImplementedException` (unreachable stubs, nothing wires them yet), `ValidateAccessTokenHash` is a no-op returning `true`, and both `ValidAlgorithms` properties are inert until `SK.12.Core` wires them in. 94/94 `SharedKernel.Security.Oidc.Tests` + 49/49 `SharedKernel.Security.ApiKey.Tests` pre-existing tests still green, 0 regressions (security-phase-implementer, sync-brain)
- [2026-08-18] SK.12.Core (WO-060, C-38–C-48) closed — every `○ QUEUED`/`STATUS: ○ QUEUED` marker tied to these 11 tasks corrected to `SHIPPED` across Interface Contracts (`SecurityOptions.Jwt.ValidAlgorithms`, `DpopProofValidator`'s `ath`/algorithm-allowlist checks, `DpopOptions.ValidAlgorithms`, `MtlsAuthenticationOptions`'s two corrected defaults, the Revocation caching-seam subsection's header/status note, `ApiKeyRotationComparer.AnyMatch`) and the Logging Conventions table (`12102`'s widened `failureReason` vocabulary, the new `12104` row) — verified line-by-line against the shipped `.cs` files, not assumed. Four `○ QUEUED` inline comments in the DI Registration usage-snippet block (`WithRevocationCheckCaching`, the `ValidAlgorithms`-widening snippet, the mTLS self-signed opt-back-in snippet, the `ApiKeyRotationComparer.AnyMatch` snippet) also corrected — code samples were already accurate, only the status annotation was stale. No new public interface shape was introduced (every C-38–C-48 type already existed as a Scaffold-phase stub); genuinely new implementation patterns worth calling out: (1) `ConfigureAlgorithmRejectionLogging` is this domain's first `OnAuthenticationFailed` chaining helper (mirrors the existing `OnTokenValidated` wrap-previous-handler pattern used by `RequireDpop`/`WithRevocationCheck`, extended to a second JWT Bearer event); (2) `WithRevocationCheckCaching<TCache>()` is the SECOND use of the `ServiceDescriptor`-capture decorator technique inside `.Oidc` itself (previously only `.ApiKey`/`.Mtls` used it, and only for `IUserContext` — this is the first time it decorates a different interface, `ITokenRevocationCheck`, via `ActivatorUtilities.CreateInstance` rather than a captured factory delegate, since the captured registration is type-based not factory-based). C-48's audit confirmed no Mtls test relies on the old permissive defaults (the unit tests bypass ASP.NET Core's own `CertificateAuthenticationHandler` pre-filter stage entirely) and no Oidc test besides the DPoP suite signs a JWT with any algorithm — only `DpopProofValidatorTests.cs` needed a fix (a shared default access token so pre-existing proofs' newly-mandatory `ath` claim matches, zero call-site changes needed). Full regression: 94+1skip Oidc, 49+1skip ApiKey, 12/12 Mtls, 0 failed, 0 regressions. `SK.12.Core` now 48/48 `●` (security-phase-implementer, sync-brain)
- [2026-08-18] SK.12.Docs (WO-060, DOC-14–DOC-19) closed — docs-only session, zero `.cs` files touched. Verified against shipped source rather than assumed: DOC-14 (DPoP `ath` binding) and DOC-16 (`Jwt.ValidAlgorithms` FAPI 2.0 baseline) found `12.Security/CLAUDE.md`'s own Interface Contracts content already `SHIPPED` from the Core-phase sync-brain pass — no CLAUDE.md edit needed for either; a companion `ath`-binding paragraph and a new "Signing-algorithm allowlist" section were still added to `SharedKernel.Security.Oidc/README.md` per the Documentation Plan's stated scope. DOC-15 found the opposite: CLAUDE.md's `MtlsAuthenticationOptions` corrected-defaults documentation was already accurate, but `SharedKernel.Security.Mtls/README.md` was genuinely stale — still documenting the pre-WO-060 `CertificateTypes.All`/`X509RevocationMode.NoCheck` defaults as current, including a DI-registration code sample that explicitly set them "// default" — rewritten with the corrected `Chained`/`Offline` defaults, a "Breaking change in `2.0.0`" callout, and an explicit CAPS-warned opt-out path. DOC-17 added a revocation-caching recipe to `SharedKernel.Security.Oidc/README.md` — an `IMemoryCache`-backed example and a Redis-backed example via `02.Caching.Abstractions`'s `ICacheService`/`CachePolicy.For` (both wired at the consumer's own composition root; this package still never references `02.Caching`), with an explicit warning against unifying `RevokedTtl`/`NotRevokedTtl`, which would reintroduce the exact failure mode the two-TTL seam exists to avoid. DOC-18 added a rotation-window recipe to `SharedKernel.Security.ApiKey/README.md`, referencing the already-shipped `Samples/RotationWindowApiKeyValidatorSample.cs` (C-46) rather than authoring a second, divergent example. DOC-19 added a "Multi-tenant JWT claim trust boundary" section to `SharedKernel.Security.Oidc/README.md`, adapted from the `## Domain Invariants` content already locked at D-42, cross-linking the DOC-17/DOC-18 recipes; the Domain Invariants STATUS line and the Documentation Plan's WO-060 subsection were both updated from "queued"/"design-locked" to "shipped" to match. One item beyond the six DOC tasks, added to this same CLAUDE.md edit pass per explicit instruction: a new Test Rules bullet referencing `16.Testing`'s `SecurityTestContextBuilder` (`.Build()`/`.BuildUserContext()`) as the recommended pattern for a *consuming service's* own test fixtures — this domain sits below `16.Testing` in the layering order and cannot reference it itself, so the guidance is explicitly scoped to downstream test projects, never this domain's own `.Tests` fixtures. This closes the one outstanding criterion that had left root Phase Backlog `P-382` open. `SK.12.Docs` now 19/19 `●` (security-phase-implementer, sync-brain)
- [2026-08-18] SK.12.Tests (WO-060, T-29–T-39) closed — all six "SHIPPED" markers in Test Rules replaced with the actually-verified assertion techniques. Two genuinely new, non-obvious testing discoveries recorded: (1) proving the mTLS secure-by-default ORDERING claim (T-32) is impossible via `MtlsAuthenticationHandlerTests`' existing direct-call pattern, since that bypasses the real framework pre-filter entirely — the correct technique drives the actual `Microsoft.AspNetCore.Authentication.Certificate` handler via a bare `DefaultHttpContext`/`httpContext.AuthenticateAsync(scheme)`, and critically requires `httpContext.Request.Scheme = "https"` (the handler silently `NoResult()`s any non-HTTPS request before even inspecting `Connection.ClientCertificate` — undocumented anywhere, discovered empirically; omitting it makes a rejection test pass for the wrong reason); (2) `ApiKeyRotationComparer.AnyMatch`'s shipped public signature took no injectable `IHmacSigner`, making its timing-safety property unverifiable without either flaky wall-clock measurement or a testability seam — resolved by adding a minimal, additive `internal AnyMatch(string, IReadOnlyList<string>, IHmacSigner)` overload (public API/behavior unchanged) reachable via the package's existing `InternalsVisibleTo` grant, now the sanctioned pattern for this class of problem. Also: JWT-Bearer-path algorithm-allowlist rejection can surface as either `SecurityTokenInvalidAlgorithmException` or `SecurityTokenSignatureKeyNotFoundException` depending on `Microsoft.IdentityModel.Tokens`' internal validation order — tests must assert on either, not one exact type. Full regression: 109/109 Oidc, 14/14 Mtls, 61/61 ApiKey, 0 failed, 0 regressions. `SK.12.Tests` now 39/39 `●` (security-phase-implementer, sync-brain)
- [2026-08-26] WO-069 (P-452) dispatched from arch-lead — a fifth sibling provider package, `SharedKernel.Security.Totp`, closing WO-058/WO-062's half-built second-factor story. Depends on `01.Core`'s P-451 (RFC 6238/4226 TOTP/HOTP primitive, planned the same session inside `SharedKernel.Cryptography` — design-locked, Core implementation not yet shipped), so this domain composes rather than reimplements: `TotpEnrollmentService`/`TotpChallengeService` are thin orchestrators over `01.Core`'s `ISecureRandomGenerator`/`Base32`/`TotpProvisioningUri`/`RecoveryCodeGenerator`/`TotpVerifier`. The central design problem — making a TOTP challenge verified by THIS package observable through `IUserContext.WasAuthenticatedWith`/`AuthenticationMethods` with zero changes in `.Oidc` or `14.Presentation` — is solved via a new `TotpStepUpClaimsTransformation : IClaimsTransformation` (framework-provided, scheme-agnostic, runs during `AuthenticateAsync` BEFORE any `IUserContext` DI factory first resolves), which stamps a synthetic `amr`-shaped claim onto the current principal after consulting a consumer-supplied `ITotpChallengeStore` seam (mirrors `IDpopProofReplayCache`/`ITokenRevocationCheck`'s never-dictates-storage precedent) for a challenge recorded within a configurable freshness window; `.Oidc`'s EXISTING defensive amr-claim reader (WO-058/P-375) then observes it automatically — the SAME sanctioned "stamp a synthetic marker claim, read it generically downstream" mechanism this domain already used for DPoP (WO-058), reapplied via a framework hook instead of `JwtBearerEvents.OnTokenValidated` (which only `.Oidc` may register). `SharedKernel.Security.Totp` never references `.Oidc`/`.ApiKey`/`.Mtls` — the sibling-packages-never-reference-each-other rule now covers a fourth sibling. Two new Domain Invariants locked: (1) TOTP step-up composes ONLY with `.Oidc`'s `OidcUserContext` — `.ApiKey`/`.Mtls` never read amr claims and always carry `UserId = Guid.Empty`, which the new hard `Guid.Empty` guard in `TotpChallengeService`/`TotpStepUpClaimsTransformation` skips before any store call, since machine-credential `ServicePrincipal` identities are not the target audience for a human second factor; (2) a TOTP step-up NEVER modifies `AuthTime`/`IsAuthenticationFresherThan` — `[RequireFreshAuthentication]` and `[RequireAuthenticationMethod("otp")]` stay two independent gates, the latter's own freshness enforced entirely by `TotpStepUpOptions.ChallengeFreshnessWindow` at claim-stamping time. A small cross-package harmonization is bundled in: a new `SecurityClaimTypes.AuthenticationMethod` constant (`"amr"`) in `.Abstractions`, becoming the single source of truth `.Oidc`'s pre-existing `ClaimMappingOptions.AmrClaimType` default and `.Totp`'s own `TotpStepUpOptions.AmrClaimType` default both reference — additive, non-breaking, zero behavior change. Enrollment/recovery-code persistence (encrypted secret via `ISymmetricEncryptionService`, hashed codes via `IOneWayHasher`) is scoped as a thin shipped helper plus a documented recipe, never persisted by this package itself, consistent with `12.Security`'s zero-infrastructure-coupling design. Packages table (new `SharedKernel.Security.Totp` row), Technology Stack (4 new rows), Interface Contracts (new `SecurityClaimTypes.AuthenticationMethod` entry, full new `SharedKernel.Security.Totp` public-surface section), Logging Conventions (new EventIds `12400`/`12401`, `12400`–`12499` reserved), Implementation Rules (7 new rules), Domain Invariants (new subsection), DI Registration (new usage snippet), AOT Compatibility (2 new notes), Test Rules (5 new coverage-requirement bullets + new Totp test-project line, including a documented test-project-only exception letting `SharedKernel.Security.Totp.Tests` reference `.Oidc` for one interop proof), and Documentation Plan (new WO-069 subsection for DOC-20–DOC-24) all updated. `SK.12.Core` for this new package is explicitly flagged as blocked on `01.Core` shipping P-451's own Core phase first — `SK.12.Design`/`SK.12.Scaffold` may proceed immediately (security-arch-planner)
- [2026-09-04] WO-069/P-452 (SharedKernel.Security.Totp) implemented and shipped — all design-locked/queued markers throughout the file flipped to SHIPPED, matching real, tested, compiled code with zero signature drift from the pre-written spec. Two genuine findings recorded inline: (1) `IClaimsTransformation` IS reachable via a bare `Microsoft.AspNetCore.App` `FrameworkReference` alone, confirmed against the installed shared-framework directory — unlike the `.Mtls`/`Certificate` correction (WO-058), the "framework-provided" assumption held this time; the lesson generalizes either way, always verify, never assume. (2) `TotpStepUpClaimsTransformation` achieves "never mutates the original `ClaimsIdentity`" via the BCL's `new ClaimsIdentity(identity, claims)` copy-plus-add-claims constructor (deep-clones every claim, copies AuthenticationType/Label/BootstrapContext/Actor) rather than `principal.AddIdentity(...)`, which would mutate the same principal in place — a new, reusable technique for any future claims-augmenting transformation in this domain. Domain Invariants gained a new callout: WO-069 is the first phase in this domain's WO-057→WO-069 lineage to add zero `IUserContext`/`ITenantProvider` interface members, therefore triggering zero cross-domain implementer fallout — a contrast worth remembering against the "always grep the whole repo" rule established by every prior interface-breaking WO. `12.Security/state-map.md` updated in the same session (all Totp tasks marked `●` except SC-34, solution registration, deliberately left for the orchestrator per this session's shared-file protocol) — root `state-map.md`/root `CLAUDE.md` were NOT touched, also per that protocol; the orchestrator must run the standard root promotion once SC-34 lands. 263/263 tests green across all five `12.Security` packages (security-phase-implementer, sync-brain)
