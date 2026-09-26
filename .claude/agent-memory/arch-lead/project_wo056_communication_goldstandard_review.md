---
name: project_wo056_communication_goldstandard_review
description: WO-056 11.Communication gold-standard review — P-356-365; validator-dead-code bug class, dead config knobs, missing gRPC OTel instrumentation, first-ever NuGet publish gap, idempotency-key feature
type: project
---

> WO-086 (2026-09): `CorrelationIdDelegatingHandler`/`TenantIdDelegatingHandler` became `RequestContextDelegatingHandler`; `IdempotencyHeaders`/`x-idempotency-key` became `WellKnownHeaders.IdempotencyKey` (`Idempotency-Key`); `SharedKernel.Communication.GraphQL` moved to `SharedKernel.Presentation.GraphQL`.

WO-056 (2026-08-11): 11.Communication gold-standard architecture review, triggered by direct user
request ("this package must be on every single project... must be gold standard and developer
friendly"). Dispatched ten phases (P-356–P-365) across 11.Communication (9) / 13.ServiceDefaults (1).

**Why this matters going forward:** this domain is the platform's most universally-consumed
package, so defects here have the widest blast radius of any domain reviewed so far.

**Key findings (verified against shipped .cs source via a dedicated research agent, not
CLAUDE.md prose):**
- Two defects flagged during the WO-041 P-255 logging retrofit as "candidate follow-up, not
  fixed" sat undispatched for weeks until this review turned them into real phases (P-356
  GUID-format fix, P-357 IClock injection). **Lesson: a "flagged as follow-up" note in a
  changelog is not the same as a queued phase — it needs to actually become one, or it rots.**
- The GUID-fallback defect (Guid.ToString("N")) was independently duplicated in `.Rest`'s
  `CorrelationIdDelegatingHandler`, never caught because the prior retrofit's
  temporary-analyzer-verification technique (borrow a governance analyzer via a temporary
  ProjectReference, check for zero diagnostics, then revert) was only ever wired into the two
  packages that had logging (`.Grpc`/`.Internal`), never into `.Rest`/`.GraphQL`. **Lesson: a
  spot-check technique scoped to "the packages that need it for reason X" can miss the same
  class of defect in siblings that don't share reason X.**
- Found a new bug *pattern* not previously seen in prior domain reviews: an `IValidateOptions<T>`
  validator that is correctly registered in DI but structurally unreachable because the options
  object it's meant to validate is constructed and consumed entirely outside the ASP.NET Core
  Options pipeline (built via `new Options(); configure?.Invoke(options);` rather than
  `AddOptions<T>()`/`Configure<T>()` + `IOptions<T>.Value` resolution). Found this exact pattern
  twice independently (`RestClientOptionsValidator`, `GraphQLOptionsValidator`). **This is a
  distinct defect class from the "dead config knob" pattern (property documented but never
  read) already seen in 07.Messaging/WO-054 — worth checking for in any domain with a validator
  class going forward.**
- `GrpcClientOptions.DeadlineSeconds` and the `OpenTelemetry.Instrumentation.GrpcNetClient`
  PackageReference are both "dead config knob" instances (documented, defaulted, never
  consulted) — same class as WO-054's `AzureServiceBusOptions.MaxConcurrentCalls`.
- The domain has never been packed/published to NuGet at all despite having complete package
  identity metadata in all four `.csproj` files — a genuinely critical gap given the user's
  stated premise ("must be on every project"). Gated the packaging phase (P-363) behind all the
  correctness-fix phases so the first-ever release ships already fixed.
- Accepted one net-new feature (not just bug fixes): opt-in idempotency-key propagation for
  outbound REST calls (P-364), reasoned as closing the platform's existing idempotency story
  (05.Application's IIdempotentRequest, 07.Messaging's IIdempotencyStore) at the outbound-HTTP
  layer, motivated by `StandardResilienceHandler`'s default retry behavior already creating a
  real duplicate-side-effect hazard.
- Declined: mTLS/client-cert config surface (K8s service-mesh sidecars already own this in most
  deployments; no concrete consumer need identified) — same reasoning pattern as declining
  probe-everything in prior WOs.
- Declined: a new platform-wide governance analyzer generalizing "documented options property
  never consulted anywhere" — not reliably mechanizable via static analysis; both known
  instances (this WO + WO-054) were fixed directly instead.

**Process note:** Domain Summary Board rows for both 11.Communication and 13.ServiceDefaults
were already `●` Published, so no `state-map-phase` calls were made — only `sync-brain` for two
new "What Goes Where" rows (idempotency-key propagation, `WithCommunicationTelemetry`) and Folder
Map annotations on rows 11/13.

See also [[project_phase_numbering]] for the running P-NNN/WO-NNN counter (this WO used
P-356–P-365, WO-056; next session starts at P-366/WO-057).
