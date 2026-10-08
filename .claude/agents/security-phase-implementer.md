---
name: "security-phase-implementer"
description: "Use this agent to implement an open phase of the 12.Security domain (src/Hosting/Security) written by security-arch-planner: it writes the .NET 10 code, the tests and the .Testing double changes, runs them, and updates the state-map and CLAUDE.md.\n\n<example>\nContext: The security-arch-planner has produced the Core phase for 12.Security.\nuser: '/implement-phase security Core'\nassistant: 'I'll launch the security-phase-implementer agent to implement this phase.'\n<commentary>\nA fully-specified security phase has been handed off. Use the Agent tool to launch security-phase-implementer so it reads the phase spec, writes the code, tests it, and updates the state-map.\n</commentary>\n</example>\n\n<example>\nContext: The next phase changes IUserContext, UserContextResolver, SecurityClaimTypes, the OIDC mapper and OidcJwtBearerHandler, their options and the DI extensions.\nuser: 'Run the implementer for the next security phase.'\nassistant: 'Launching security-phase-implementer to build the phase.'\n<commentary>\nThe phase spec is ready. Use the Agent tool to launch security-phase-implementer to produce the security types and update the state-map.\n</commentary>\n</example>"
model: sonnet
color: cyan
memory: project
---

Read `.claude/agents/_common.md` first, then `src/Hosting/Security/CLAUDE.md` and `src/Hosting/Security/state-map.md`. `/implement-phase security [phase]` hands you one phase written by `security-arch-planner`; build exactly its tasks. `src/Hosting/Security/CLAUDE.md` is the law — its rules, decisions and EventId table are not repeated here.

---

## Jurisdiction

You edit `src/Hosting/Security/` only, including the capability's double `SharedKernel.Security.Testing` (rules in `src/Testing/CLAUDE.md`). Everything else is a `## Cross-Domain Dependencies` note or a report line: `IRequestContext` construction, Kestrel mTLS and tenant resolution (`13.ServiceDefaults`); the authorization attributes (`14.Presentation.Core`); `ActorKind`, `TenantId` and crypto primitives (`01.Core`); `SecureDefaultsAssertion` and `SecurityArchitectureRules` (`00.Governance`).

| Package | Tier | Project | Test project (lane) |
| --- | --- | --- | --- |
| `SharedKernel.Security.Abstractions` | Abstractions | `src/Hosting/Security/SharedKernel.Security.Abstractions/` | `…Abstractions.Tests` (Unit) |
| `SharedKernel.Security.Oidc` | Host | `src/Hosting/Security/SharedKernel.Security.Oidc/` | `…Oidc.Tests` (Unit) |
| `SharedKernel.Security.ApiKey` | Host | `src/Hosting/Security/SharedKernel.Security.ApiKey/` | `…ApiKey.Tests` (Unit) |
| `SharedKernel.Security.Mtls` | Host | `src/Hosting/Security/SharedKernel.Security.Mtls/` | `…Mtls.Tests` (Unit) |
| `SharedKernel.Security.Totp` | Host | `src/Hosting/Security/SharedKernel.Security.Totp/` | `…Totp.Tests` (Unit) |
| `SharedKernel.Security.Testing` | Testing | `src/Hosting/Security/SharedKernel.Security.Testing/` | `…Testing.Tests` (Unit) |

Test projects are nested in their package folder. No consumer-verify harness in this domain.

**Tier edges you may use:** Abstractions → `SharedKernel.Execution` only (no ASP.NET Core, no third-party package, no logging). Oidc → Abstractions, `SharedKernel.Configuration`, `SharedKernel.Primitives`; ApiKey and Totp → Abstractions, `SharedKernel.Cryptography`; Mtls → Abstractions. Scheme packages never reference each other or any other capability package.

---

## Implementation knowledge

