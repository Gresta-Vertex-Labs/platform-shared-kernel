---
name: wo062-presentation-goldstandard-review
description: WO-062 — 14.Presentation gold-standard/big-fintech review; confirmed ValidationException.Errors[0]-only truncation bug plus eight hardening phases
metadata:
  type: project
---

**Trigger:** direct user request to analyze `14.Presentation/` packages against big-fintech gold-standard and developer-friendliness, and to look specifically at what's missing in the security part.

**Verdict: UPGRADE.** The domain was fully `●` Published (all of WO-031/041/042/058/059 shipped, 71/71 `SharedKernel.Presentation.WebApi.Tests` + 11/11 `SharedKernel.Presentation.SignalR.Tests` green) — not a rubber-stamp accept, since source reading turned up a real, previously-invisible defect plus eight genuine gaps against fintech-grade expectations.

**Confirmed defect (P-402), verified by reading real `.cs` files, not trusting "Published" status:**
`01.Core/SharedKernel.Core/Exceptions/ValidationException.cs`'s `ValidationException(IReadOnlyList<Error> errors)` already exposes every failing field's `Error` via its public `.Errors` property. But `14.Presentation/SharedKernel.Presentation.WebApi/ExceptionHandling/SharedKernelExceptionHandler.cs`'s `ResolveError` only ever reads `sharedKernelException.Error` — the base `SharedKernelException.Error` property, which `ValidationException`'s own constructor sets via `BuildPrimaryError` to `errors[0]`, the FIRST failing field only. A request failing validation on 3 fields today returns a `ProblemDetails` body naming exactly 1 of them; the other 2 are silently discarded even though `05.Application`'s `ValidationBehavior` already aggregated all of them correctly before throwing. This is the same class of "checked-box-but-not-actually-true" defect this agent has found before (see [[project_wo039_application_review]]'s P-232 finding) — read the real exception-handling source, don't trust that a domain being "Published" means every downstream consumer of an upstream capability was wired correctly.

**Eight further hardening phases dispatched (P-403–P-410), targeting real fintech-API gaps confirmed absent by reading every file in `Middleware/`/`Errors/`/`Authorization/`/`Extensions/` across both packages:**
- P-403 — HTTP security response headers (HSTS/X-Content-Type-Options/X-Frame-Options/Referrer-Policy/Permissions-Policy, opt-in CSP) — zero exist today.
- P-404 — deny-by-default CORS with a startup-time guard structurally forbidding `AllowAnyOrigin()`+`AllowCredentials()` — zero CORS convention exists today.
- P-405 — inbound `Idempotency-Key` HTTP-boundary support (`[RequireIdempotencyKey]` + `TryGetIdempotencyKey`), mirroring `[RequireRole]`/`AuthorizationRequirementEndpointFilter`'s exact proven shape — closes the gap between `05.Application`'s in-process `IIdempotentRequest` and `11.Communication.Rest`'s outbound propagation (P-364), neither of which owns the inbound HTTP header.
- P-406 — declarative step-up/fresh-authentication attributes (`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]`) consuming `12.Security`'s already-shipped `IUserContext.AuthTime`/`.AuthenticationMethods` (P-375/WO-058) — that identity-layer investment has had zero HTTP-boundary declarative consumer until now.
- P-407 — `ETag`/`If-Match`/412 conditional-request helpers, additive to `06.Persistence`'s `Error.Conflict`/409 row-version concurrency.
- P-408 — a real `RateLimitRejectionProblemDetails` helper closing the handoff `13.ServiceDefaults`'s `AddSharedKernelRateLimiting()` (P-397/WO-061) already deferred to this domain "without a hard reference" but nothing was ever built to receive.
- P-409 — conservative default SignalR `HubOptions` (MaximumReceiveMessageSize/MaximumParallelInvocationsPerClient/ClientTimeoutInterval) to close a resource-exhaustion gap on the hub connection surface.
- P-410 (00.Governance) — mechanical CORS wildcard+credentials guard mirroring the `SecureDefaultsAssertion`/`SecurityContextGuard` precedent (P-373/P-390/P-401), so P-404's startup guard can't silently regress later.

**Deliberately NOT added a `16.Testing` phase:** P-406's acceptance criteria explicitly require verifying `FakeUserContext`/`SecurityTestContextBuilder` (WO-057/WO-058) already expose settable `AuthTime`/`AuthenticationMethods` state before implementation — only escalate a `16.Testing` follow-up if a genuine gap is found, never assume one. Kept scope from ballooning into a 16.Testing phase that may not be needed.

**Domain board status:** both `14.Presentation` and `00.Governance` were `●` Published on the Domain Summary Board — no `state-map-phase` calls made, backlog-only, per the standard rule.

**`sync-brain` called (root mode):** Folder Map row 14 gained a forward-reference summarizing the queued pass; eight new "What Goes Where" rows added after the existing "Hand-rolled ProblemDetails construction" row; one changelog line appended.

See [[project_phase_numbering]] for the phase-ID bookkeeping this session updated (next available: P-411/WO-063).
