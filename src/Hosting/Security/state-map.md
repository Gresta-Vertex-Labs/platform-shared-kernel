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
| `SK.12.SecurityConformance` | Phase 2: Security conforms to root "Observability, exceptions and error codes" (WO-087 / P-610) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.12.SecurityConformance — Conform Security to the observability gold standard `○`

**Work order:** WO-087 / P-610 · **Depends on:** SK.12.RevocationFailureLogging (P-593), SK.01.ObservabilityHelpers (P-580), SK.16.TelemetryCapture (P-581), SK.13.WildcardTelemetry (P-582, nothing to do here; recorded only), SK.00.ObservabilityReportOnly (P-583)

Applies the applicable rules of root "Observability, exceptions and error codes" to the four logging packages. Verified scope on HEAD: 15 `[LoggerMessage]` events with bare literal ids in `OidcLog` (12100-12106), `ApiKeyLog` (12200-12201), `MtlsAuthenticationLog` (12300-12301) and `TotpLog` (12400-12402), plus 12107 from P-593; log classes sit in `Logging/` and `Internal/`; templates use `(key: value)` parentheticals and `{Reason}`; the domain emits no `ActivitySource`, `Meter` or `Activity` and produces no `Error` or `SharedKernelException` (it speaks `AuthenticateResult.Fail` and WWW-Authenticate to ASP.NET Core). Rules that apply: L1-L8, E2/E3 (configuration and guard messages), SK0022 for reason strings. Rules that do not apply, recorded in `## Decisions`: T1-T8 (no telemetry; adding authentication metrics is a feature for its own work order, and `AddServiceDefaults`' `SharedKernel.*` wildcard would pick it up unchanged), E1 and C1-C3 (nothing classified is thrown or returned as an `Error`). **Deviation from the P-610 text, for arch-lead to confirm:** the entry asks for a public `SecurityErrorCodes`; the domain has no code to put in it, and an empty public class is dead API (C2's purpose is that results carry an `Error`). No class is created; the decision and the revisit trigger (first time a Security API returns an `Error`) are recorded. Wire values that stay: RFC 6750/9449 `error` values (`invalid_token`, `use_dpop_nonce`) are protocol-defined and exempt from C1.