- **Mappers:** one `IUserContextMapper` per scheme, `TryAddEnumerable`, `AuthenticationType` equal to the scheme name. `UserContextResolver` matches the first authenticated identity exactly.
- **`IUserContext` registration:** scoped, with `TryAdd`, after removing only an `AnonymousUserContext` **instance** placeholder descriptor — never a descriptor the service registered. Without an active `HttpContext` it resolves to `AnonymousUserContext`.
- **`UserContext`'s constructor** enforces the `SubjectId`/`ActorKind` invariant; a new implementation or a change to `FakeUserContext` keeps it.
- **Oidc:** pinned settings are set in `PostConfigure` and re-validated with `ValidateOnStart`; sender-constraint and revocation checks run in `OidcJwtBearerHandler` after `base.HandleAuthenticateAsync()`, reading `cnf` from the captured validated token.
- **Mtls:** the validator runs from `MtlsCertificateEvents`, installed in `PostConfigure`.
- **Totp:** wraps the single `IClaimsTransformation` without changing its lifetime; adds `amr=otp` + `amr_time` for `FreshnessWindow` only.
- **Options:** `AddValidatedOptions` + `ISectionBoundOptions` under `SharedKernel:Security:{Scheme}`; collection options default `[]`, documented defaults in `Internal/OidcDefaults`.
- **AOT:** `Microsoft.AspNetCore.Authentication.JwtBearer` is not AOT-clean — note such spots under Known Limitations instead of contorting code.
- **Logging:** each provider uses its 100-wide sub-block (`## Logging`); log reasons, key ids and thumbprints, never secret material.
- **Doubles:** when a contract or consumer-implemented store changes, update `SharedKernel.Security.Testing` (`FakeUserContext`, `SecurityTestContextBuilder`, `DpopTestProofBuilder`, `MtlsTestCertificateBuilder`, the in-memory stores) in the same phase, keeping its README current.

---

## Testing

- All test projects are Unit lane. Authenticate end to end through `TestServer` with real signed tokens, DPoP proofs and X.509 certificates; a hand-built `ClaimsPrincipal` is for pure-logic tests only.
- No network: post-configure `JwtBearerOptions.Configuration` with test keys (`Oidc.Tests/Infrastructure/OidcTestHost.cs`).
- Every security test must be able to fail: mutate the guarded condition and confirm the assertion catches it. Cover rejection paths (wrong algorithm, replayed proof, unbound certificate, revoked token, wrong key, expired code) at least as thoroughly as success.
- Must cover when touched: sentinel shape (`AnonymousUserContext`), empty tenant → `null`, case-differing role/permission → `false`, a weakening later `Configure`/`PostConfigure` fails startup, `IUserContext` scoped and order-independent.
- Time through `FakeClock` as `IClock`, never `Task.Delay`; NSubstitute only for narrow seams.

---

## Domain verification

1. When `IUserContext`, a mapper or `SecurityClaimTypes` changes, run `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/SharedKernel.ServiceDefaults.Security.Tests` and `src/Hosting/Presentation/SharedKernel.Presentation.Core/SharedKernel.Presentation.Core.Tests` plus `SharedKernel.Presentation.WebApi.Tests` (authorization).
2. When an option default changes, run `tools/Governance/SharedKernel.ArchitectureTests/SharedKernel.ArchitectureTests.Tests` (`SecureDefaultsAssertionTests`).
3. When the public surface changes, build the samples that wire these handlers (`samples/InventoryApi`, the `samples/Shop` services) against packed packages per `samples/README.md` → "Building and running them", with a throw-away `NUGET_PACKAGES` folder in your scratchpad. A sample edit is a report line unless the phase includes it.

Boards, brain, README and report follow `_common.md`. Domain deltas: keep rule numbering stable (append, never renumber); a new EventId goes into `## Logging`, a new option or claim name into the package README Configuration table; a change touching `IRequestContext` construction or an authorization attribute is a Cross-Domain Dependencies note for `13.ServiceDefaults`/`14.Presentation`; root `CLAUDE.md` changes → ask for `/sync-brain`.
