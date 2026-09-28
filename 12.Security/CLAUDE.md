# 12.Security — Domain Brain

> Who is calling, and how sure we are. This domain turns an ASP.NET Core authentication result (OIDC bearer token,
> API key, client certificate) into `IUserContext`, and adds session-bound TOTP step-up. It does **not** own the
> execution context every other layer reads — that is `IRequestContext` in `01.Core`'s `SharedKernel.Execution`,
> built over `IUserContext` by `13.ServiceDefaults`' `AddSharedKernelRequestContext()`. It does not own endpoint
> authorization attributes (`14.Presentation`'s `SharedKernel.Presentation.Core`), tenant resolution middleware
> (`13.ServiceDefaults`' `SharedKernel.MultiTenancy`) or cryptographic primitives (`01.Core`'s `SharedKernel.Cryptography`).
> Consumers read each package's `README.md`; this file holds the rules, traps and couplings the source does not make obvious.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Security.Abstractions` | Abstractions | `IUserContext`, `UserContext`, `AnonymousUserContext`, `SystemUserContext`, `IUserContextMapper`, `UserContextResolver`, `SecurityClaimTypes`, `AuthenticationMethodTimeClaim`. References only `SharedKernel.Execution` (`ActorKind`, `TenantId`); no ASP.NET Core |
| `SharedKernel.Security.Oidc` | Host | JWT bearer for any OIDC provider; algorithm allow-list; DPoP (RFC 9449); certificate-bound tokens (RFC 8705); token revocation |
| `SharedKernel.Security.ApiKey` | Host | Managed API keys (format, generator, hashed store, validator) or a custom validator; `ApiKeyOrDefault` forwarding scheme |
| `SharedKernel.Security.Mtls` | Host | Client-certificate authentication through an `IMtlsCertificateValidator`; private-CA trust |
| `SharedKernel.Security.Totp` | Host | TOTP enrollment, challenge and recovery-code redemption; session-bound step-up claims transformation |

Provider packages never reference each other. Every package tracks its public API (`PublicAPI.*.txt`, RS0016/RS0017 as errors).

## Public Entry Points

- **Abstractions** — inject `IUserContext` only at the HTTP edge; `UserContextResolver.Resolve` maps a principal through
  the registered `IUserContextMapper`s. Placeholders: `AnonymousUserContext.Instance`, `SystemUserContext` for worker hosts.
- **Oidc** — `services.AddOidcAuthentication(configuration)` binds `OidcAuthenticationOptions`
  (`SharedKernel:Security:Oidc`: `Authority`, `Audiences`, `ValidIssuers`, `ValidAlgorithms`, `ValidTokenTypes`,
  `ClockSkew`, `Claims` (`OidcClaimOptions`, incl. `TenantClaimType`, `PermissionClaimTypes`, `ApplicationTokenClaims`),
  `Dpop` (`DpopOptions`: `Mode`, `RequireNonce`, `ProofLifetime`), `Revocation` (`NotRevokedCacheDuration`)). Returns
  `OidcAuthenticationBuilder`: `.AddDpop<TReplayCache>()` (`IDpopReplayCache`), `.AddTokenRevocation<TCheck>()`
  (`ITokenRevocationCheck`), `.AddTokenRevocationCache<TCache>()` (`ITokenRevocationCache`). Scheme `Bearer`
  (`OidcAuthenticationDefaults`).
- **ApiKey** — `AddManagedApiKeyAuthentication<TStore>(k => k.Prefix = "…")` (`IApiKeyStore`, `ApiKeyGenerator`,
  `ManagedApiKeyOptions`) or `AddApiKeyAuthentication<TValidator>()` (`IApiKeyValidator`). Scheme `ApiKey`, forwarding
  scheme `ApiKeyOrDefault` becomes the default, header `X-Api-Key` (`ApiKeyAuthenticationDefaults`).
- **Mtls** — `AddMtlsAuthentication<TValidator>(o => …)` (`IMtlsCertificateValidator`, `MtlsAuthenticationOptions`).
  Scheme `Certificate`; never made the default — select it per endpoint.
- **Totp** — `AddSharedKernelCryptography(configuration).AddTotpStepUp<TStepUpStore, TRecoveryCodeStore>(o => …)`
  (`ITotpStepUpStore`, `IRecoveryCodeStore`, `TotpStepUpOptions.FreshnessWindow` 1 min–24 h); then
  `TotpEnrollmentService`, `TotpChallengeService`, `TotpStepUpClaimsTransformation`. The consumer also registers an
  `ITotpReplayGuard` (shared by every replica) and optionally an `ITotpAttemptThrottle`.

## Rules & Invariants

1. `SubjectId` is a **string**, non-null exactly for `ActorKind.User` and `ActorKind.Service`; `UserContext`'s constructor enforces it — keep it enforced in any new implementation.
2. `IsAuthenticated` is `true` for every `ActorKind` except `Anonymous`; no implementation (fakes included) may let them disagree. `default(ActorKind)` is `User` — represent anonymous with `AnonymousUserContext.Instance`, never a default value.
3. `HasRole`, `HasPermission`, `WasAuthenticatedWith` compare **ordinally** (OAuth scopes are case-sensitive).
4. `TenantId` is `TenantId?` from the credential; a missing, malformed or empty-GUID tenant claim maps to `null` (fail closed downstream). Never add a second tenant abstraction here — the tenant travels on the caller.
5. **One mapper per scheme.** Each provider registers an `IUserContextMapper` (`TryAddEnumerable`) whose `AuthenticationType` equals its scheme name, and the identities it issues carry that authentication type. An identity from a scheme with no mapper resolves to **anonymous**.
6. **Registration order must not matter.** Providers register `IUserContext` with `TryAdd`. The only exception: an `AnonymousUserContext` **instance** descriptor is a placeholder every provider removes first — register placeholders only as `ServiceDescriptor.Singleton(typeof(IUserContext), AnonymousUserContext.Instance)`.
7. **Inbound claim renaming stays off.** `ConfigureOidcJwtBearerOptions` forces `MapInboundClaims = false` in `PostConfigure`; everything, including `SecurityClaimTypes`, assumes short names (`sub`, `roles`, `amr`, `tenant_id`).
8. **Security checks live in the handler, not in events.** `OidcJwtBearerHandler` runs sender-constraint and revocation checks after `base.HandleAuthenticateAsync()`; `cnf` is read from the captured validated token, never from the principal. A check added to a `JwtBearerEvents` callback is bypassable.
9. **Configure vs PostConfigure.** Adjustable settings go in `Configure`. `MapInboundClaims`, the algorithm allow-list, `ValidateIssuer/Audience/Lifetime`, `RequireSignedTokens`, `RequireExpirationTime`, `UseSecurityTokenValidators` are pinned in `PostConfigure` and re-checked by validation with `ValidateOnStart`, so a later application `PostConfigure` fails startup instead of weakening validation.
10. **Collection options default to `[]`.** Configuration binding appends to a non-empty list; documented defaults live in `Internal/OidcDefaults` and a configured value replaces them.
11. **Algorithms**: only RS/PS/ES 256–512 for JWTs and DPoP proofs; `none` and `HS*` fail startup. Defaults RS256/PS256/ES256.
12. **Sender-constrained tokens** are enforced with or without `AddDpop`: a `cnf.jkt` token needs `Authorization: DPoP` and a valid proof; an unbound token under the DPoP scheme is rejected; `DpopMode.Required` rejects unbound tokens; `cnf.x5t#S256` needs the matching client certificate. `Dpop:Mode = Required` or `Dpop:RequireNonce` without `AddDpop` fails startup.
13. **DPoP proofs have no `kid`**: validation sets `TryAllIssuerSigningKeys`; `typ` is checked case-insensitively by the validator itself. Replay entries are keyed by `SHA-256(jkt.jti)`; nonces are Data Protection-protected, so every replica needs the shared key ring.
14. **A token must name a caller.** A signed token with neither subject nor client id fails authentication (12100), so `HttpContext.User` is never authenticated while `IUserContext` is anonymous.
15. **Fail closed.** A throwing revocation check, DPoP replay cache, API key store or certificate validator rejects the request. A validator rejection is final — an application `OnAuthenticationFailed` calling `Success()` is overridden. A failing revocation *cache* is logged (12106) and bypassed; the check still runs.
16. **Revocation** receives the token but caches by `TokenHash` only; "revoked" until expiry, "not revoked" at most `NotRevokedCacheDuration`.
17. **Certificate events cannot be replaced** (`CertificateAuthenticationHandler` is internal): `MtlsCertificateEvents` is installed in `PostConfigure`, and `ConfigureCertificateOptions` fails startup when `EventsType` is set, `Events` was swapped, or trust settings diverge from `MtlsAuthenticationOptions`. A private CA uses `CustomRootTrust` + `CustomTrustStore`, never `AllowedCertificateTypes = All`.
18. **API keys** are header-only (never the query string); a whitespace-only header counts as absent so the forwarding scheme falls back. Managed key format `{prefix}_{16 base62 id}_{32 base62 secret}{6 base62 CRC-32}`; only `SHA-256(key)` is stored and compared in fixed time even for an unknown id; prefix, revocation and expiry checked with `IClock`.
19. **`IClaimsTransformation` is a single service.** `AddTotpStepUp` re-registers the existing unkeyed transformation under a private key (same lifetime) and wraps it; `NoopClaimsTransformation` is dropped, not wrapped.
20. **TOTP step-up is per session**, keyed by `(SubjectId, SessionId)` (session id falls back `sid` → `jti` → `uti`). Replay protection and throttling stay keyed by subject. A step-up never changes `AuthTime`; `IsAuthenticationFresherThan` rejects an `AuthTime` more than `UserContext.MaxFutureAuthTime` ahead.
21. **Method times.** `GetAuthenticationMethodTime(method)` is non-null only when `WasAuthenticatedWith(method)`: the latest `amr_time` (`SecurityClaimTypes.AuthenticationMethodTime`, written/read only through `AuthenticationMethodTimeClaim`), else `AuthTime`. Every mapper sets `UserContext.AuthenticationMethodTimes`. Code that adds an `amr` value after sign-in must add its `amr_time`.
22. **Recovery codes** are hashed with `IOneWayHasher` with a two-character lookup; `IRecoveryCodeStore.TryMarkUsedAsync` must be atomic.
23. **Never log** a token, proof, key, certificate, code, secret or claim value (key ids and thumbprints are fine). `ToString()` of `GeneratedApiKey`, `TotpEnrollment` and `TokenRevocationRequest` omits secrets — keep it so for any new secret-holding type.
24. The Oidc authority must be an absolute http/https URL (`/issuer` parses as a `file://` URI on Linux).

## Decisions

| Decision | Why |
| --- | --- |
| Generic OIDC only; no Azure B2C / Microsoft.Identity.Web helper | `Authority` covers Entra ID, B2C and External ID; the dependency dragged a vulnerable Data Protection version |
| No multi-issuer support | Not requested; a service needing several issuers registers a second JWT scheme itself |
| `IUserContextMapper` per scheme instead of chained `IUserContext` factories | A descriptor-capture chain is order dependent and drops type- or instance-registered contexts |
| Managed API keys hash with SHA-256, not a password hash | 190-bit secret; a slow hash adds per-request latency and no security |
| DPoP nonces via Data Protection | Services already share a key ring; no second secret to manage |
| Certificate-binding (RFC 8705) check lives in Oidc, not Mtls | It is a property of the token being validated; Mtls needs no Cryptography reference |
| `ActorKind` shared with `IRequestContext` | No mapping layer between the two execution views |
| `GetAuthenticationMethodTime` default-implemented (`null`) | Older implementations still compile and fail closed under a maximum age |

## Logging

Block **12000–12999** (`LoggingEventIdRanges`), 100-wide sub-blocks:

| Range | Package | Events |
| --- | --- | --- |
| 12000–12099 | Abstractions | none (no logging dependency) |
| 12100–12199 | Oidc | 12100 subject missing · 12101 tenant claim invalid · 12102 DPoP rejected · 12103 revocation rejected · 12104 algorithm rejected (reads the token's `alg` header) · 12105 certificate binding rejected · 12106 revocation cache failed |
| 12200–12299 | ApiKey | 12200 key rejected (reason, key id) · 12201 ambiguous header |
| 12300–12399 | Mtls | 12300 certificate rejected (reason, thumbprint) · 12301 validator threw |
| 12400–12499 | Totp | 12400 challenge not accepted · 12401 step-up recorded · 12402 recovery code redeemed |

## Cross-Domain Couplings

- **01.Core** — `SharedKernel.Execution` (`ActorKind`, `TenantId`); `SharedKernel.Cryptography` (ApiKey random/hash, Totp `ITotpVerifier`, `IOneWayHasher`); `SharedKernel.Configuration` (`ISectionBoundOptions`); `IClock`.
- **13.ServiceDefaults** — `ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` builds `IRequestContext` from `IUserContext` (`UserId = SubjectId ?? ClientId`, same `ActorKind` and `TenantId?`). `SharedKernel.MultiTenancy`'s claim strategy resolves the tenant through the registered mappers (no Oidc reference). `ServiceDefaults.Security.Mtls` calls `IMtlsCertificateValidator` during the TLS handshake and forwards certificates that Oidc's RFC 8705 check reads.
- **14.Presentation** — `SharedKernel.Presentation.Core`'s `[RequireRole]`, `[RequireEndpointPermission]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod(…, MaxAgeSeconds = n)]` resolve an `IUserContext` from the principal being authorized via `UserContextResolver.Resolve` (not the scoped registration). On a SignalR connection, only a hub-method requirement with a maximum age ends a step-up; a gRPC stream is authorized once and checks `GetAuthenticationMethodTime` itself.
- **06.Persistence** — audit columns store `SubjectId` (256 chars) or the service name.
- **00.Governance** — DPoP parsing only in `SharedKernel.Security.Oidc`; `ConnectionInfo.ClientCertificate` getter only in `SharedKernel.Security.Mtls`; secure-default tests on `MtlsAuthenticationOptions` and the configured `JwtBearerOptions`; no singleton security context.

## Testing

- Each package has a nested `*.Tests` project, all in the **Unit** lane (`Platform.SharedKernel.Unit.slnf`).
- Authentication is tested end to end through `TestServer` with real signed tokens, DPoP proofs and certificates; a hand-built `ClaimsPrincipal` cannot catch claim renaming or handler wiring. No network: post-configure `JwtBearerOptions.Configuration` with an `OpenIdConnectConfiguration` holding the test keys.
- Time through `FakeClock` as `IClock`; never `Task.Delay`. Security tests must be able to fail — mutate the condition and confirm the assertion catches it.
- Consumer fakes (`16.Testing/SharedKernel.Security.Testing`): `FakeUserContext`, `SecurityTestContextBuilder`, `DpopTestProofBuilder`, `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, `InMemoryTotpStepUpStore`, `InMemoryRecoveryCodeStore`.

## Known Limitations

- A signed `tenant_id` proves the provider issued the value, not that the subject belongs to the tenant; with one issuer serving several tenants, verify membership before high-impact actions.
- Without a `sid` claim a step-up ends when the access token is refreshed (session id falls back to the token id).
- A step-up (`amr=otp`) on a long-lived SignalR connection lasts until a hub-method requirement with a maximum age rejects it; on HTTP it disappears after `FreshnessWindow`.
- The mTLS validator runs synchronously inside the TLS handshake when `ServiceDefaults.Security.Mtls` is used.
- Single issuer per scheme (see Decisions).
