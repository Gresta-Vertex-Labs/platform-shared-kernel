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

| Phase key | Phase | Status |
| --- | --- | :---: |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

- `SK.12.RevocationFailureLogging` T-01 to T-05 and `SK.12.SecurityConformance` T-01, T-02 use the P-581 assertions (`ShouldHaveLoggedWithException`, `ShouldNotLeak`, `ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock`) and `LoggingEventIdRanges.IsInSubBlock` from P-580. A search of `src/` finds none of them on disk (both phases are `○` on their boards). The implementer marks only those tasks `⚑` if the producers are still absent; the code tasks proceed with the existing `InMemoryLogger` assertions.

## Cross-Domain Dependencies

- **01.Core, `SK.01.ObservabilityHelpers` (P-580):** `LoggingEventIdRanges.IsInSubBlock` and `PackageSubBlockWidth` for the sub-block tests (C-01, T-01 of `SK.12.SecurityConformance`). Nothing else is consumed: Security records no span or metric.
- **16.Testing, `SK.16.TelemetryCapture` (P-581):** the log assertions named under Blocked. The Security `.Testing` package needs no new double (no new consumer-implemented store or validator).
- **13.ServiceDefaults (P-611):** its split of the shared 13000-13099 range is independent of this domain; every Security event is in 12xxx. Note for that planner: `Security.Mtls` is 12300 and `ServiceDefaults.Security.Mtls` is 13xxx, so tests must not conflate them.
- **00.Governance, `SK.00.ObservabilityReportOnly` (P-583):** the zero-findings acceptance of `SK.12.SecurityConformance` reads its SK0044 and EventId reports. Its error-code check must tolerate a domain with no `{Capability}ErrorCodes` class (Security produces no `Error`; see the deviation in the phase).
- **arch-lead:** (1) confirm or overrule the `SecurityErrorCodes` deviation; (2) root L6 has no carve-out for a handled, degraded exception (12106 stays Warning); (3) root L5 lists "actor id, client id" but not credential handles: the managed API key id is logged by design (rule 23), consider listing "key ids" under Allowed. Pre-existing text follows. (`01.Core`'s TOTP primitives, P-451, and the `ITotpReplayGuard` change, P-514/P-528, have shipped.)