EventId layout (ratifies D-01): Security has five packages and ten 100-wide slots, so the one-block-per-package rule holds and the package-family fallback of `SK.01.CoreConformance` is not needed. Abstractions 12000-12099 (no logging dependency, no `LoggingEventIds` class), Oidc 12100-12199, ApiKey 12200-12299, Mtls 12300-12399, Totp 12400-12499, 12500-12999 free. Every Security event is in 12xxx, so nothing here shares a sub-block with `ServiceDefaults.Security`, `ServiceDefaults.Security.Mtls` or Localization (their 13000-13099 overlap is `13.ServiceDefaults`' to split, P-611); no id is renumbered, and a test pins each `Base` so `Security.Mtls` (12300) cannot be confused with `ServiceDefaults.Security.Mtls` (13xxx).

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the sub-block table above and that no EventId changes number. Run the P-583 report for the domain first and take its findings as the checklist | all four logging packages | `○` |
| D-02 | Ratify the template table: 12100 "The token names neither a subject nor a client id, so the caller is anonymous."; 12101 "The tenant claim '{TenantClaimType}' is not a valid GUID, so the caller has no tenant."; 12102 "DPoP validation failed with rejection code '{RejectionCode}'."; 12103 "The access token was rejected because it is revoked."; 12104 "The signing algorithm '{Algorithm}' is not allowed."; 12105 "The certificate binding was rejected with rejection code '{RejectionCode}'."; 12106 "The revocation cache failed, so the revocation check is called directly."; 12107 "The revocation check failed, so the access token was rejected."; 12200 "The API key was rejected with rejection code '{RejectionCode}' for key id '{KeyId}'."; 12201 "The request carries more than one '{HeaderName}' header value."; 12300 "The client certificate was rejected with rejection code '{RejectionCode}' for thumbprint '{Thumbprint}'."; 12301 "The certificate validator failed, so the client certificate was rejected for thumbprint '{Thumbprint}'."; 12400 "The TOTP challenge was not accepted with result '{Result}' during operation '{Operation}'."; 12401 "The TOTP step-up completed during operation '{Operation}'."; 12402 "A recovery code was redeemed and {Remaining} unused recovery codes remain." Placeholder `{Reason}` becomes `{RejectionCode}` because the values are closed codes, not free text (L3). The managed key id stays a loggable value (rule 23: not a client id) | all four logging packages | `○` |
| D-03 | Ratify levels (L6): expected authentication failures Warning, validator exception 12301 and revocation-check exception 12107 Error, TOTP lifecycle Information. 12106 (revocation cache exception, request continues) stays **Warning** as a deliberate degraded-but-handled case: Error per request during a cache outage would flood logs. Root L6 has no such carve-out; record the departure and ask arch-lead to confirm or reword L6 | `SharedKernel.Security.Oidc` | `○` |
| D-04 | Ratify categories (L7) and loggers (L8). All services take required `ILogger<T>` of a kernel type; `OidcJwtBearerHandler` and `ApiKeyAuthenticationHandler` log through the framework `Logger`, whose category is the handler's full type name (accepted: it is the kernel type's name, not a consumer or hand-typed category); `MtlsCertificateEvents` resolves `ILoggerFactory.CreateLogger<MtlsCertificateEvents>()` from the request services (required, accepted). Pin each category in a test | all four logging packages | `○` |
| D-05 | Ratify the non-applicable rules and the `SecurityErrorCodes` deviation as `## Decisions` rows (T1-T8, E1, C1-C3). Ratify the reason vocabulary: log-only rejection codes keep their current PascalCase values (`Expired`, `UnknownKey`, `MalformedConfirmation`, ...) and become internal constants (`DpopRejectionReasons`, `CertificateBindingReasons`, `ApiKeyRejectionReasons`, `MtlsRejectionReasons`); values do not change | all four logging packages | `○` |
| D-06 | E2/E3 sweep list: `UserContext` ctor `ArgumentOutOfRangeException`, `ApiKeyValidationResult`/`MtlsValidationResult` tenant guards, `ApiKeyGenerator` prefix `InvalidOperationException`, `OidcAuthenticationOptionsValidator`, `ConfigureCertificateOptions`, `MtlsServiceCollectionExtensions` validation messages. Each becomes a sentence ending in a period with quoted values, "must not" phrasing, naming the remedy, no inner `ex.Message`. Move the `ApiKeyGenerator` prefix check into `ManagedApiKeyOptions` validation (fail at startup, `ValidateOnStart`) and keep the generator guard as defence | `SharedKernel.Security.Abstractions`, `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey`, `SharedKernel.Security.Mtls` | `○` |
| C-01 | `OidcLoggingEventIds`, `ApiKeyLoggingEventIds`, `MtlsLoggingEventIds`, `TotpLoggingEventIds` (`internal static class`, `Base = LoggingEventIdRanges.Security + n * LoggingEventIdRanges.PackageSubBlockWidth`, each event `Base + k`); no bare literal remains in any `[LoggerMessage]` | four logging packages | `○` |
| C-02 | Move each log class to `Diagnostics/` as `internal static partial class {Package}Log` (`OidcLog`, `ApiKeyLog`, `MtlsLog` renamed from `MtlsAuthenticationLog`, `TotpLog`), plain static partial methods; rewritten templates and `{RejectionCode}` placeholder per D-02; levels per D-03; exception last on 12106, 12107, 12301; call sites updated | four logging packages | `○` |
| C-03 | Rejection-reason constants per D-05 replacing the string literals in `DpopProofValidator`, `OidcJwtBearerHandler`, `CertificateBinding`, `ApiKeyAuthenticationHandler`, `ManagedApiKeyValidator`, `MtlsCertificateEvents` (SK0022) | Oidc, ApiKey, Mtls | `○` |
| C-04 | Message rewrites and the `ApiKeyGenerator` start-up validation per D-06 | Abstractions, Oidc, ApiKey, Mtls | `○` |
| T-01 | Per package, a logging conformance test over a captured logger driving every event: id equals `{Package}LoggingEventIds` constant and lies in the package sub-block (`IsInSubBlock`), template matches D-02 text and ends with a period, level per D-03, exception present on 12106/12107/12301 and absent elsewhere, category per D-04, no free-text placeholder (`ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock`). Literal pins for 12100-12107, 12200-12201, 12300-12301, 12400-12402. Mutation check: renumbering one event or restoring a literal fails | four `*.Tests` projects (Unit) | `○` |
| T-02 | Existing tests that match `new EventId(12xxx)` or the `Reason` property move to the constants and the `RejectionCode` property through the P-581 helpers; `TotpLog` id constants replace the per-test `private const int` copies | four `*.Tests` projects (Unit) | `○` |
| T-03 | Message tests: every thrown or validation message in the D-06 list ends with a period, quotes values, contains no inner exception text; the `ApiKeyGenerator` prefix fails at host start through `ValidateOnStart` | `SharedKernel.Security.ApiKey.Tests`, `SharedKernel.Security.Oidc.Tests`, `SharedKernel.Security.Mtls.Tests`, `SharedKernel.Security.Abstractions.Tests` (Unit) | `○` |
| T-04 | Fail-closed regression: the full Oidc, ApiKey, Mtls and Totp end-to-end suites stay green unchanged in behaviour (status codes, challenges) | four `*.Tests` projects (Unit) | `○` |
| DO-01 | `PublicAPI.Unshipped.txt` (expected: only the `FailureReason`/message-text changes, no signature change; state it per package); READMEs of Oidc, ApiKey, Mtls, Totp (EventId tables with the rewritten templates and `RejectionCode`, Reference and Design decisions); domain README package notes | four logging packages | `○` |
| DO-02 | Domain `CLAUDE.md`: Logging table, rule 23 wording, Decisions rows (T1-T8, E1, C1-C3 not applicable, `SecurityErrorCodes` deviation, reason vocabulary); remove the *(planned)* markers | `src/Hosting/Security` | `○` |

