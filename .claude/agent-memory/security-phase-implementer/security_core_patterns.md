---
name: security-core-patterns
description: Key implementation decisions from SK.12.Core — claims mapping, DI registration, AOT constraints, test patterns
metadata:
  type: project
---

## OidcUserContext — IsAuthenticated invariant

`IsAuthenticated` is forced to `false` (and `UserId` to `Guid.Empty`) when:
- `ClaimsPrincipal.Identity?.IsAuthenticated` is `false`
- The `sub` claim is absent
- The `sub` claim value cannot be parsed as a non-empty `Guid`

**Why:** The interface contract guarantees `UserId` is never `Guid.Empty` when `IsAuthenticated == true`. Failing silently would produce invalid user identity downstream.

**How to apply:** Any future change to `OidcUserContext` must preserve this invariant. Tests must cover all three failure paths.

## AnonymousUserContext fallback pattern

Scoped DI factory for `IUserContext`:
```csharp
services.AddScoped<IUserContext>(sp => {
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    var user = accessor.HttpContext?.User;
    return user is not null ? new OidcUserContext(user) : AnonymousUserContext.Instance;
});
```
`AnonymousUserContext.Instance` is a static readonly singleton — avoids allocation when no HTTP context is present (background workers, console hosts, unit-test DI containers).

## OidcTenantProvider — never throws

Returns `Guid.Empty` for absent or malformed `tenant_id` claim. Never throws. Callers must handle `Guid.Empty` (unauthenticated or system-level requests).

## SecurityOptions binding

- Section key: `"Security"` (constant `SecurityOptions.SectionKey`)
- Nested: `JwtOptions` with `Authority` (required), `Audience` (required), `ValidateLifetime` (default `true`), `ClockSkewSeconds` (default `30`)
- Uses `AddValidatedOptions<SecurityOptions>` from `SharedKernel.Configuration` — startup fails at `IHost.StartAsync()` when required fields are missing
- JWT Bearer post-configured via `IOptions<SecurityOptions>` — no `BuildServiceProvider()` anti-pattern

## JWT validation defaults

`ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true` by default.
Any relaxation must be explicit and documented at the call site.

## AOT constraints

- `Microsoft.Identity.Web` isolated entirely to `AddAzureB2CAuthentication` — not AOT-safe; swap to standard Entra ID path avoids AOT blast radius
- `Microsoft.AspNetCore.Authentication.JwtBearer` has partial AOT support (internal reflection in token parsing) — encapsulated behind `IUserContext` so blast radius is limited to DI registration only
- `OidcUserContext`/`OidcTenantProvider` claims iteration is AOT-safe (no reflection on user types)

## Test construction patterns

**Authenticated principal:**
```csharp
new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
// authenticationType string → IsAuthenticated = true
```

**Unauthenticated principal:**
```csharp
new ClaimsPrincipal(new ClaimsIdentity(claims))
// no authenticationType → IsAuthenticated = false
```

**DI registration tests:** Use `ServiceCollection` + `BuildServiceProvider()` directly. No `WebApplicationFactory` or test host required for unit-level DI verification.

## SecurityClaimTypes constants

- `UserId` = `"sub"`
- `TenantId` = `"tenant_id"`
- `Email` = `ClaimTypes.Email`
- `Role` = `ClaimTypes.Role`

## Phase completion

SK.12.Design ● → SK.12.Scaffold ● → SK.12.Core ● → SK.12.Tests ● → SK.12.Docs ● → SK.12.Published ● (all as of 2026-06-02)
46 tests passing: 13 Abstractions + 33 Oidc
Domain fully complete. Both packages produce `.nupkg` + `.snupkg` via `dotnet pack --configuration Release`.
Output dir: `artifacts/nupkg/` (SharedKernel.Security.Abstractions.1.0.0 + SharedKernel.Security.Oidc.1.0.0)

## Pack notes

- NuGet metadata was present from the Scaffold phase — no .csproj edits needed in Published
- NU1903 warnings on `System.Security.Cryptography.Xml` 9.0.0 are transitive from `Microsoft.Identity.Web` — cannot be suppressed without removing the package; not a code defect
- `dotnet pack` outputs to `artifacts/nupkg/` (created on first pack run)
