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

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.12.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Error`, `Result<T>`) | Available |

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

---

## Phase: Docs <!-- phase-key: SK.12.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package | State |
| -- | ---- | ------- | :---: |
| DOC-01 | Verify XML doc comments on all public APIs in `SharedKernel.Security.Abstractions` (IUserContext, ITenantProvider, AnonymousUserContext, SecurityClaimTypes) | `SharedKernel.Security.Abstractions` | `●` |
| DOC-02 | Verify XML doc comments on all public APIs in `SharedKernel.Security.Oidc` (OidcUserContext, OidcTenantProvider, SecurityOptions + JwtOptions, AddSharedKernelSecurity, AddAzureB2CAuthentication) | `SharedKernel.Security.Oidc` | `●` |

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

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.12.Design` | Design | 9 | 9 | 0 | `●` |
| `SK.12.Scaffold` | Scaffold | 9 | 9 | 0 | `●` |
| `SK.12.Core` | Core | 9 | 9 | 0 | `●` |
| `SK.12.Tests` | Tests | 6 | 6 | 0 | `●` |
| `SK.12.Docs` | Docs | 2 | 2 | 0 | `●` |
| `SK.12.Published` | Published | 6 | 6 | 0 | `●` |

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
