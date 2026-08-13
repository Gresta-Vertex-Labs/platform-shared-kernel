# 12.Security — State Map

> **What this file is:** Phase and task tracker for all work within `12.Security`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.12.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.12.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.12.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.12.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.12.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.12.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.12.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IUserContext | SK.12.Core | SharedKernel.Security.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Azure B2C wiring | SK.12.Core | Waiting on tenant claim name decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Security.Abstractions` | Published | `●` | Zero NuGet dependencies; references only SharedKernel.Primitives |
| `SharedKernel.Security.Oidc` | Published | `●` | References Abstractions + Microsoft.Identity.Web |
| `SharedKernel.Security.ApiKey` | Published | `●` | New sibling provider package (WO-057, P-370); references Abstractions + `01.Core/SharedKernel.Cryptography` only; v1.0.0 |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.12.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Error`, `Result<T>`) | Available |
| `SK.12.Scaffold` | `01.Core` | `SharedKernel.Cryptography` ProjectReference (constant-time comparison primitive) for new `SharedKernel.Security.ApiKey` package (WO-057, P-370) | Available |

---

## Phase: Design <!-- phase-key: SK.12.Design -->

> Finalize all interface shapes, option contracts, and DI extension signatures before any implementation begins.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| D-01 | Define `IUserContext` interface (UserId, Email, Username, Roles, Claims, IsAuthenticated, HasRole) | `SharedKernel.Security.Abstractions` | `●` |
| D-02 | Define `ITenantProvider` interface (TenantId → Guid) | `SharedKernel.Security.Abstractions` | `●` |
| D-03 | Define `AnonymousUserContext` sealed class (sentinel, all-empty) | `SharedKernel.Security.Abstractions` | `●` |
| D-04 | Define `SecurityClaimTypes` static class (const string fields: UserId, TenantId, Email, Role) | `SharedKernel.Security.Abstractions` | `●` |
| D-05 | Define `OidcUserContext` sealed class shape (ClaimsPrincipal constructor, all IUserContext members) | `SharedKernel.Security.Oidc` | `●` |
| D-06 | Define `OidcTenantProvider` sealed class shape (ClaimsPrincipal constructor, TenantId) | `SharedKernel.Security.Oidc` | `●` |
| D-07 | Define `SecurityOptions` sealed class (Jwt nested: Authority, Audience, ValidateLifetime, ClockSkewSeconds) | `SharedKernel.Security.Oidc` | `●` |
| D-08 | Define `AddSharedKernelSecurity` DI extension signature (IConfiguration, registers IUserContext/ITenantProvider scoped, JWT Bearer) | `SharedKernel.Security.Oidc` | `●` |
| D-09 | Define `AddAzureB2CAuthentication` DI extension signature (IConfiguration, B2C wiring via Microsoft.Identity.Web) | `SharedKernel.Security.Oidc` | `●` |
| D-10 | Define `SecurityOptions.ClaimMapping` (`ClaimMappingOptions`: `EmailClaimType` default `"email"`, `NameClaimType` default `"name"`, `RoleClaimType` default `"roles"`, `PermissionClaimType` default `"scope"`) — defaults match modern short-name OIDC convention, configurable per-IdP to legacy `ClaimTypes.*`/`"role"`/other shapes (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| D-11 | Define `TokenValidationParameters.NameClaimType`/`RoleClaimType` wiring contract — both DI extensions must source these from the same `ClaimMappingOptions` values `OidcUserContext` reads (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| D-12 | Define defensive role-claim reader shape: accepts one `Claim` per role OR a single claim whose value is a JSON array of roles, never throws on either shape (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| D-13 | Define `IdentityKind` enum (`Anonymous` = 0, `User`, `ServicePrincipal`, `System`) — `Anonymous` as the default(IdentityKind) fail-safe value (WO-057, P-367) | `SharedKernel.Security.Abstractions` | `●` |
| D-14 | Define `IUserContext.IdentityKind` property addition (additive-but-breaking interface member) and the corrected invariant: `UserId` never `Guid.Empty` when `IdentityKind == User`; `IdentityKind.ServicePrincipal`/`.System` legitimately carry `IsAuthenticated = true` with `UserId == Guid.Empty` (WO-057, P-367) | `SharedKernel.Security.Abstractions` | `●` |
| D-15 | Define `OidcUserContext` service-principal detection logic — IdP-agnostic: a valid `ClaimsPrincipal.Identity.IsAuthenticated == true` with no parseable human subject claim resolves to `ServicePrincipal`, never hardcoding one vendor's claim names (e.g. Entra's `idtyp`/`azp`) as the sole mechanism (WO-057, P-367) | `SharedKernel.Security.Oidc` | `●` |
| D-16 | Define `IUserContext.Permissions` (`IReadOnlyCollection<string>`) and `HasPermission(string permission)` (case-insensitive) — mirrors `Roles`/`HasRole` shape exactly (WO-057, P-368) | `SharedKernel.Security.Abstractions` | `●` |
| D-17 | Define scope-claim parsing strategy: space-delimited `scope`/`scp` claim value split into individual permission entries, claim type name sourced from `ClaimMappingOptions.PermissionClaimType` (WO-057, P-368) | `SharedKernel.Security.Oidc` | `●` |
| D-18 | Define `SystemUserContext` sealed sentinel shape — singleton instance, `IdentityKind.System`, peer of `AnonymousUserContext` not a replacement (WO-057, P-369) | `SharedKernel.Security.Abstractions` | `●` |
| D-19 | Define new `SharedKernel.Security.ApiKey` package shape: `IApiKeyValidator`, `ApiKeyValidationResult`, `ApiKeyAuthenticationOptions`, `ApiKeyAuthenticationHandler`, `ApiKeyUserContext`, `AddApiKeyAuthentication` policy/forwarding-scheme composition alongside JWT Bearer (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| D-20 | Define constant-time key comparison strategy for `ApiKeyAuthenticationHandler`, delegating to `01.Core/SharedKernel.Cryptography`'s existing primitives — never a new bespoke comparison routine (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| D-21 | Define `SecurityLogEvents` `[LoggerMessage]` surface and `EventId` sub-range split within `12000`-`12999`: `12100`-`12199` for `SharedKernel.Security.Oidc`, `12200`-`12299` for `SharedKernel.Security.ApiKey` (`SharedKernel.Security.Abstractions` stays logging-free — zero-NuGet rule) (WO-057, P-371) | `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey` | `●` |
| D-22 | Define domain-root `README.md` and package-README refresh structure, plus the four end-to-end recipe topics (`05.Application` bridge, `06.Persistence` `TenantedDbContext`, `07.Messaging` `ITenantContextAccessor`, background-execution `SystemUserContext` registration) (WO-057, P-372) | All | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.12.Scaffold -->

> Wire up .csproj NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| SC-01 | Create `SharedKernel.Security.Abstractions.csproj` targeting `net10.0`; project-reference `SharedKernel.Primitives`; no NuGet dependencies | `SharedKernel.Security.Abstractions` | `●` |
| SC-02 | Create folder structure: `Abstractions/`, `Claims/` under Abstractions package | `SharedKernel.Security.Abstractions` | `●` |
| SC-03 | Create `SharedKernel.Security.Abstractions.Tests.csproj`; reference main project; add xUnit + Microsoft.NET.Test.Sdk | `SharedKernel.Security.Abstractions` | `●` |
| SC-04 | Create `SharedKernel.Security.Oidc.csproj` targeting `net10.0`; project-references Abstractions + SharedKernel.Configuration; NuGet: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Identity.Web` | `SharedKernel.Security.Oidc` | `●` |
| SC-05 | Create folder structure: `Mapping/`, `Extensions/`, `Options/` under Oidc package | `SharedKernel.Security.Oidc` | `●` |
| SC-06 | Create `SharedKernel.Security.Oidc.Tests.csproj`; reference main project; add xUnit + NSubstitute + DI + Hosting test packages | `SharedKernel.Security.Oidc` | `●` |
| SC-07 | Register all four projects in `/12.Security/` solution folder of `Platform.SharedKernel.slnx` | Solution | `●` |
| SC-08 | Create empty test stub files for Abstractions.Tests (`AnonymousUserContextTests.cs`, `SecurityClaimTypesTests.cs`) | `SharedKernel.Security.Abstractions` | `●` |
| SC-09 | Create empty test stub files for Oidc.Tests (`Mapping/OidcUserContextTests.cs`, `Mapping/OidcTenantProviderTests.cs`, `Options/SecurityOptionsTests.cs`, `Extensions/SecurityServiceCollectionExtensionsTests.cs`) | `SharedKernel.Security.Oidc` | `●` |
| SC-10 | Create `SharedKernel.Security.ApiKey.csproj` targeting `net10.0`; project-references `SharedKernel.Security.Abstractions` + `SharedKernel.Cryptography` only; no other third-party NuGet dependencies (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| SC-11 | Create folder structure: `Validation/`, `Extensions/`, `Options/`, `Logging/` under the ApiKey package (WO-057, P-370/P-371) | `SharedKernel.Security.ApiKey` | `●` |
| SC-12 | Create `SharedKernel.Security.ApiKey.Tests.csproj`; reference main project + `SharedKernel.Testing`; add xUnit + DI + Hosting test packages (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| SC-13 | Register `SharedKernel.Security.ApiKey` project (+ `.Tests`) in the `/12.Security/` solution folder of `Platform.SharedKernel.slnx` (WO-057, P-370) | Solution | `●` |
| SC-14 | Create empty test stub files for ApiKey.Tests (`Validation/ApiKeyAuthenticationHandlerTests.cs`, `Extensions/ApiKeyServiceCollectionExtensionsTests.cs`) (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| SC-15 | Add `Logging/SecurityLogEvents.cs` stub file (partial class, `[LoggerMessage]` skeleton, no bodies yet) in both `SharedKernel.Security.Oidc` and `SharedKernel.Security.ApiKey` (WO-057, P-371) | `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey` | `●` |

---

## Phase: Core <!-- phase-key: SK.12.Core -->

> Full implementation of all types, interfaces, extensions, and DI registrations.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| C-01 | Implement `IUserContext` interface (full XML docs, HasRole case-insensitive contract) | `SharedKernel.Security.Abstractions` | `●` |
| C-02 | Implement `ITenantProvider` interface (full XML docs, Guid.Empty contract) | `SharedKernel.Security.Abstractions` | `●` |
| C-03 | Implement `AnonymousUserContext` sealed class (sentinel, all-empty, Instance singleton, EmptyDictionary inner class) | `SharedKernel.Security.Abstractions` | `●` |
| C-04 | Implement `SecurityClaimTypes` static class (const string fields: UserId="sub", TenantId="tenant_id", Email=ClaimTypes.Email, Role=ClaimTypes.Role) | `SharedKernel.Security.Abstractions` | `●` |
| C-05 | Implement `OidcUserContext` sealed class (ClaimsPrincipal constructor, claims mapping, IsAuthenticated forced false on missing/invalid sub, HasRole case-insensitive) | `SharedKernel.Security.Oidc` | `●` |
| C-06 | Implement `OidcTenantProvider` sealed class (ClaimsPrincipal constructor, TenantId from tenant_id claim, Guid.Empty on absent/unparseable) | `SharedKernel.Security.Oidc` | `●` |
| C-07 | Implement `SecurityOptions` sealed class (JwtOptions nested: Authority required, Audience required, ValidateLifetime default true, ClockSkewSeconds default 30, SectionKey="Security") | `SharedKernel.Security.Oidc` | `●` |
| C-08 | Implement `AddSharedKernelSecurity` DI extension (IHttpContextAccessor, IUserContext scoped→OidcUserContext/AnonymousUserContext fallback, ITenantProvider scoped, JWT Bearer ValidateIssuer/Audience/Lifetime=true, post-configure from SecurityOptions) | `SharedKernel.Security.Oidc` | `●` |
| C-09 | Implement `AddAzureB2CAuthentication` DI extension (same IUserContext/ITenantProvider registration + Microsoft.Identity.Web B2C wiring, AOT note documented) | `SharedKernel.Security.Oidc` | `●` |
| C-10 | Implement `SecurityOptions.ClaimMapping` (`ClaimMappingOptions` — `EmailClaimType`/`NameClaimType`/`RoleClaimType`/`PermissionClaimType`, short-name defaults) with full XML docs explaining the legacy-`ClaimTypes.*` vs short-name distinction (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| C-11 | Wire `TokenValidationParameters.NameClaimType`/`RoleClaimType` in `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` from `SecurityOptions.ClaimMapping` — no divergence between ASP.NET Core's own claims machinery and what `OidcUserContext` reads (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| C-12 | Implement defensive role-claim reader in `OidcUserContext` — one `Claim` per role OR a single JSON-array-valued claim, never throws on either shape (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| C-13 | Update `OidcUserContext.Email`/`.Username`/role+permission readers to resolve via the injected `SecurityOptions.ClaimMapping` claim names (constructed via an updated DI factory accepting `IOptions<SecurityOptions>`) instead of hardcoded `SecurityClaimTypes.Email`/`ClaimTypes.Name`/`ClaimTypes.Role`; `SecurityClaimTypes` XML docs updated to state `Email`/`Role` are legacy-shape reference constants, not the active lookup key (WO-057, P-366) | `SharedKernel.Security.Abstractions`, `SharedKernel.Security.Oidc` | `●` |
| C-14 | Add `IdentityKind` enum (`Anonymous`, `User`, `ServicePrincipal`, `System`) to `SharedKernel.Security.Abstractions` (WO-057, P-367) | `SharedKernel.Security.Abstractions` | `●` |
| C-15 | Add `IUserContext.IdentityKind` property; `AnonymousUserContext.IdentityKind = IdentityKind.Anonymous` (WO-057, P-367) | `SharedKernel.Security.Abstractions` | `●` |
| C-16 | Implement `OidcUserContext.IdentityKind` resolution: `User` when a valid `Guid`-parseable human subject claim is present, `ServicePrincipal` when the token is otherwise valid (`ClaimsPrincipal.Identity.IsAuthenticated == true`) but carries no such human subject, `Anonymous`/`IsAuthenticated = false` only when the underlying principal itself is not authenticated (WO-057, P-367) | `SharedKernel.Security.Oidc` | `●` |
| C-17 | Add `IUserContext.Permissions`/`HasPermission(string)` members; `AnonymousUserContext.Permissions` empty, `HasPermission` always `false` (WO-057, P-368) | `SharedKernel.Security.Abstractions` | `●` |
| C-18 | Implement `OidcUserContext` scope-claim parsing (space-delimited, claim type from `ClaimMappingOptions.PermissionClaimType`) into `Permissions`; `HasPermission` case-insensitive, mirrors `HasRole` (WO-057, P-368) | `SharedKernel.Security.Oidc` | `●` |
| C-19 | Implement `SystemUserContext` sealed singleton class (`IdentityKind.System`, `IsAuthenticated = true`, `UserId = Guid.Empty`, empty `Roles`/`Permissions`/`Claims`, `HasRole`/`HasPermission` always `false`) — no DI wiring shipped (WO-057, P-369) | `SharedKernel.Security.Abstractions` | `●` |
| C-20 | Implement `IApiKeyValidator` (`ValidateAsync(string presentedKey, CancellationToken) → ApiKeyValidationResult`) and `ApiKeyValidationResult` (`IsValid`, `ClientId`, optional `Roles`/`Permissions` passthrough) — consumer supplies the storage-backed implementation; this package never dictates a storage mechanism (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| C-21 | Implement `ApiKeyAuthenticationOptions` (header/query key name, scheme name) + `ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>` producing an authenticated `ClaimsPrincipal`/`ApiKeyUserContext` (`IdentityKind.ServicePrincipal`, `IsAuthenticated = true`) on a successful `IApiKeyValidator` match (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| C-22 | Implement constant-time key comparison in `ApiKeyAuthenticationHandler`, delegating to `SharedKernel.Cryptography`'s existing primitives — never `string.Equals`/`==` (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| C-23 | Implement `AddApiKeyAuthentication` DI extension composing alongside `AddSharedKernelSecurity`'s JWT Bearer scheme via a policy/forwarding scheme selector — a service can accept either credential type on the same host without one scheme silently shadowing the other (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| C-24 | Implement `[LoggerMessage]` structured security-audit log events: `EventId` `12100`-`12199` in Oidc (missing/unparseable human subject, successful service-principal recognition, tenant-claim resolution failure) and `12200`-`12299` in ApiKey (key validation failure) — no raw claim values, token content, or API keys ever logged, only structured safe fields (WO-057, P-371) | `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey` | `●` |

---

## Phase: Tests <!-- phase-key: SK.12.Tests -->

> Unit test coverage for all packages.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| T-01 | AnonymousUserContext: sentinel values, HasRole always false, IsAuthenticated false, UserId Guid.Empty | `SharedKernel.Security.Abstractions` | `●` |
| T-02 | SecurityClaimTypes: const values match expected strings and BCL ClaimTypes | `SharedKernel.Security.Abstractions` | `●` |
| T-03 | OidcUserContext: valid principal maps all claims; missing/unparseable sub forces IsAuthenticated=false; case-insensitive HasRole; Roles empty when absent; first-value-wins Claims dict | `SharedKernel.Security.Oidc` | `●` |
| T-04 | OidcTenantProvider: valid claim parses; absent/malformed/empty → Guid.Empty; null principal throws | `SharedKernel.Security.Oidc` | `●` |
| T-05 | SecurityOptions validation: valid passes; missing Authority fails; missing Audience fails; default values correct | `SharedKernel.Security.Oidc` | `●` |
| T-06 | DI registration: IUserContext scoped; ITenantProvider scoped; no HttpContext → AnonymousUserContext; null guards throw ArgumentNullException | `SharedKernel.Security.Oidc` | `●` |
| T-07 | New realistic-default test: `ClaimsPrincipal` built from unmapped short-name claims (`"email"`, `"name"`, `"roles"` as both a JSON-array-valued claim and discrete per-role claims) with no `MapInboundClaims` override — proves `Email`/`Username`/`Roles`/`HasRole` all resolve correctly; must fail against pre-fix shipped code and pass after (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| T-08 | Regression test: the legacy `ClaimTypes.*`-mapped claim shape (a consumer that opted into `MapInboundClaims = true`, or an older token) continues to resolve correctly — widened default, not narrowed (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| T-09 | DI test: `TokenValidationParameters.NameClaimType`/`RoleClaimType` set correctly from `SecurityOptions.ClaimMapping` by both `AddSharedKernelSecurity` and `AddAzureB2CAuthentication` (WO-057, P-366) | `SharedKernel.Security.Oidc` | `●` |
| T-10 | Client-credentials-shaped token (no human subject claim, valid signature/issuer/audience) resolves `IsAuthenticated = true`, `IdentityKind.ServicePrincipal` (WO-057, P-367) | `SharedKernel.Security.Oidc` | `●` |
| T-11 | Genuinely invalid/unauthenticated principal resolves `IdentityKind.Anonymous`, `IsAuthenticated = false` — existing behavior for that case unchanged (WO-057, P-367) | `SharedKernel.Security.Oidc` | `●` |
| T-12 | Multi-scope claim value (`"orders:read orders:write"`) parses into two independent permission entries, both matched correctly by `HasPermission` (WO-057, P-368) | `SharedKernel.Security.Oidc` | `●` |
| T-13 | Absent scope claim yields an empty `Permissions` collection, not a throw (WO-057, P-368) | `SharedKernel.Security.Oidc` | `●` |
| T-14 | `SystemUserContext.IdentityKind == IdentityKind.System`, distinguishable from `AnonymousUserContext.IdentityKind == IdentityKind.Anonymous` by every `IdentityKind` pattern-match consumer (WO-057, P-369) | `SharedKernel.Security.Abstractions` | `●` |
| T-15 | Invalid or absent API key never resolves to an authenticated context; a valid key resolves correctly regardless of which scheme (JWT Bearer or API key) the request actually presented (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| T-16 | Constant-time comparison test proving the timing-safe compare path is actually invoked (no early-exit `string.Equals`/`==` short-circuit) (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| T-17 | `[LoggerMessage]` events fire at the correct level/EventId for malformed-token, service-principal-recognized, tenant-resolution-failure, and API-key-validation-failure scenarios, asserted via `16.Testing`'s in-memory `ILogger` double; no PII/claim/token/key value present in any logged field (WO-057, P-371) | `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey` | `●` |

---

## Phase: Docs <!-- phase-key: SK.12.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| DOC-01 | Verify XML doc comments on all public APIs in `SharedKernel.Security.Abstractions` (IUserContext, ITenantProvider, AnonymousUserContext, SecurityClaimTypes) | `SharedKernel.Security.Abstractions` | `●` |
| DOC-02 | Verify XML doc comments on all public APIs in `SharedKernel.Security.Oidc` (OidcUserContext, OidcTenantProvider, SecurityOptions + JwtOptions, AddSharedKernelSecurity, AddAzureB2CAuthentication) | `SharedKernel.Security.Oidc` | `●` |
| DOC-03 | XML doc comments on all new public APIs (`ClaimMappingOptions`, `IdentityKind`, `IUserContext.IdentityKind`/`.Permissions`/`.HasPermission`, `SystemUserContext`, all `SharedKernel.Security.ApiKey` types, `SecurityLogEvents`) (WO-057, P-366–P-371) | All | `●` |
| DOC-04 | Author `12.Security/README.md` domain-root orientation (currently empty) — matches the pattern every other domain's root README follows (WO-057, P-372) | Domain root | `●` |
| DOC-05 | Refresh `SharedKernel.Security.Abstractions/README.md` and `SharedKernel.Security.Oidc/README.md` documenting every new type/member from P-366–P-369 with a usage example each (WO-057, P-372) | `SharedKernel.Security.Abstractions`, `SharedKernel.Security.Oidc` | `●` |
| DOC-06 | Author `SharedKernel.Security.ApiKey/README.md` — states it is for pre-shared-key/machine-client scenarios only and explicitly does not attempt key issuance, rotation, or storage (WO-057, P-370/P-372) | `SharedKernel.Security.ApiKey` | `●` |
| DOC-07 | Author four end-to-end recipes: `05.Application` `IAuthorizationContext` bridge combining `HasRole`+`HasPermission`, `06.Persistence` `TenantedDbContext` wiring, `07.Messaging` `ITenantContextAccessor` bridge, and a background-execution host registering `SystemUserContext` (WO-057, P-372) | `SharedKernel.Security.Oidc` | `●` |

---

## Phase: Published <!-- phase-key: SK.12.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| PUB-01 | Verify NuGet packaging metadata present in `SharedKernel.Security.Abstractions.csproj` (PackageId, Version, Authors, Description, Tags, RepositoryUrl, README, symbols) | `SharedKernel.Security.Abstractions` | `●` |
| PUB-02 | Verify NuGet packaging metadata present in `SharedKernel.Security.Oidc.csproj` (PackageId, Version, Authors, Description, Tags, RepositoryUrl, README, symbols) | `SharedKernel.Security.Oidc` | `●` |
| PUB-03 | `dotnet build --configuration Release` succeeds with 0 errors for both packages | Both | `●` |
| PUB-04 | `dotnet pack --configuration Release` produces `.nupkg` + `.snupkg` for `SharedKernel.Security.Abstractions` | `SharedKernel.Security.Abstractions` | `●` |
| PUB-05 | `dotnet pack --configuration Release` produces `.nupkg` + `.snupkg` for `SharedKernel.Security.Oidc` | `SharedKernel.Security.Oidc` | `●` |
| PUB-06 | Final test run: 13 Abstractions + 33 Oidc tests all pass (0 failed) | Both | `●` |
| PUB-07 | NuGet packaging metadata for `SharedKernel.Security.ApiKey.csproj` (PackageId, Version, Authors, Description, Tags, RepositoryUrl, README, symbols) (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| PUB-08 | `dotnet build --configuration Release` succeeds with 0 errors across all three packages, including the new `SharedKernel.Security.ApiKey` (WO-057) | All | `●` |
| PUB-09 | `dotnet pack --configuration Release` produces `.nupkg` + `.snupkg` for `SharedKernel.Security.ApiKey` (WO-057, P-370) | `SharedKernel.Security.ApiKey` | `●` |
| PUB-10 | Version bump: `SharedKernel.Security.Abstractions` and `SharedKernel.Security.Oidc` bumped per the platform's breaking-change policy — `IdentityKind` and `Permissions`/`HasPermission` are additive-but-breaking `IUserContext` interface members requiring every implementer to be updated (WO-057, P-367/P-368) | `SharedKernel.Security.Abstractions`, `SharedKernel.Security.Oidc` | `●` |
| PUB-11 | Final test run across all three packages: full suite green (0 failed) (WO-057) | All | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.12.Design` | Design | 22 | 22 | 0 | `●` |
| `SK.12.Scaffold` | Scaffold | 15 | 15 | 0 | `●` |
| `SK.12.Core` | Core | 24 | 24 | 0 | `●` |
| `SK.12.Tests` | Tests | 17 | 17 | 0 | `●` |
| `SK.12.Docs` | Docs | 7 | 7 | 0 | `●` |
| `SK.12.Published` | Published | 11 | 11 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-02] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
- [2026-06-02] SK.12.Design → ● — all 9 design tasks complete: IUserContext, ITenantProvider, AnonymousUserContext, SecurityClaimTypes, OidcUserContext, OidcTenantProvider, SecurityOptions, AddSharedKernelSecurity, AddAzureB2CAuthentication (state-map-phase)
- [2026-06-02] SK.12.Scaffold → ● — all 9 scaffold tasks complete: both .csproj files, folder structures, test projects, solution entries, test stub files; 46 tests pass (13 Abstractions + 33 Oidc) (state-map-phase)
- [2026-06-02] SK.12.Core → ● — all 9 core tasks complete: IUserContext, ITenantProvider, AnonymousUserContext, SecurityClaimTypes, OidcUserContext, OidcTenantProvider, SecurityOptions, AddSharedKernelSecurity, AddAzureB2CAuthentication; 46 tests pass (state-map-phase)
- [2026-06-02] SK.12.Tests → ● — all 6 test tasks complete: 13 Abstractions tests + 33 Oidc tests, 46 total passing (state-map-phase)
- [2026-06-02] SK.12.Docs → ● — all 2 doc tasks complete: full XML doc coverage confirmed on all public APIs in both packages; 46 tests still passing (security-phase-implementer)
- [2026-06-02] SK.12.Published → ● — all 6 publish tasks complete: metadata already present in both .csproj files; both build 0 errors; pack produces .nupkg + .snupkg for both packages; 46 tests pass (security-phase-implementer)
- [2026-08-13] D-10 → ● through D-22 → ● in SK.12.Design — all 13 remaining WO-057 Design tasks confirmed complete: shapes for `ClaimMappingOptions`, `TokenValidationParameters` wiring, the defensive role-claim reader, `IdentityKind`, the corrected `IUserContext` invariant, service-principal detection, `Permissions`/`HasPermission`, scope-claim parsing, `SystemUserContext`, the full `SharedKernel.Security.ApiKey` package shape, constant-time comparison strategy, and the `SecurityLogEvents` EventId sub-range split were already fully specified in `12.Security/CLAUDE.md` by security-arch-planner; D-22 (README/doc structure) was the one genuine gap and was closed this session with a new "Documentation Plan" section in CLAUDE.md. SK.12.Design now 22/22 `●`; no source code written — Design is a pure specification phase (state-map-phase, security-phase-implementer)
- [2026-08-13] SC-10→SC-15 → `●` in `SK.12.Scaffold` — `SharedKernel.Security.ApiKey` project + `.Tests` created (net10.0, project-references only, `Microsoft.AspNetCore.App` FrameworkReference for `[LoggerMessage]`/`AuthenticationHandler<TOptions>`), folder structure (`Validation/`, `Extensions/`, `Options/`, `Logging/`), registered in `Platform.SharedKernel.slnx`, empty skipped test stubs, `Logging/SecurityLogEvents.cs` partial-class skeletons added to both `.Oidc` and `.ApiKey`; 46/46 pre-existing tests (13 Abstractions + 33 Oidc) still pass, 0 regressions; `SK.12.Scaffold` now 15/15 `●` (security-phase-implementer, state-map-phase)
- [2026-08-13] C-10→C-24 → `●` in `SK.12.Core` — all 15 remaining WO-057 Core tasks complete: `ClaimMappingOptions`, `TokenValidationParameters` NameClaimType/RoleClaimType wiring, defensive role-claim reader (one-Claim-per-role or JSON-array), `OidcUserContext`/`OidcTenantProvider` updated to accept `ClaimMappingOptions`/`ILogger` and resolve Email/Username/Roles/Permissions via the configured mapping, `IdentityKind` enum + `IUserContext.IdentityKind` + `OidcUserContext` IdentityKind resolution (User/ServicePrincipal/Anonymous), `IUserContext.Permissions`/`HasPermission` + `OidcUserContext` scope-claim parsing, `SystemUserContext` singleton sentinel, full `SharedKernel.Security.ApiKey` runtime (`IApiKeyValidator`, `ApiKeyValidationResult`, `ApiKeyUserContext`, `ApiKeyAuthenticationOptions`, `ApiKeyAuthenticationHandler` with a header/query ambiguity guard using an `IHmacSigner`-backed constant-time comparer, `AddApiKeyAuthentication` composing a policy/forwarding scheme alongside JWT Bearer via a captured-descriptor decorator so `IUserContext` resolves correctly for either credential type with zero cross-reference to `SharedKernel.Security.Oidc`), and `[LoggerMessage]` events `12100`/`12101` (Oidc) + `12200` (ApiKey). Cross-domain fallout fixed to keep the repo building: `IUserContext`'s two new interface members (`IdentityKind`, `Permissions`/`HasPermission`) required updating four other `IUserContext` implementers outside this domain — `06.Persistence.EfCore`'s `NoOpUserContext` and two test fixtures (`EfCorePersistenceBuilderTests.CustomUserContext`, `DbContextPoolingTests.MutableTestUserContext`), and `16.Testing`'s `FakeUserContext`/`TestSharedKernelDbContext`'s private `NoOpUserContext` — all updated with sentinel-appropriate values (`IdentityKind.User` or `.Anonymous`, empty `Permissions`, `HasPermission` false), consistent with those types' existing conventions; `16.Testing/state-map.md`'s own C-111–C-113 (`⚑` Blocked pending this exact change) can now be closed in a future `16.Testing` session. 107 tests passing across the domain (29 Abstractions + 46 Oidc + 32 ApiKey, new: 46 net-new tests for `ClaimMappingOptions`/`IdentityKind`/`Permissions`/`ApiKey`); 358/358 `06.Persistence.EfCore` and 890/890 `16.Testing.SelfTests` (non-Docker) regression-clean (security-phase-implementer, state-map-phase)
- [2026-08-13] DOC-03→DOC-07 → `●` in `SK.12.Docs` — all 5 remaining WO-057 Docs tasks complete. DOC-03: re-read every public type across all three packages (`IUserContext`/`ITenantProvider`/`AnonymousUserContext`/`SystemUserContext`/`IdentityKind`/`SecurityClaimTypes` in `.Abstractions`; `OidcUserContext`/`OidcTenantProvider`/`SecurityOptions`/`ClaimMappingOptions`/`SecurityServiceCollectionExtensions`/`SecurityLogEvents` in `.Oidc`; `IApiKeyValidator`/`ApiKeyValidationResult`/`ApiKeyUserContext`/`ApiKeyAuthenticationOptions`/`ApiKeyAuthenticationHandler`/`ApiKeyServiceCollectionExtensions`/`SecurityLogEvents` in `.ApiKey`) and confirmed full XML doc coverage already existed from the Core-phase implementer — no `.cs` edits were needed; all three packages build 0 warnings/0 errors. DOC-04: authored `12.Security/README.md` (previously empty) — Overview/Packages/Quick-Start/ordering-rules, matching the populated-root-README pattern `13.ServiceDefaults/README.md` established (chosen as the closest structural precedent among domains with a non-empty root README; most domains' root READMEs are still empty). DOC-05: refreshed `SharedKernel.Security.Abstractions/README.md` (added `IdentityKind`/`Permissions`/`HasPermission`/`SystemUserContext`/the `SecurityClaimTypes` legacy-vs-active-lookup-key callout, each with a usage example) and `SharedKernel.Security.Oidc/README.md` (added the `ClaimMapping` config section, the defensive role-claim-reader/space-delimited-permission-parsing usage note, and the four DOC-07 recipes). DOC-06: authored new `SharedKernel.Security.ApiKey/README.md` — states up front (per its own Implementation Rules disclaimer) that this package is for pre-shared-key/machine-client scenarios only and does not attempt key issuance, rotation, or storage; documents `IApiKeyValidator` implementation, DI registration ordering relative to `AddSharedKernelSecurity`, and the constant-time-comparison scope. DOC-07: authored all four end-to-end recipes inside `SharedKernel.Security.Oidc/README.md` (per the Documentation Plan's own suggested placement) — verified each cross-domain interface against real shipped source before writing any example code (not from memory): `05.Application.Behaviors.Authorization.IAuthorizationContext` (`IsAuthorizedAsync`/`AllOf`/`AnyOf`, confirmed via direct file read — no shipped bridge implementation exists in-repo, so the recipe's `UserContextAuthorizationAdapter` is new, matching the pattern `05.Application/CLAUDE.md`'s own prose already sketches); `06.Persistence.EfCore.MultiTenancy.TenantedDbContext` (5-argument protected constructor incl. `ITenantProvider`, `ApplyTenantFilters`'s `Expression.Constant(this, GetType())` rebinding mechanism — confirmed via direct file read, not assumed from `06.Persistence/CLAUDE.md` prose); `07.Messaging.Abstractions.TenantContext.ITenantContextAccessor` (single `Guid? TenantId` property, confirmed via direct file read; `MessagingBusBuilder.WithTenantContext<TAccessor>()`'s real registration shape also confirmed) — the recipe's `SecurityTenantContextAccessor` mirrors the interface's own XML-doc-embedded `HttpTenantContextAccessor` example one-for-one; and a background-execution host recipe registering `SystemUserContext.Instance` after `AddSharedKernelSecurity`, cross-referencing recipe 1's `IAuthorizationContext` bridge for how a consuming service opts `IdentityKind.System` into (or out of) bypassing fine-grained checks. All builds re-confirmed 0 errors after every doc edit (docs-only session — zero `.cs` files touched). `SK.12.Docs` now 7/7 `●` (security-phase-implementer, state-map-phase)
- [2026-08-13] T-07→T-17 → `●` in `SK.12.Tests` — all 11 remaining WO-057 Tests tasks confirmed/closed: T-07/T-08/T-10–T-14 were already fully covered by the Core-phase's own net-new test suite (verified line-by-line against each task's acceptance shape, not assumed from the "46 net-new tests" summary alone) and required no new code; three genuine gaps were found and closed — T-09 (`AddAzureB2CAuthentication`'s `TokenValidationParameters.NameClaimType`/`RoleClaimType` wiring had no test at all; three new tests added to `SecurityServiceCollectionExtensionsTests.cs`, requiring an explicit `services.AddSingleton<IConfiguration>(config)` registration since `Microsoft.Identity.Web`'s `SetIdentityModelLogger` resolves `IConfiguration` from the container itself, not merely the parameter passed to `AddAzureB2CAuthentication`), T-16 (`ConstantTimeKeyComparer` had zero direct test coverage; new `ConstantTimeKeyComparerTests.cs` added, requiring a new `InternalsVisibleTo("SharedKernel.Security.ApiKey.Tests")` grant on the main `ApiKey` project since the comparer is `internal` — proves the timing-safe path is actually invoked via a hand-rolled `RecordingHmacSigner` double showing `AreEqual`'s outcome is dictated entirely by the injected `IHmacSigner.Verify` result, never by an independent string-comparison shortcut of its own), and T-17 (the Oidc half was entirely untested — no test ever passed a logger to `OidcUserContext`/`OidcTenantProvider` — six new logging tests added across `OidcUserContextTests.cs`/`OidcTenantProviderTests.cs` proving EventId 12100/12101 fire at the correct level with no raw claim value in the message; the ApiKey half's pre-existing `EventId`-only assertions were strengthened to also assert `LogLevel.Warning` and absence of the raw presented key from the logged message). `SharedKernel.Security.Oidc.Tests.csproj` gained a `SharedKernel.Testing` `ProjectReference` (bumping `Microsoft.Extensions.DependencyInjection`/`.Hosting` from `9.0.5` to `10.0.9` to avoid an `NU1605` downgrade) — the first `12.Security` test project to reference `16.Testing`, per the root `CLAUDE.md` Test Project Rules. 43/43 `SharedKernel.Security.ApiKey.Tests` (32 pre-existing + 11 new) and 59/59 `SharedKernel.Security.Oidc.Tests` (46 pre-existing + 13 new) all green, 0 regressions. `SK.12.Tests` now 17/17 `●` (security-phase-implementer, state-map-phase)
- [2026-08-13] WO-057: seven phases (P-366–P-372) dispatched from arch-lead — a claim-type-resolution correctness fix (P-366, `MapInboundClaims = false` since .NET 8 means unmapped short-name claims like `"email"`/`"roles"` silently resolve to null/empty against every standards-conformant IdP's default token shape, confirmed as this domain's own test suite masking the defect by only ever constructing fixtures with the legacy long-form `ClaimTypes.*` shape), a new `IdentityKind` discriminator distinguishing service-principal/client-credentials tokens from a rejected/unauthenticated caller (P-367), a `Permissions`/`HasPermission` fine-grained scope surface alongside the existing role check (P-368), a `SystemUserContext` sentinel for background-execution hosts distinct from `AnonymousUserContext` (P-369), a new sibling provider package `SharedKernel.Security.ApiKey` for pre-shared-key/machine-client authentication built on `01.Core/SharedKernel.Cryptography`'s constant-time primitives (P-370), this domain's first-ever `[LoggerMessage]`-authored structured logging retrofit under the platform's WO-041 convention with EventId range `12000`–`12999` (P-371), and a full documentation/recipe refresh including the currently-empty domain-root `README.md` (P-372). 13 new Design tasks (D-10–D-22), 6 new Scaffold tasks (SC-10–SC-15, including the new `SharedKernel.Security.ApiKey`/`.Tests` projects), 15 new Core tasks (C-10–C-24), 11 new Tests tasks (T-07–T-17), 5 new Docs tasks (DOC-03–DOC-07), and 5 new Published tasks (PUB-07–PUB-11, including a version bump for the `IUserContext`-breaking `IdentityKind`/`Permissions` additions) appended across all six phases; all six phase states moved from `●` back to `◐` to reflect the new pending work; Package Board gained a `SharedKernel.Security.ApiKey` row (WO-057, Design, `○`); Cross-Domain Dependencies gained a `SharedKernel.Cryptography` entry for the new package (security-arch-planner)
- [2026-08-13] PUB-07→PUB-11 → `●` in `SK.12.Published` — all 5 remaining WO-057 Published tasks complete. PUB-07: confirmed `SharedKernel.Security.ApiKey.csproj` carried full packaging metadata already, but found and fixed a real gap during pack verification — it declared `PackageReadmeFile` yet never included a `<None Include="README.md" Pack="true" PackagePath="\" />` item (unlike `.Abstractions`/`.Oidc`), so `dotnet pack` failed with `NU5039` ("readme file not found in package"); added the missing item. PUB-08: all three packages build 0 errors/0 warnings in `Release` (Oidc carries 16 pre-existing `NU1903` transitive-vulnerability advisories from `Microsoft.Identity.Web`'s `System.Security.Cryptography.Xml` 9.0.0 dependency — unrelated to this session, not fixed here). PUB-09: `SharedKernel.Security.ApiKey.1.0.0.nupkg`+`.snupkg` produced in root `nupkgs/`, contents verified (README.md + DLL + XML doc file present). PUB-10: `SharedKernel.Security.Abstractions` and `SharedKernel.Security.Oidc` both bumped `1.0.0` → `2.0.0` (major, breaking — `IdentityKind`/`Permissions`/`HasPermission` are new non-optional `IUserContext` interface members), each gaining a `PackageReleaseNotes` block naming the breaking members and citing WO-057; both re-packed at `2.0.0` (`SharedKernel.Security.Abstractions.2.0.0.nupkg`/`SharedKernel.Security.Oidc.2.0.0.nupkg`, mirroring the `04.Contracts`/WO-052/P-328 precedent for a breaking-interface major bump). `SharedKernel.Security.ApiKey` stays at `1.0.0` — a first release, not a breaking change. PUB-11: full suite green across all three packages — 29 Abstractions + 59 Oidc + 43 ApiKey = 131/131, 0 failed, 0 regressions. Per this phase's own scope note, **no package was pushed to a NuGet feed** — only local pack artifacts (`nupkgs/`) and metadata were produced; distribution remains a separate, user-authorized decision (mirrors `11.Communication`'s PB-06 precedent). Package Board `SharedKernel.Security.ApiKey` row moved `Design ○` → `Published ●` (v1.0.0). `SK.12.Published` now 11/11 `●` — all six phase keys for `12.Security` are `●`, WO-057 (P-366–P-372) complete end to end (security-phase-implementer, state-map-phase)
