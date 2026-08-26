---
name: project_wo069_totp_step_up
description: WO-069/P-452 design — SharedKernel.Security.Totp's IClaimsTransformation-based step-up mechanism and its two scope-boundary invariants
type: project
---

WO-069 (2026-08-26) dispatched a fifth `12.Security` sibling provider package, `SharedKernel.Security.Totp`
(P-452, depends on `01.Core`'s P-451 TOTP/HOTP primitive inside `SharedKernel.Cryptography`, itself
design-locked/not yet Core-implemented at dispatch time). Full task breakdown is in
`12.Security/state-map.md` (D-45–D-57, SC-31–SC-37, C-49–C-57, T-40–T-48, DOC-20–DOC-24, PUB-20–PUB-23)
and `12.Security/CLAUDE.md`'s new `SharedKernel.Security.Totp` interface-contract section and Domain
Invariants subsection ("TOTP step-up scope boundary and AuthTime non-interaction").

**The central design problem solved**: a TOTP challenge verified by `SharedKernel.Security.Totp` itself
(not the primary IdP) must become observable through `IUserContext.WasAuthenticatedWith`/
`AuthenticationMethods` with zero changes to `.Oidc` or `14.Presentation`, while `.Totp` cannot reference
`.Oidc` (sibling-packages-never-reference-each-other rule). Resolved via `TotpStepUpClaimsTransformation :
Microsoft.AspNetCore.Authentication.IClaimsTransformation` — a framework-provided, scheme-agnostic hook
that runs during `AuthenticateAsync`, BEFORE any `IUserContext` DI factory first resolves for the request.
It consults a consumer-supplied `ITotpChallengeStore` seam (never dictates storage, mirrors
`IDpopProofReplayCache`/`ITokenRevocationCheck`) and, on a fresh hit, stamps a synthetic `amr`-shaped claim
onto the principal. `.Oidc`'s EXISTING defensive amr-claim reader (`OidcUserContext`, shipped WO-058/P-375)
then picks it up automatically — reusing the domain's already-sanctioned "stamp a synthetic marker claim,
read it generically downstream" mechanism (first used for DPoP's `IsSenderConstrained`, WO-058), just via
a framework hook instead of `JwtBearerEvents.OnTokenValidated` (which only `.Oidc` may register).

**Why:** `IClaimsTransformation` was chosen over (a) decorating `IUserContext` directly (blocked by the
sync-property/async-store-lookup mismatch — `IUserContext.AuthenticationMethods` is a synchronous property,
`ITotpChallengeStore` lookups are inherently async) and (b) hand-rolled middleware + `HttpContext.Items`
(would invent a second parallel side-channel, which an existing Implementation Rule from WO-058 explicitly
forbids). `IClaimsTransformation` sidesteps the sync/async problem entirely since it's natively async and
runs before any `IUserContext` factory needs the result.

Two Domain Invariants were locked as a result and must not be casually widened in a future phase without
re-deriving why:
1. TOTP step-up composes ONLY with `.Oidc`'s `OidcUserContext` — `.ApiKey`/`.Mtls` (`ApiKeyUserContext`/
   `MtlsUserContext`) hardcode `AuthenticationMethods` empty by design and always carry `UserId = Guid.Empty`,
   so a step-up lookup for either never fires (the `Guid.Empty` guard skips it structurally, no explicit
   `IdentityKind` special-casing needed). Intentional: those are machine-credential `ServicePrincipal`
   identities, not the audience for a human second factor.
2. A TOTP step-up NEVER touches `AuthTime`/`IsAuthenticationFresherThan` — only `AuthenticationMethods`/
   `WasAuthenticatedWith`. `[RequireFreshAuthentication]` stays keyed on primary-auth recency only;
   `[RequireAuthenticationMethod("otp")]`'s own freshness is enforced entirely by
   `TotpStepUpOptions.ChallengeFreshnessWindow` at claim-stamping time. Keeps two recency concepts from
   conflating onto one `AuthTime` axis.

**How to apply:** when reviewing or implementing `SharedKernel.Security.Totp`'s Core phase, verify the
`IClaimsTransformation` approach was actually used (not silently swapped for a decorator or middleware) and
that both invariants above still hold in the shipped code — this is exactly the kind of design decision this
domain has a track record of drifting from during implementation without a source-verification pass (see the
WO-058→WO-060 pattern: real gaps get found only when someone re-reads the shipped `.cs` against the design).
Also verify at Scaffold/Core time whether `IClaimsTransformation` needs a separate NuGet `PackageReference`
or is reachable purely via the `Microsoft.AspNetCore.App` `FrameworkReference` — do NOT assume; the `.Mtls`/
`Microsoft.AspNetCore.Authentication.Certificate` precedent (WO-058) found the "framework-provided" assumption
was wrong for that package.
