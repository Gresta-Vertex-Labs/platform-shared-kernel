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
| `SharedKernel.Security.ApiKey` *(WO-057, in progress)* | Pre-shared-key / machine-client authentication scheme — `IApiKeyValidator` extensibility point, constant-time key comparison, composes alongside JWT Bearer via a policy/forwarding scheme; never OIDC | `SharedKernel.Security.Abstractions`, `SharedKernel.Cryptography` (`01.Core`) |

All three target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — only `SharedKernel.Primitives` project reference. `SharedKernel.Security.ApiKey` is a sibling provider package to `SharedKernel.Security.Oidc` — a machine-client credential scheme is not an OIDC concept, so it never lives inside `.Oidc` and never references it.

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
    NOTE: Registered as fallback so IUserContext is always resolvable regardless of auth state.
          Infrastructure and application code must check IsAuthenticated before using UserId.

SystemUserContext  (sealed class, singleton instance, implements IUserContext)                              [WO-057, P-369]
    — sentinel for trusted, non-HTTP execution authority: a Temporal activity, a MassTransit consumer,
      a Hangfire job, a startup seeder — as opposed to a rejected or genuinely unauthenticated caller
    — IdentityKind = IdentityKind.System; IsAuthenticated = true; UserId = Guid.Empty
    — Roles/Permissions/Claims empty; HasRole/HasPermission always return false
      (a system context asserts trust by IdentityKind, not by inheriting role/permission membership it was
      never granted — a consuming service's own authorization bridge decides what IdentityKind.System means)
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

SecurityOptions  (sealed class — Options-pattern, bound via AddValidatedOptions)
    .Jwt.Authority                                              → string  (required)
    .Jwt.Audience                                               → string  (required)
    .Jwt.ValidateLifetime                                       → bool    (default: true)
    .Jwt.ClockSkewSeconds                                       → int     (default: 30)
    .ClaimMapping.EmailClaimType                                → string  (default: "email")            [WO-057, P-366]
    .ClaimMapping.NameClaimType                                 → string  (default: "name")              [WO-057, P-366]
    .ClaimMapping.RoleClaimType                                 → string  (default: "roles")             [WO-057, P-366]
    .ClaimMapping.PermissionClaimType                           → string  (default: "scope")             [WO-057, P-368]
    NOTE: Defaults match modern short-name OIDC claim conventions — the unmapped shape every standards-
          conformant issuer (Entra ID v2.0, Auth0, Okta, Keycloak) emits by default against a .NET 8+
          JwtBearerHandler (MapInboundClaims = false). NOT the legacy ClaimTypes.* long-form URIs the
          pre-WO-057 defaults hardcoded. A consuming service on an IdP still emitting the legacy shape
          (or one that has opted into MapInboundClaims = true) overrides these to the ClaimTypes.*
          equivalents. Both AddSharedKernelSecurity and AddAzureB2CAuthentication source
          TokenValidationParameters.NameClaimType/RoleClaimType from these SAME values — never divergent
          from what OidcUserContext reads.
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

## Logging Conventions *(WO-057, P-371 — this domain's first structured-logging retrofit, Core-implemented)*

`12.Security` reserves EventId range **`12000`–`12999`** in `01.Core`'s platform-wide registry, subdivided per package in declaration order. Shipped events, all `internal static partial class SecurityLogEvents`:

| EventId | Package | Method | Level | Fires when |
|---------|---------|--------|-------|-------------|
| `12100` | `SharedKernel.Security.Oidc` | `ServicePrincipalRecognized(ILogger, bool subjectClaimPresent)` | Debug | `OidcUserContext` resolves `IdentityKind.ServicePrincipal` — an authenticated principal with no (or an unparseable) human subject claim; expected/routine, not an error |
| `12101` | `SharedKernel.Security.Oidc` | `TenantClaimResolutionFailed(ILogger, string tenantClaimType)` | Warning | `OidcTenantProvider` falls back to `Guid.Empty` on an *authenticated* principal (never logged for a genuinely unauthenticated request) |
| `12200` | `SharedKernel.Security.ApiKey` | `ApiKeyValidationFailed(ILogger, string failureReason)` | Warning | `ApiKeyAuthenticationHandler` rejects a key — `failureReason` is a structured string (`"Rejected"`, `"AmbiguousCredential"`), never the raw presented key |

`12000`–`12099` (`SharedKernel.Security.Abstractions`) remains reserved but unused — this package stays logging-free (zero-NuGet-dependency rule forbids referencing `Microsoft.Extensions.Logging.Abstractions`). No log statement in either package ever includes a raw claim value, token content, or API key — only structured, safe fields.

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

// Background-execution hosts (17.Workflows activities, 07.Messaging consumers, hosted services)
// register SystemUserContext explicitly in place of the HTTP-derived IUserContext factory:
services.AddScoped<IUserContext>(_ => SystemUserContext.Instance);
```

`SharedKernel.Security.Abstractions` ships **no DI extensions** — it is a pure abstraction library (this includes `SystemUserContext`, which a consuming composition root wires up itself). All HTTP-pipeline registration lives in `SharedKernel.Security.Oidc`; machine-client registration lives in `SharedKernel.Security.ApiKey`.

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

---

## Test Rules

- Unit tests for `SharedKernel.Security.Abstractions` live in `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.Tests/`.
- Unit tests for `SharedKernel.Security.Oidc` live in `12.Security/SharedKernel.Security.Oidc/SharedKernel.Security.Oidc.Tests/`.
- Unit tests for `SharedKernel.Security.ApiKey` live in `12.Security/SharedKernel.Security.ApiKey/SharedKernel.Security.ApiKey.Tests/` *(WO-057, in progress)*.
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

---

## Documentation Plan *(WO-057, P-372 — design for the queued Docs-phase content, DOC-04–DOC-07)*

This section defines the **structure** the Docs phase must fill in — it is a Design-phase deliverable (D-22), not the documentation itself.

- **Domain-root `README.md`** (currently empty — `12.Security/README.md`): `Overview` (thin abstractions / claims-first / no domain coupling philosophy) → `Packages` table (mirrors the one in this brain: `.Abstractions`, `.Oidc`, `.ApiKey`) → `Quick Start` with three minimal snippets (`AddSharedKernelSecurity`, `AddAzureB2CAuthentication`, `AddApiKeyAuthentication<TValidator>`) → links to each package's own `README.md`. Matches the orientation-doc pattern every other domain's root `README.md` already follows.
- **`SharedKernel.Security.Abstractions/README.md` refresh**: usage example per public type — `IUserContext`/`HasRole`/`HasPermission`, `ITenantProvider`, `IdentityKind` (all four values, the corrected `UserId`/`IsAuthenticated` invariant), `AnonymousUserContext`, `SystemUserContext` (background-host registration one-liner), `SecurityClaimTypes` (with the legacy-vs-active-lookup-key callout from P-366).
- **`SharedKernel.Security.Oidc/README.md` refresh**: usage example per public type — `OidcUserContext`/`OidcTenantProvider`, `SecurityOptions` including the new `ClaimMapping` sub-options (short-name defaults vs. legacy `ClaimTypes.*` override), `AddSharedKernelSecurity`/`AddAzureB2CAuthentication`.
- **`SharedKernel.Security.ApiKey/README.md`** (new): states up front, in its own section, that this package is for pre-shared-key/machine-client scenarios only and explicitly does **not** attempt key issuance, rotation, or storage — mirrors the equivalent disclaimer already required in Implementation Rules.
- **Four end-to-end recipes** (own subsection, likely inside `SharedKernel.Security.Oidc/README.md` per DOC-07, since it is the package every recipe bridges *from*):
  1. `05.Application` `IAuthorizationContext` bridge combining `HasRole` + `HasPermission`.
  2. `06.Persistence` `TenantedDbContext` wiring via `ITenantProvider`.
  3. `07.Messaging` `ITenantContextAccessor` bridge (mirrors the `IAuthorizationContext`/`IUnitOfWork` bridge pattern already used elsewhere on the platform).
  4. A background-execution host (Temporal activity / MassTransit consumer / hosted service) registering `SystemUserContext` in place of the HTTP-derived `IUserContext` factory.

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
