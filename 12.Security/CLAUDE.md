# 12.Security — Identity, Authentication & Step-Up

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md).
> This brain holds what the source does not make obvious: rules, traps, couplings and decisions.
> Work-order narrative before the 2026-09-16 redesign (WO-057 through WO-069) lives in
> [`CLAUDE.history.md`](CLAUDE.history.md) and describes types that no longer exist.

## What This Domain Is

Who is calling, and how sure we are. Application code reads the caller through `IUserContext` and the tenant
through `ITenantProvider`; the provider packages turn an ASP.NET Core authentication result into that context.
`12.Security` references only `01.Core`.

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Security.Abstractions` | `IUserContext`, `ITenantProvider`, `IdentityKind`, `UserContext`, `AnonymousUserContext`, `SystemUserContext`, `IUserContextMapper`, `UserContextResolver`, `UserContextTenantProvider`, `SecurityClaimTypes` | None; no ASP.NET Core |
| `SharedKernel.Security.Oidc` | JWT bearer for any OIDC provider; DPoP (RFC 9449); certificate-bound tokens (RFC 8705); token revocation | Abstractions, `SharedKernel.Configuration`, `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `SharedKernel.Security.ApiKey` | Managed API keys (format, generator, hashed store, validator) or a custom validator | Abstractions, `SharedKernel.Cryptography`, ASP.NET Core shared framework |
| `SharedKernel.Security.Mtls` | Client certificate authentication with an `IMtlsCertificateValidator` | Abstractions, `Microsoft.AspNetCore.Authentication.Certificate` |
| `SharedKernel.Security.Totp` | TOTP enrollment, code and recovery-code checks, session step-up claims transformation | Abstractions, `SharedKernel.Cryptography`, ASP.NET Core shared framework |

Provider packages never reference each other. Every package tracks its public API (`PublicAPI.*.txt`, RS0016/RS0017
as errors) and fails the build on an undocumented public member.

## The identity model

- `SubjectId` is a **string**. Identity providers issue `auth0|…`, Okta ids, Entra pairwise ids; a `Guid` subject
  turned every such user into a service account. `SubjectId` is non-null exactly for `User` and `ServicePrincipal`.
  `UserContext`'s constructor enforces it; keep it enforced in any new implementation.
- `IsAuthenticated` is derived from `IdentityKind` (`true` for everything but `Anonymous`). No implementation, fake
  included, may let the two disagree.
- `HasRole`, `HasPermission` and `WasAuthenticatedWith` compare **ordinally**. OAuth scopes are case-sensitive
  (RFC 6749 §3.3); a case-insensitive match can grant `Orders.Write` to a holder of `orders.write`.
- `TenantId` is `Guid?` from the credential; `UserContextTenantProvider` turns `null` into `Guid.Empty`, the
  platform's no-tenant sentinel (a tenant filter over `Guid.Empty` returns no rows).
- `FindClaim`/`FindClaims` replace the old first-value-wins dictionary, which hid multi-valued claims.
- **Method times** (P-562 X1). `GetAuthenticationMethodTime(method)` is non-null only when `WasAuthenticatedWith(method)`:
  the latest `amr_time` claim for it (`SecurityClaimTypes.AuthenticationMethodTime`, value `{method} {unix seconds}`,
  written and read only through `AuthenticationMethodTimeClaim`), else `AuthTime` (a method the credential carried was
  verified at sign-in). Every mapper sets `UserContext.AuthenticationMethodTimes` from `AuthenticationMethodTimeClaim.Read`;
  ApiKey and Mtls also map `amr`, which their handlers never issue but a claims transformation may add. The interface
  member is default-implemented (`null`), so an older implementation compiles and fails closed under a maximum age.
  This is the only thing that bounds a step-up on a SignalR connection, whose principal outlives its request: through
  `14.Presentation`'s `[RequireAuthenticationMethod(…, MaxAgeSeconds = n)]` on the hub method, checked on every call (on
  the hub class or `MapHub<T>()` only when the connection opens). A gRPC streaming call is authorized once, when it
  starts, so a stream checks `GetAuthenticationMethodTime` itself. Code that adds an `amr`
  value after sign-in must add its `amr_time` too, or the method is dated from `AuthTime`.

## Composition rules (the traps)

