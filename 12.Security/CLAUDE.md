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

All four target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — only `SharedKernel.Primitives` project reference. `SharedKernel.Security.ApiKey` and `SharedKernel.Security.Mtls` are sibling provider packages to `SharedKernel.Security.Oidc` and to **each other** — a machine-client credential scheme and a certificate-based scheme are not OIDC concepts, so neither lives inside `.Oidc`, and sibling provider packages never reference one another (each references only `.Abstractions` + whatever `01.Core` primitives it needs).

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
    NOTE: As of WO-057 (P-366), Email/Role are LEGACY-SHAPE REFERENCE CONSTANTS ONLY — the active lookup key
          OidcUserContext uses at runtime is SecurityOptions.ClaimMapping (SharedKernel.Security.Oidc), whose
          short-name defaults ("email"/"name"/"roles"/"scope") match the unmapped claim shape every standards-
          conformant OIDC issuer emits by default against a .NET 8+ JwtBearerHandler (MapInboundClaims = false).
          UserId/TenantId are unaffected — "sub" and "tenant_id" are already short names.
          Never use raw string literals for any of these — always the named constant or the configured option.
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
    — parses the request's DPoP header as a "typ: dpop+jwt" proof JWT; verifies its embedded jwk signature
    — validates htm (HTTP method) and htu (HTTP URI, without query/fragment) match the current request
    — validates iat falls within a configurable freshness window (default: 60 seconds)
    — computes jkt = base64url(SHA-256(canonical JWK)) and compares against the access token's cnf.jkt
      confirmation claim — a mismatch, malformed proof, or absent header all reject the request
    — on success, calls IDpopProofReplayCache.TryConsumeAsync(jti, ...) — false rejects as replay
    — on any failure, context.Fail(...) — the request never reaches application code; IUserContext
      resolves IsSenderConstrained = false only because authentication itself failed, not as a partial state
    NOTE: Chosen over mTLS sender-constraining (SharedKernel.Security.Mtls, P-377) as this domain's
          PRIMARY recommended sender-constraining mechanism — no PKI/certificate-provisioning burden on
          each consuming team, matching the platform's "developer friendly" requirement for this domain.
          Both remain available; a consumer may require either or both depending on regulatory regime.
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
```

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

## Logging Conventions *(WO-057, P-371 — this domain's first structured-logging retrofit, Core-implemented)*

`12.Security` reserves EventId range **`12000`–`12999`** in `01.Core`'s platform-wide registry, subdivided per package in declaration order. Shipped events, all `internal static partial class SecurityLogEvents`:

| EventId | Package | Method | Level | Fires when |
|---------|---------|--------|-------|-------------|
| `12100` | `SharedKernel.Security.Oidc` | `ServicePrincipalRecognized(ILogger, bool subjectClaimPresent)` | Debug | `OidcUserContext` resolves `IdentityKind.ServicePrincipal` — an authenticated principal with no (or an unparseable) human subject claim; expected/routine, not an error |
| `12101` | `SharedKernel.Security.Oidc` | `TenantClaimResolutionFailed(ILogger, string tenantClaimType)` | Warning | `OidcTenantProvider` falls back to `Guid.Empty` on an *authenticated* principal (never logged for a genuinely unauthenticated request) |
| `12102` | `SharedKernel.Security.Oidc` | `DpopProofRejected(ILogger, string failureReason)` *(WO-058, P-376)* | Warning | `DpopProofValidator` rejects a DPoP proof — `failureReason` structured (`"MissingProof"`, `"JktMismatch"`, `"Expired"`, `"Replayed"`), never the raw proof/token |
| `12103` | `SharedKernel.Security.Oidc` | `TokenRevocationRejected(ILogger, bool checkAvailable)` *(WO-058, P-379)* | Warning | `ITokenRevocationCheck` reports a token revoked, OR the check itself was unavailable/threw and failed closed — `checkAvailable` distinguishes the two without leaking which |
| `12200` | `SharedKernel.Security.ApiKey` | `ApiKeyValidationFailed(ILogger, string failureReason)` | Warning | `ApiKeyAuthenticationHandler` rejects a key — `failureReason` is a structured string (`"Rejected"`, `"AmbiguousCredential"`), never the raw presented key |
| `12300` | `SharedKernel.Security.Mtls` | `MtlsCertificateRejected(ILogger, string failureReason)` *(WO-058, P-377)* | Warning | `IMtlsCertificateValidator` rejects a certificate, or the RFC 8705 `cnf.x5t#S256` binding check mismatches — `failureReason` structured, never the raw certificate/thumbprint |

`12000`–`12099` (`SharedKernel.Security.Abstractions`) remains reserved but unused — this package stays logging-free (zero-NuGet-dependency rule forbids referencing `Microsoft.Extensions.Logging.Abstractions`). `12300`–`12399` is now reserved for `SharedKernel.Security.Mtls` (WO-058) — the fourth per-package sub-block within the domain's `12000`–`12999` range. No log statement in any package ever includes a raw claim value, token content, API key, or certificate/thumbprint value — only structured, safe fields.

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

