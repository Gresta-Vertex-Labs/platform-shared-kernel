# 12.Security — Identity & Access Brain

## What This Domain Is

The security abstractions and OIDC/B2C wiring layer. Downstream microservices depend on `SharedKernel.Security.Abstractions` to access the current user identity and tenant context — never on the concrete OIDC implementation. The concrete `SharedKernel.Security.Oidc` package wires JWT validation, Azure B2C / Microsoft Entra External ID authentication, and maps claims to the abstraction contracts.

Philosophy: **Thin abstractions. Claims-first. No domain coupling. Request-scoped identity.**

> `12.Security` may only reference `01.Core`. It must never reference `03.Domain`, `06.Persistence`, or any other capability domain.

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Security.Abstractions` | `IUserContext`, `ITenantProvider` interfaces — the only types application and domain-adjacent code should ever inject | `SharedKernel.Primitives` |
| `SharedKernel.Security.Oidc` | Concrete JWT/OIDC implementation: Azure B2C / Entra External ID wiring, claims-to-`IUserContext` mapping, `ITenantProvider` claim resolution, DI extensions | `SharedKernel.Security.Abstractions`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Identity.Web` |

Both target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — only `SharedKernel.Primitives` project reference.

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Identity abstractions | Pure C# 13 — zero external NuGet dependencies |
| JWT Bearer validation | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Azure B2C / Entra External ID | `Microsoft.Identity.Web` |
| Claims parsing | `System.Security.Claims` (BCL — no NuGet) |
| DI wiring | `Microsoft.Extensions.DependencyInjection.Abstractions` (transitive via ASP.NET Core) |

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
    .Claims                                                     → IReadOnlyDictionary<string, string>
    .IsAuthenticated                                            → bool
    .HasRole(string role)                                       → bool
        Returns true when Roles contains the given role (case-insensitive).
    NOTE: IUserContext is request-scoped. UserId must never be Guid.Empty when IsAuthenticated == true.
          Inject IUserContext only in application-layer code — never in domain or infrastructure repositories.

AnonymousUserContext  (sealed class, implements IUserContext)
    — sentinel for unauthenticated requests; all string properties null; Roles/Claims empty collections
    — UserId = Guid.Empty; IsAuthenticated = false; HasRole always returns false
    NOTE: Registered as fallback so IUserContext is always resolvable regardless of auth state.
          Infrastructure and application code must check IsAuthenticated before using UserId.
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
    SecurityClaimTypes.UserId                                   → string  ("sub" by default; configurable)
    SecurityClaimTypes.TenantId                                 → string  ("tenant_id")
    SecurityClaimTypes.Email                                    → string  (maps to ClaimTypes.Email)
    SecurityClaimTypes.Role                                     → string  (maps to ClaimTypes.Role)
    NOTE: These constants are the canonical claim type names used by both the Oidc mapper and
          consumer code that inspects IUserContext.Claims directly. Never use raw string literals.
```

---

### `SharedKernel.Security.Oidc` — public surface

#### Claims-to-context mapper (`Mapping/`)

```text
OidcUserContext  (sealed class, implements IUserContext)
    — constructed from ClaimsPrincipal at request time
    — UserId resolved from SecurityClaimTypes.UserId claim; parsed to Guid
    — Roles resolved from all SecurityClaimTypes.Role claims
    — Claims resolved as IReadOnlyDictionary<string, string> (first value per claim type)
    — IsAuthenticated delegates to ClaimsPrincipal.Identity?.IsAuthenticated

OidcTenantProvider  (sealed class, implements ITenantProvider)
    — resolves TenantId from SecurityClaimTypes.TenantId claim on ClaimsPrincipal
    — returns Guid.Empty when claim is absent or cannot be parsed
```

#### DI registration (`Extensions/`)

```text
AddSharedKernelSecurity(IConfiguration config)  →  IServiceCollection
    Registers:
      — JWT Bearer middleware with options bound from config section "Security:Jwt"
      — IUserContext as Scoped → OidcUserContext (resolved from IHttpContextAccessor)
      — ITenantProvider as Scoped → OidcTenantProvider (resolved from IHttpContextAccessor)
      — AnonymousUserContext registered as fallback when no HTTP context is present
    NOTE: Call after AddAuthentication() in the host startup pipeline.

AddAzureB2CAuthentication(IConfiguration config)  →  IServiceCollection
    Wraps AddSharedKernelSecurity with Azure B2C / Entra External ID specific Authority/Audience
    bound from config section "AzureAdB2C". Uses Microsoft.Identity.Web under the hood.
    NOTE: Use this instead of AddSharedKernelSecurity when the identity provider is Azure B2C.

SecurityOptions  (sealed class — Options-pattern, bound via AddValidatedOptions)
    .Jwt.Authority                                              → string  (required)
    .Jwt.Audience                                               → string  (required)
    .Jwt.ValidateLifetime                                       → bool    (default: true)
    .Jwt.ClockSkewSeconds                                       → int     (default: 30)