- **One mapper per scheme.** Each provider registers an `IUserContextMapper` (`TryAddEnumerable`) whose
  `AuthenticationType` equals its scheme name, and the identities it issues carry that authentication type
  (`TokenValidationParameters.AuthenticationType = "Bearer"`, `new ClaimsIdentity(claims, Scheme.Name, …)`).
  `UserContextResolver` picks by exact match on the first authenticated identity. An identity from a scheme with
  no mapper (a cookie, a custom handler) resolves to **anonymous** — never an authenticated context by accident.
- **Registration order must not matter.** Providers register `IUserContext` and `ITenantProvider` with `TryAdd`,
  so a worker host's `SystemUserContext` or a service's own implementation wins. The one exception: an
  `AnonymousUserContext` **instance** descriptor is a placeholder (the persistence builder registers one) and every
  provider removes it first. Register placeholders only that way, with the non-generic
  `ServiceDescriptor.Singleton(typeof(IUserContext), AnonymousUserContext.Instance)` so `00.Governance`'s
  no-singleton-security-context rule stays meaningful. `13.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` (the `IRequestContext` `06.Persistence` uses
  since P-558) TryAdds the same `UserContextTenantProvider`, for the same reason.
- **Inbound claim renaming is off.** `JwtBearerOptions.MapInboundClaims` defaults to `true` in .NET 10 and turns
  `sub`, `roles`, `email`, `amr`, `scp`, `tid` into long URIs. `ConfigureOidcJwtBearerOptions.PostConfigure` forces
  it off; everything in this domain and `SecurityClaimTypes` assumes the short names. The regression test drives a
  real signed token through the handler — keep it.
- **Security checks live in the handler, not in events.** `OidcJwtBearerHandler` (swapped in through
  `AuthenticationOptions.SchemeMap["Bearer"].HandlerType`) runs the sender-constraint and revocation checks after
  `base.HandleAuthenticateAsync()` succeeds. Events can be replaced by the application or by `EventsType`; the
  handler wraps whatever events exist per request (`InitializeEventsAsync`) only to extract DPoP tokens and capture
  the validated token. `cnf` is read from that captured token, never from the principal, which an application's
  `OnTokenValidated` may rebuild. A check added to an event instead of the handler is bypassable.
- **Configure vs PostConfigure.** Settings go in `Configure` so an application can still adjust them (for example
  an `IssuerValidator` for multi-tenant Entra). What must not be weakened — `MapInboundClaims`, the algorithm
  allow-list, `ValidateIssuer/Audience/Lifetime`, `RequireSignedTokens`, `RequireExpirationTime`,
  `UseSecurityTokenValidators` — is pinned in `PostConfigure`. `ConfigureOidcJwtBearerOptions.Validate` re-checks the
  pinned values (plus `SignatureValidator` unset) after every `PostConfigure`, with `ValidateOnStart`, so an
  application `PostConfigure` registered later fails startup instead of silently weakening validation.
- **Collection options default to empty.** Configuration binding **appends** to a list, array or read-only list that
  already has items, so `ValidAlgorithms = ["RS256", …]` plus a configured `["PS256"]` becomes all four. Every
  collection option defaults to `[]`, the documented defaults live in `Internal/OidcDefaults`, and a configured
  value replaces them. Verified 2026-09-16; `13.ServiceDefaults`' `StrategyOrder` options were fixed the same way.
- **Certificate events cannot be replaced.** `CertificateAuthenticationHandler` is internal, so the validator runs
  from `MtlsCertificateEvents`, installed in `PostConfigure`. Options validation runs after every post-configuration,
  so `ConfigureCertificateOptions.Validate` fails startup when `EventsType` is set or `Events` is no longer
  `MtlsCertificateEvents` (an application `PostConfigure` registered later).
- **`IClaimsTransformation` is a single service.** ASP.NET Core resolves one. `AddTotpStepUp` removes the last
  unkeyed registration, re-registers it under a private key with its original lifetime, and resolves it as the
  inner transformation, so a singleton stays one instance and is disposed. Keyed registrations are left alone; the
  framework's `NoopClaimsTransformation` is dropped, not wrapped.