// Opt-in DPoP sender-constraining and/or opt-in revocation checking (WO-058, P-376/P-379)
// — chained off the builder AddSharedKernelSecurity/AddAzureB2CAuthentication
// return; a consumer that ignores the return value gets today's unconstrained-bearer behavior
// unchanged:
services
    .AddSharedKernelSecurity(configuration)
    .RequireDpop<MyDistributedDpopReplayCache>()
    .WithRevocationCheck<MyIntrospectionBackedRevocationCheck>();

// Step-up authorization in application code — no new DI registration required, just the
// richer IUserContext surface (WO-058, P-375):
// if (!userContext.WasAuthenticatedWith("mfa")
//     || !userContext.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), clock.UtcNow))
// {
//     return Result.Failure(Error.Unauthorized("StepUpRequired", "..."));
// }
```

`SharedKernel.Security.Abstractions` ships **no DI extensions** — it is a pure abstraction library (this includes `SystemUserContext`, which a consuming composition root wires up itself). All HTTP-pipeline registration (including the opt-in DPoP/revocation-check builder chain) lives in `SharedKernel.Security.Oidc`; machine-client registration lives in `SharedKernel.Security.ApiKey` (pre-shared key) and `SharedKernel.Security.Mtls` (client certificate).

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
- **DI registration tests** use `IServiceCollection` / `ServiceCollection` directly with `BuildServiceProvider()` — no `WebApplicationFactory` or test host required for unit-level DI verification.
- **`AddAzureB2CAuthentication` DI tests must additionally register `IConfiguration` directly in the `ServiceCollection`** (`services.AddSingleton<IConfiguration>(config)`) before calling `AddAzureB2CAuthentication(config)` — confirmed at WO-057/T-09 implementation time: `Microsoft.Identity.Web`'s `SetIdentityModelLogger` resolves `IConfiguration` from the container itself, not merely from the `configuration` parameter passed into the extension method. Every real ASP.NET Core host registers `IConfiguration` automatically via `WebApplicationBuilder`; a bare `ServiceCollection`-based unit test does not and must do so explicitly or `BuildServiceProvider()`/first options resolution throws `InvalidOperationException`.
- **Testing `ConstantTimeKeyComparer` directly** (an `internal` type in `SharedKernel.Security.ApiKey`) requires `SharedKernel.Security.ApiKey.csproj` to declare `[assembly: InternalsVisibleTo("SharedKernel.Security.ApiKey.Tests")]` (via an `AssemblyAttribute` `ItemGroup`, mirroring the pattern already used across `02.Caching`/`04.Contracts`/`06.Persistence`/`07.Messaging`/`11.Communication`) — added WO-057/T-16 so the timing-safe compare path can be proven directly (via a hand-rolled `IHmacSigner` test double showing the outcome is dictated entirely by the injected `Verify` result, never an independent string-comparison shortcut) rather than only indirectly through `ApiKeyAuthenticationHandler`.
- **`SharedKernel.Security.Oidc.Tests` references `16.Testing/SharedKernel.Testing`** (WO-057/T-17) — the first `12.Security` test project to do so — for `InMemoryLogger<TCategoryName>`/`LoggerAssertions` to prove `OidcUserContext`/`OidcTenantProvider`'s structured security-audit `[LoggerMessage]` events (`12100`/`12101`) fire at the correct level with no raw claim value in the rendered message. Adding this reference required bumping `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Hosting` from `9.0.5` to `10.0.9` to avoid an `NU1605` downgrade (mirrors the identical pin already carried by `SharedKernel.Security.ApiKey.Tests`, which references `16.Testing` for the same reason).
- Step-up authentication tests (WO-058, P-375): a multi-value `amr` claim (discrete claims AND a single space-delimited value) parses correctly into `AuthenticationMethods`; `WasAuthenticatedWith` matches case-insensitively; an absent/malformed `auth_time` claim yields `AuthTime = null` without throwing; `IsAuthenticationFresherThan` is tested by passing an explicit `now` value in the test (never relying on real wall-clock time) to prove both the fresh-accept and stale-reject branches deterministically.
- DPoP tests (WO-058, P-376): a correctly-bound proof (valid `jwk` signature, matching `htm`/`htu`, fresh `iat`, unseen `jti` via a test double `IDpopProofReplayCache`) is accepted and resolves `IsSenderConstrained = true`; each failure mode (missing/malformed proof, `jkt` mismatch, expired `iat`, replayed `jti`) is tested independently; a non-opted-in host's existing bearer-only tests must be proven unaffected — run the pre-existing Oidc test suite unchanged against a host that never calls `RequireDpop<TReplayCache>()`.
- Mtls tests (WO-058, P-377): a validator-accepted certificate resolves `MtlsUserContext` correctly (`IdentityKind.ServicePrincipal`, `IsAuthenticated = true`, `UserId = Guid.Empty`); a rejected/absent certificate never resolves an authenticated context; a `cnf.x5t#S256` mismatch is rejected even when the underlying bearer token is otherwise valid — construct the mismatch scenario with two distinct test certificates, never a hand-computed "wrong" thumbprint string.
- Revocation tests (WO-058, P-379): a non-revoked token with the seam enabled still succeeds; a revoked token is rejected with the exact same failure shape/status code as an expired token (assert on the response, not an internal flag); a non-opted-in host is proven to never invoke `ITokenRevocationCheck` (e.g. via a test double that throws if called, asserting it is never hit); a throwing/timing-out revocation check is proven to fail closed (reject), not fail open.

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