```

---

## Implementation Rules

- `SharedKernel.Security.Abstractions` has **zero NuGet dependencies** — references only `SharedKernel.Primitives`.
- `IUserContext` and `ITenantProvider` are **scoped** — one instance per HTTP request. Never register as singleton.
- `AnonymousUserContext` is the registered fallback — `IUserContext` is always resolvable; callers must check `IsAuthenticated` before consuming `UserId`.
- `IUserContext.UserId` must **never return `Guid.Empty`** when `IsAuthenticated == true` — if the `sub` claim is absent or unparseable, the implementation must set `IsAuthenticated = false`.
- `ITenantProvider.TenantId` returns `Guid.Empty` when no tenant claim is present — callers must handle this case (unauthenticated or system-level requests).
- Application and domain-adjacent code must **inject `IUserContext`** — never inject `IHttpContextAccessor`, `ClaimsPrincipal`, or `HttpContext` directly. Those are infrastructure details.
- Domain code (`03.Domain`) must **never reference `ITenantProvider`** — the application layer resolves `TenantId` and passes it as a `Guid` primitive to aggregate constructors.
- Infrastructure (`06.Persistence`) may inject `ITenantProvider` **only** for global tenant filter application in `TenantedDbContext`. No other infrastructure component should depend on `ITenantProvider`.
- `SecurityClaimTypes` constants are `const string` fields in a static class — not enums. Consuming services may define additional local constants.
- `OidcUserContext` and `OidcTenantProvider` are constructed lazily per-request — no caching across requests.
- `SecurityOptions` must use `AddValidatedOptions` from `SharedKernel.Configuration` — misconfigured apps must fail at startup, not at first authentication attempt.
- JWT Bearer configuration must set `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true` by default. Any relaxation must be explicit and documented.
- `HasRole(string role)` comparison is **case-insensitive** — role names may arrive from different identity providers with varying casing.
- `IUserContext.Claims` is a **read-only dictionary keyed by claim type** (first value wins for multi-value claims) — for roles, always use `IUserContext.Roles`.
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
```

`SharedKernel.Security.Abstractions` ships **no DI extensions** — it is a pure abstraction library. All registration lives in `SharedKernel.Security.Oidc`.

---

## AOT Compatibility

- `IUserContext`, `ITenantProvider`, `AnonymousUserContext` are sealed/interface types — AOT-safe.
- `SecurityClaimTypes` is a static class of string constants — AOT-safe.
- `OidcUserContext` and `OidcTenantProvider` read from `ClaimsPrincipal` — `ClaimsPrincipal.Claims` iteration is AOT-safe (no reflection on user types).
- `SecurityOptions` uses `AddValidatedOptions` from `SharedKernel.Configuration` — `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+; verify on each upgrade.
- `Microsoft.AspNetCore.Authentication.JwtBearer` — AOT support is partial; JWT token parsing uses internal reflection in some code paths. Encapsulating it behind `IUserContext` limits the AOT blast radius to the registration path only.
- `Microsoft.Identity.Web` — not fully AOT-safe; document this explicitly and keep it isolated to `AddAzureB2CAuthentication`. A swap to a lighter JWT-only path is possible without changing abstractions.
- `Guid.Parse` on claim values — AOT-safe (BCL).
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in hot paths.

---

## Test Rules

- Unit tests for `SharedKernel.Security.Abstractions` live in `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.Tests/`.
- Unit tests for `SharedKernel.Security.Oidc` live in `12.Security/SharedKernel.Security.Oidc/SharedKernel.Security.Oidc.Tests/`.
- `AnonymousUserContext`: all properties return correct sentinel values; `HasRole` always returns `false`; `IsAuthenticated == false`; `UserId == Guid.Empty`.
- `OidcUserContext`: valid `ClaimsPrincipal` with all claims maps correctly; missing `sub` claim → `IsAuthenticated = false`; unparseable `sub` → `IsAuthenticated = false`; missing role claims → empty `Roles`; `HasRole` is case-insensitive.
- `OidcTenantProvider`: valid tenant claim parses to correct `Guid`; absent claim → `Guid.Empty`; malformed claim → `Guid.Empty`.
- `SecurityOptions` validation: valid config registers without throw; missing `Authority` throws at `IHost.StartAsync()`; missing `Audience` throws at startup.
- DI registration tests: `AddSharedKernelSecurity` registers `IUserContext` as scoped; registers `ITenantProvider` as scoped; `IUserContext` is resolvable without an active HTTP context (returns `AnonymousUserContext` fallback).
- Role-based tests: `HasRole` with exact case match returns `true`; `HasRole` with differing case returns `true`; unknown role returns `false`.
- **Test construction pattern — authenticated principal:** `new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))` — passing an `authenticationType` string makes `IsAuthenticated = true`.
- **Test construction pattern — unauthenticated principal:** `new ClaimsPrincipal(new ClaimsIdentity(claims))` — omitting `authenticationType` makes `IsAuthenticated = false`.
- **DI registration tests** use `IServiceCollection` / `ServiceCollection` directly with `BuildServiceProvider()` — no `WebApplicationFactory` or test host required for unit-level DI verification.

---

## Changelog

> Maintained by the security domain agent. One line per significant change.

- [2026-06-02] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules
- [2026-06-02] SK.12.Core complete — test construction patterns added to Test Rules (authenticated vs unauthenticated ClaimsPrincipal; DI via ServiceCollection) (security-phase-implementer)
- [2026-06-02] SK.12.Published complete — no brain changes warranted; Published phase was verification-only (build, pack, test all green) (sync-brain)
