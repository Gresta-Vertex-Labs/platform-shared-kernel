# 12.Security — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Security.Abstractions` | Abstractions | ● | `IUserContext` (subject, client, `TenantId?`, `ActorKind`, session, roles, permissions, `amr`/`acr`/`auth_time`, sender-constraint), `IUserContextMapper` + `UserContextResolver`, `AnonymousUserContext`/`SystemUserContext`, `SecurityClaimTypes`. References only `SharedKernel.Execution`. |
| `SharedKernel.Security.Oidc` | Host | ● | JWT bearer for any OIDC provider: configurable claim mapping, `ValidAlgorithms` allow-list (asymmetric only), DPoP with `ath` binding, certificate-bound tokens (`cnf.x5t#S256`), token revocation + revocation cache (fail closed). |
| `SharedKernel.Security.ApiKey` | Host | ● | Managed keys (`AddManagedApiKeyAuthentication<TStore>`) or a custom validator; header only; multi-key rotation (`ApiKeyRotationComparer`). |
| `SharedKernel.Security.Mtls` | Host | ● | Client-certificate authentication (`AddMtlsAuthentication<TValidator>`), `IMtlsCertificateValidator`, private-CA trust (`CustomRootTrust` + `CustomTrustStore`), RFC 8705 binding. |
| `SharedKernel.Security.Totp` | Host | ● | `TotpEnrollmentService`, `TotpChallengeService` over `ITotpChallengeStore`, `TotpStepUpClaimsTransformation` (session-bound `amr=otp` for `FreshnessWindow`), recovery codes; built on `SharedKernel.Cryptography`'s TOTP primitives. |

Test doubles: `FakeUserContext`, test certificates and DPoP proofs in `16.Testing/SharedKernel.Security.Testing`. Application code reads `IRequestContext` (built over `IUserContext` by `13.ServiceDefaults`' `AddSharedKernelRequestContext()`), never `IUserContext` for the tenant.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.12.Design` | Design (D-01–D-57) | ● |
| `SK.12.Scaffold` | Scaffold (SC-01–SC-37; SC-34 `.slnx` registration of `.Totp` verified on disk 2026-09-28) | ● |
| `SK.12.Core` | Core (C-01–C-57) | ● |
| `SK.12.Tests` | Tests (T-01–T-48) | ● |
| `SK.12.Docs` | Docs (DOC-01–DOC-24) | ● |
| `SK.12.Published` | Published (PUB-01–PUB-23) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (`01.Core`'s TOTP primitives, P-451, and the `ITotpReplayGuard` change, P-514/P-528, have shipped.)

## Completed Phases

- WO-086 ● `IdentityKind` → `ActorKind`; tenants `TenantId?`; `ITenantProvider` deleted; providers to Host tier; `Require*` attributes moved to `Presentation.Core` (P-565, P-574, P-575) (2026-09-26)
- WO-083 ● Consumers of the `ITotpReplayGuard`/`TotpVerifier.VerifyAsync` change fixed (P-528) (2026-09-09)
- WO-069 ● `SharedKernel.Security.Totp` end to end (P-452) (2026-09-04)
- WO-060 ● DPoP `ath` binding, JWS algorithm allow-list, revocation cache, API-key rotation comparer (P-385–P-389, P-392) (2026-08-18)
- WO-058 ● Step-up signals, DPoP, certificate-bound tokens, `SharedKernel.Security.Mtls` (P-375–P-379) (2026-08-14)
- WO-057 ● Claim-type resolution fix, service principals, `SharedKernel.Security.ApiKey` (P-366–P-372) (2026-08-13)
- Earlier phases (initial Abstractions + Oidc Design → Published) — archived.

## Changelog

- [2026-09-28] State map rewritten as a living board; SC-34 closed (the `.Totp` projects are in `Platform.SharedKernel.slnx` and the Unit lane).
- [2026-09-26] WO-086 recorded (root P-565, P-574, P-575); `CLAUDE.md` and all READMEs rewritten.
- [2026-09-09] P-528 (WO-083) implemented and verified.
- [2026-09-04] WO-069 / P-452 (`SharedKernel.Security.Totp`) implemented end to end.
- [2026-08-26] WO-069 (P-452) dispatched from arch-lead.
