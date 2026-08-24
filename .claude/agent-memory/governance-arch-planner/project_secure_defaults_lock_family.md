---
name: secure_defaults_lock_family
description: The recurring "lock a documented-but-not-yet-mechanized default" phase family built on SecureDefaultsAssertion — when to add a new method vs. an executed test, and the near-universal "dependency already shipped by implementation time" finding.
type: project
---

Recurring phase family in 00.Governance/state-map.md: a security- or correctness-relevant default,
once designed in another domain, gets a mechanical lock here so a future refactor can't silently
regress it with that domain's own tests still green. All built on the single `SecureDefaultsAssertion`
class in `SharedKernel.ArchitectureTests` (first introduced WO-060/P-390). None of these phases mint a
new SK diagnostic ID — SK0033 has been "next available, unconsumed" across the entire family.

Occurrences so far (phase key — domain — WO/P):
1. SK.00.SecureDefaultsLock — 12.Security — WO-060/P-390 (enum-default + forbidden-string-collection)
2. SK.00.TenantAndMtlsBoundaryLock — 13.ServiceDefaults — WO-061/P-401 (ordered-string-collection + call-presence)
3. SK.00.CorsWildcardCredentialsGuard — 14.Presentation — WO-062/P-410 (new Roslyn SK0032 + throw-presence)
4. SK.00.CorrelationIdValidationGuard — 14.Presentation — WO-063/P-420 (zero new code, reused call-presence)
5. SK.00.WebhookSsrfGuardLock — 15.Integration — WO-064/P-432 (DI-registration-presence + 1st EXECUTED test)
6. SK.00.CacheEncryptionAndRedisValidationLock — 02.Caching — WO-065/P-437 (2nd EXECUTED test + 2nd zero-new-code)

**Technique-selection rule** (apply this before picking an approach for the next occurrence):
- If the fact is STRUCTURAL (a property's default value, a call site's presence/absence, a throw's
  presence, a DI registration's presence) → add/reuse a `SecureDefaultsAssertion` method, IL-only,
  never executes the assembly under test.
- If the fact is a COMPUTED BEHAVIOR (range-membership logic, decorator-composition ordering, any
  runtime output that depends on unfixed internal representation) → a genuinely EXECUTED test living
  directly in a test file (not a reusable `SecureDefaultsAssertion` method), explicitly flagged as a
  departure from the IL-only discipline. Two so far: WebhookSsrfGuardLock's IP-range accept/reject
  table, and CacheEncryptionAndRedisValidationLock's compress-then-encrypt size-ratio assertion.
- `AssertMethodBodyInvokesMethod`/`AssertMethodBodyThrowsExceptionType` both auto-extend into
  compiler-generated lambda closures (`<{methodName}>b__*` on nested `<>c`/`<>c__DisplayClassN_M`
  types) — proven necessary the first time (T-318, `PostConfigure` lambda) and reused unchanged since.

**Near-universal finding — ALWAYS re-verify, never trust the design-time dependency note:** in every
occurrence except WebhookSsrfGuardLock/CacheEncryptionAndRedisValidationLock's authoring, the producing
domain had ALREADY SHIPPED the dependency by the time this phase's implementation session began, even
though it was correctly `○`/not-yet-dispatched at design time. Standard closeout move: correct the
CLAUDE.md/state-map.md prose in place with a "STALE-DEPENDENCY CORRECTION" annotation (never silently
rewrite) and implement the real-assembly test as GATING, not deferred. When designing the NEXT phase in
this family, still record the dependency as pending per the dispatcher's note (never fabricate an
already-shipped status), but flag in the phase's own Dependencies section that this is expected to
resolve before implementation, per the now 4-out-of-6-plus occurrence rate.

**Design/reality mismatches also recur** — don't assume the anticipated shape:
- CorsWildcardCredentialsGuard assumed an inline throw; the real guard used
  `IValidateOptions<T>` + `.ValidateOnStart()` instead — resolved by re-pointing
  `AssertMethodBodyInvokesMethod` at the internal validator's `Validate` method instead of using
  `AssertMethodBodyThrowsExceptionType`.
- WebhookSsrfGuardLock assumed plain `AddSingleton`; the real registration used `TryAddSingleton` —
  resolved by accepting both method-name literals in the technique itself, confirmed against real
  source before writing the method.
This is *why* CacheEncryptionAndRedisValidationLock's Technique B was deliberately pointed at
`OptionsBuilderExtensions.ValidateOnStart` (the actual eager-validation trigger) rather than assuming
a specific validator class shape — same lesson applied pre-emptively.

See also [[project_sk_diagnostic_registry]] for the running SK ID ledger this family never consumes from.