- **DPoP proofs have no `kid`.** IdentityModel ignores `IssuerSigningKey` for a token without a matching `kid` unless
  `TryAllIssuerSigningKeys` is true; the proof's own key is the only candidate. `typ` is checked case-insensitively by
  the validator itself, not through `ValidTypes` (ordinal).
- **Algorithm rejection logging** (12104) reads the rejected token's `alg` header: IdentityModel reports a disallowed
  algorithm as a signature failure without `SecurityTokenInvalidAlgorithmException`.

## Security invariants

- **Fail closed.** A throwing revocation check, DPoP replay cache (`ReplayCacheUnavailable`), API key store or
  certificate validator rejects the request (the certificate validator's exception is caught and logged as 12301; an
  API key store's exception propagates and fails the request). A validator rejection is final: an application
  `OnAuthenticationFailed` that calls `Success()` afterwards is overridden. `ConfigureCertificateOptions.Validate`
  also fails startup when trust settings on `CertificateAuthenticationOptions` no longer match
  `MtlsAuthenticationOptions`. `Dpop:Mode = Required` or `Dpop:RequireNonce` without `AddDpop` fails
  startup validation instead of silently accepting bearer tokens. A failing revocation *cache* is logged and bypassed (the check still runs); it never accepts.
- **Algorithms.** JWTs and DPoP proofs accept only `RS/PS/ES` 256–512. `none` and `HS*` fail startup validation.
  Defaults are RS256/PS256/ES256 because every mainstream provider signs with RS256; FAPI 2.0 services configure
  PS256/ES256.
- **Sender-constrained tokens** (RFC 9449 §7, RFC 8705 §3), enforced for every token, with or without `AddDpop`:
  a token with `cnf.jkt` needs `Authorization: DPoP` and a valid proof, so without `AddDpop` it is always rejected;
  an unbound token presented with the DPoP scheme is rejected; `Mode = Required` rejects unbound tokens; a token
  with `cnf.x5t#S256` needs the matching client certificate on the connection. `IsSenderConstrained` is read from
  `cnf` on an accepted token — there is no synthetic claim to forge.
- **DPoP proof checks**: single `DPoP` header, `typ dpop+jwt`, allow-listed alg, public `jwk` only (RSA ≥ 2048 or
  EC; no `d`/`p`/`q`/`dp`/`dq`/`qi`/`oth`/`k`), signature, `htm` ordinal, `htu` normalized without query/fragment,
  `iat` within `ProofLifetime ± ClockSkew`, `jti` ≤ 256 chars, `jkt` match, `ath` in fixed time, optional nonce, then
  replay insert keyed by `SHA-256(jkt.jti)`. Nonces are Data Protection-protected expiry + random bytes; every
  replica needs the shared key ring.
- **Revocation** receives the token (introspection needs it) but caches by `TokenHash` only. "Revoked" is cached
  until the token expires; "not revoked" for at most `NotRevokedCacheDuration` (the revocation delay).
- **A token must name a caller.** `OidcJwtBearerHandler` maps the validated identity first; a signed token with
  neither a subject nor a client id fails authentication (event 12100), so `HttpContext.User` is never authenticated
  while `IUserContext` is anonymous. Revocation requests take `SubjectId` from that mapped context, so a configured
  `SubjectClaimType` applies.
- **Empty tenant ids mean no tenant** in every mapper and in `ManagedApiKeyValidator` (`ApiKeyValidationResult.Success`
  itself rejects `Guid.Empty`).
- **API keys** are never read from the query string. A header holding only whitespace counts as absent, so the
  forwarding scheme falls back to the default scheme. Managed keys: `{prefix}_{16 base62 id}_{32 base62 secret}{6
  base62 CRC-32}`; only `SHA-256(key)` is stored (190-bit secret, so no slow hash is needed); the hash is compared
  in fixed time even for an unknown id; prefix, revocation and expiry are checked with `IClock`.
- **Certificates** default to ASP.NET Core's own posture (Chained, System trust, Online revocation) or stronger;
  a private CA uses `CustomRootTrust` + `CustomTrustStore`, never `AllowedCertificateTypes = All`.
- **TOTP step-up is per session.** Keyed by `(SubjectId, SessionId)`, so a code entered in one session never marks
  the user's other sessions or a stolen token as stepped up. Session id falls back `sid` → `jti` → `uti`; without a
  `sid` the step-up ends when the access token is refreshed. Replay protection and throttling stay keyed by subject.
  A step-up never changes `AuthTime`. `IsAuthenticationFresherThan` rejects an `AuthTime` more than
  `UserContext.MaxFutureAuthTime` (5 minutes) ahead of `now`; `UserContext` copies collections set through `init`.
  The transformation stamps the step-up's `verifiedAt` (whole seconds, rounded down) as `amr_time` next to `amr=otp`,
  and when the credential already carries the method it stamps only a step-up newer than what the identity reports
  (latest `amr_time`, else `AuthTime`) — so the store is read on every request of a user with a session, `otp` in
  the token or not. On HTTP the method disappears after `FreshnessWindow`; on a SignalR connection it does not, and
  only a requirement with a maximum age on the hub method ends it.
- **Recovery codes** are hashed with `IOneWayHasher` and stored with a two-character lookup so redemption verifies
  only matching hashes; `TryMarkUsedAsync` must be atomic.
- **Tenant trust boundary.** A signed `tenant_id` proves the provider issued that value, not that the subject
  belongs to the tenant. With one issuer serving several tenants, verify membership before high-impact actions.
- **Never log** a token, proof, key, certificate, code, secret or claim value. Key ids and certificate thumbprints
  are not secret and are logged for audit. `ToString()` of `GeneratedApiKey`, `TotpEnrollment` and
  `TokenRevocationRequest` omits secrets — keep it that way for any new type holding one.

## Logging (EventId 12000–12999)

| Range | Package | Events |
| --- | --- | --- |
| 12000–12099 | Abstractions | none (no logging dependency) |
| 12100–12199 | Oidc | 12100 subject missing · 12101 tenant claim invalid · 12102 DPoP rejected (reason) · 12103 revocation rejected (check available) · 12104 algorithm rejected · 12105 certificate binding rejected · 12106 revocation cache failed |
| 12200–12299 | ApiKey | 12200 key rejected (reason, key id) · 12201 ambiguous header |
| 12300–12399 | Mtls | 12300 certificate rejected (reason, thumbprint), 12301 validator failed (exception, thumbprint) |
| 12400–12499 | Totp | 12400 challenge not accepted (result, operation) · 12401 step-up recorded · 12402 recovery code redeemed (remaining) |

## Decisions

| Decision | Why |
| --- | --- |
| Removed `AddAzureB2CAuthentication` and Microsoft.Identity.Web | Generic OIDC covers Entra ID, B2C and External ID through `Authority`; the dependency pulled Data Protection 9.0 with eight advisories (a pinned override), and B2C is closed to new customers |
| No multi-issuer support | Not requested for this pass. A service needing several issuers registers a second JWT scheme itself |
| `IUserContextMapper` instead of wrapping earlier `IUserContext` factories | The old descriptor-capture chain was order dependent and dropped any type- or instance-registered context |
| Managed API keys hash with SHA-256, not a password hash | The secret carries 190 bits of entropy; a slow hash adds latency to every request and no security |
| DPoP nonces via Data Protection, not a new key setting | Services already need a shared key ring for cookies and antiforgery; no second secret to manage |
| Mtls no longer references `SharedKernel.Cryptography` | Its only use was the certificate-binding check, which moved to Oidc where the token is validated |

## Cross-Domain Couplings

- `06.Persistence` audit columns store `SubjectId` (≤ 255 chars for OIDC; columns are 256) or the service name.
- `13.ServiceDefaults`: `SharedKernel.MultiTenancy`'s claim strategy resolves the tenant through the registered mappers
  (no Oidc reference); `ServiceDefaults.Security.Mtls` calls `IMtlsCertificateValidator` during the TLS handshake
  (synchronously — a known limitation there) and forwards certificates, which Oidc's RFC 8705 check reads.
- `14.Presentation`'s requirements (native authorization policies since P-562) resolve an `IUserContext` from the
  principal being authorized through the registered mappers (`UserContextResolver.Resolve`), never the scoped
  registration, and read `HasRole`, `HasPermission`, `WasAuthenticatedWith`, `IsAuthenticationFresherThan` and, for a
  method with a maximum age, `GetAuthenticationMethodTime` bounded by `UserContext.MaxFutureAuthTime`. A signed-in
  principal no mapper understands is refused (403, logged); a scheme without a mapper is named in a startup warning.
- `16.Testing`: `FakeUserContext`, `SecurityTestContextBuilder`, `DpopTestProofBuilder`, `InMemoryApiKeyStore`,
  `InMemoryDpopReplayCache`, `InMemoryTotpStepUpStore`, `InMemoryRecoveryCodeStore`.
- `00.Governance`: DPoP parsing only in `SharedKernel.Security.Oidc`; `ConnectionInfo.ClientCertificate` getter only
  in `SharedKernel.Security.Mtls` (Oidc uses `GetClientCertificateAsync`); secure-default tests on
  `MtlsAuthenticationOptions` and the configured `JwtBearerOptions`.

## Test Rules

- Authentication behaviour is tested end to end through `TestServer` with real signed tokens, proofs and
  certificates; unit tests cover pure logic. A test that builds a `ClaimsPrincipal` by hand cannot catch claim
  renaming or handler wiring — that is how the `MapInboundClaims` defect shipped unnoticed before.
- No network: post-configure `JwtBearerOptions.Configuration` with an `OpenIdConnectConfiguration` holding the
  test signing keys.
- Time through `FakeClock` as `IClock`; never `Task.Delay`.
- Security-critical tests must be able to fail: mutate the condition mentally and confirm the assertion catches it.

## Changelog

> Entries before 2026-09-16 are in [`CLAUDE.history.md`](CLAUDE.history.md).

- [2026-09-16] **Root P-546 — pre-publish redesign.** Breaking rewrite of all five packages before their first
  publish: string `SubjectId`, `IUserContextMapper` composition, inbound claim renaming disabled, handler-enforced
  DPoP (RFC 9449) and certificate binding (RFC 8705), revocation keyed by token hash, Azure B2C and
  Microsoft.Identity.Web removed, managed API keys, certificate trust options, session-bound TOTP step-up with
  enrollment confirmation and recovery-code redemption, public API tracking.
- [2026-09-17] P-546 README pass: five package READMEs and the domain overview rewritten; defects found while
  documenting fixed — later `PostConfigure` could weaken pinned JWT settings (now fails startup), subjectless tokens
  authenticated as anonymous callers (now 401), revocation `SubjectId` ignored `SubjectClaimType`, managed keys with
  `TenantId = Guid.Empty` threw per request, whitespace `X-Api-Key` blocked the bearer fallback, API key and mTLS
  mappers accepted `Guid.Empty` tenants; the unused `SharedKernel.Primitives` reference was removed from
  `.Abstractions` (`.Oidc` now references it directly). mTLS: a throwing validator now rejects (new event 12301)
  instead of a 500, an app `OnAuthenticationFailed` can no longer overturn a validator rejection, and trust settings
  changed on `CertificateAuthenticationOptions` after registration fail startup.
- [2026-09-17] Published all five packages as `1.0.0-alpha.0.1026` from `3471936`. Before publishing, CI on Linux
  caught that `/issuer` parses as an absolute `file://` URI there; the authority must now be an absolute http or https
  URL. Publishing a package whose SharedKernel dependencies were last published at a lower commit height needs
  those dependencies republished from the same commit first (the workflow's feed dependency gate enforces it).
- [2026-09-24] **P-562 X1 (security review S3) — step-up expires on long-lived connections.** A SignalR connection
  kept `amr=otp` from connect time, so `[RequireAuthenticationMethod("otp")]` passed long after the step-up window.
  Additive API: `SecurityClaimTypes.AuthenticationMethodTime` (`amr_time`), `AuthenticationMethodTimeClaim`
  (`Create`/`Read`), `IUserContext.GetAuthenticationMethodTime` (default-implemented), `UserContext.AuthenticationMethodTimes`
  and the member on the anonymous/system contexts. Every mapper maps the times (ApiKey/Mtls now also map `amr`);
  `TotpStepUpClaimsTransformation` stamps the verification time and, when `otp` is already present, dates it with a
  newer step-up instead of returning early. `14.Presentation.WebApi` gained `RequireAuthenticationMethodAttribute.MaxAgeSeconds`
  and a `RequireAuthenticationMethod(TimeSpan, …)` convention. All five packages need a republish (Abstractions,
  Oidc, ApiKey, Mtls, Totp).
