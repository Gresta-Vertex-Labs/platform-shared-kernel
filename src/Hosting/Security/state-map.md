# 12.Security — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

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

Test doubles: `FakeUserContext`, test certificates and DPoP proofs in `src/Hosting/Security/SharedKernel.Security.Testing`. Application code reads `IRequestContext` (built over `IUserContext` by `13.ServiceDefaults`' `AddSharedKernelRequestContext()`), never `IUserContext` for the tenant.

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (`01.Core`'s TOTP primitives, P-451, and the `ITotpReplayGuard` change, P-514/P-528, have shipped.)