**Acceptance:**
- [ ] Every applicable rule holds (L1-L8, E2/E3, SK0022); the P-583 report-only tests show zero findings for the domain.
- [ ] No bare EventId literal in the domain; no sub-block shared with `ServiceDefaults.Security`, `ServiceDefaults.Security.Mtls` or Localization; no EventId renumbered.
- [ ] The domain emits no telemetry and declares no `SharedKernel.Security` source or meter; the decision and the revisit trigger are in `CLAUDE.md`.
- [ ] No `SecurityErrorCodes` class is created unless arch-lead overrules the deviation above; the decision is recorded.
- [ ] Breaking changes listed under a "Breaking changes" heading in the commit/PR body for the release notes: log property `Reason` renamed `RejectionCode` (12102, 12105, 12200, 12300), `Operation`/`Result` message text changes, rewritten templates, log class and namespace moves (internal), start-up failure for an invalid managed API key prefix.
- [ ] `PublicAPI.Unshipped.txt`, package READMEs and the domain `CLAUDE.md` updated in the same change; tests assert logs through the P-581 capture helpers, not by string search.

## Blocked

- `SK.12.RevocationFailureLogging` T-01 to T-05 and `SK.12.SecurityConformance` T-01, T-02 use the P-581 assertions (`ShouldHaveLoggedWithException`, `ShouldNotLeak`, `ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock`) and `LoggingEventIdRanges.IsInSubBlock` from P-580. A search of `src/` finds none of them on disk (both phases are `○` on their boards). The implementer marks only those tasks `⚑` if the producers are still absent; the code tasks proceed with the existing `InMemoryLogger` assertions.

## Cross-Domain Dependencies

- **01.Core, `SK.01.ObservabilityHelpers` (P-580):** `LoggingEventIdRanges.IsInSubBlock` and `PackageSubBlockWidth` for the sub-block tests (C-01, T-01 of `SK.12.SecurityConformance`). Nothing else is consumed: Security records no span or metric.
- **16.Testing, `SK.16.TelemetryCapture` (P-581):** the log assertions named under Blocked. The Security `.Testing` package needs no new double (no new consumer-implemented store or validator).
- **13.ServiceDefaults (P-611):** its split of the shared 13000-13099 range is independent of this domain; every Security event is in 12xxx. Note for that planner: `Security.Mtls` is 12300 and `ServiceDefaults.Security.Mtls` is 13xxx, so tests must not conflate them.
- **00.Governance, `SK.00.ObservabilityReportOnly` (P-583):** the zero-findings acceptance of `SK.12.SecurityConformance` reads its SK0044 and EventId reports. Its error-code check must tolerate a domain with no `{Capability}ErrorCodes` class (Security produces no `Error`; see the deviation in the phase).
- **arch-lead:** (1) confirm or overrule the `SecurityErrorCodes` deviation; (2) root L6 has no carve-out for a handled, degraded exception (12106 stays Warning); (3) root L5 lists "actor id, client id" but not credential handles: the managed API key id is logged by design (rule 23), consider listing "key ids" under Allowed. Pre-existing text follows. (`01.Core`'s TOTP primitives, P-451, and the `ITotpReplayGuard` change, P-514/P-528, have shipped.)
