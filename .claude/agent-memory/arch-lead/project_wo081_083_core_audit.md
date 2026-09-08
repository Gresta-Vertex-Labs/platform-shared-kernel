---
name: project_wo081_083_core_audit
description: WO-081/082/083 — 01.Core gold-standard audit turned into 33 phases (P-491-P-523) across three work orders
type: project
---

01.Core deep gold-standard audit (12 packages, ~181 files, 20 source-verified findings + 4 ratified user
decisions) turned into 33 phases, P-491–P-523, deliberately split across THREE work orders rather than one.

**Why split into 3 WOs:** the scope genuinely spans three independent blast radii — a breaking crypto/KMS
wave with a six-domain cascade, a separate breaking package-consolidation with its own narrow blast radius,
and a batch of non-breaking correctness/hygiene fixes. Conflating them into one WO id would make partial
rollback/tracking harder. See [[project_phase_numbering]] for the running P-NNN/WO-NNN ledger.

**WO-081 (P-491–P-504), Cryptography Breaking Wave:**
- AAD becomes a required (not optional-defaulted) parameter on every `ISymmetricEncryptionService` member (P-491)
- Sync `Encrypt`/`Decrypt` gated behind a new synchronous-provider capability marker instead of silently
  blocking via `GetAwaiter().GetResult()` (P-492) — this is the general fix; P-498 is the EF-Core-specific
  severe-defect closure that actually uses it
- **Arch-lead-identified gap beyond the user's own decision:** the user's ratified decision only named
  `IAsymmetricKeyProvider` going async. Left alone, `IAsymmetricSignatureService.Sign/Verify` would have had
  to block-on-async internally to call the now-async key provider — reintroducing the exact anti-pattern F1
  flagged, one layer up. P-493 makes `IAsymmetricSignatureService` async too (`SignAsync`/`VerifyAsync`,
  mirroring the retained-sync-with-blocking-caveat P-446 shape), plus fixes a key-disposal bug (provider-owned
  RSA/ECDsa instances were being `Dispose()`d by the caller) and a `Verify` vs `Sign` minimum-key-size check
  asymmetry
- New Azure Key Vault remote-signing `IAsymmetricKeyProvider` (P-494) — accepted as in-scope under decision 3
  ("fix Azure provider first") since it completes the existing provider family rather than adding a new cloud
- `SharedKernel.Cryptography.Argon2` new sibling package (P-495), PBKDF2 stays default/FIPS
- `AzureKeyVaultEncryptionKeyProvider` redesign (P-496): short stable key-version registry replaces encoding
  the full wrapped-DEK into `KeyId` — fixes per-call `CryptographyClient` construction, ~470-byte row bloat,
  unbounded `CachedEncryptionKeyProvider` cache growth, AND gives a real rotation story, all from one root-cause
  fix. Automatic rotation *scheduling* deliberately declined as out of scope (future 19.Scheduling candidate)
- Cascading migrations dispatched to exactly the six domains the user named (02.Caching/06.Persistence/
  07.Messaging/15.Integration/17.Workflows/16.Testing), plus two the fix chain required that the user didn't
  name: 13.ServiceDefaults (verify/close whether P-449's `AddSharedKernelKeyVaultKeyProvider` actually wraps
  the provider in `CachedEncryptionKeyProvider` by default — a real caching-wiring gap the design assumes is
  closed) and 00.Governance (mechanical lock)
- 06.Persistence's phase (P-498) is the SEVERE finding: EF Core's `ValueConverter` API has no async path as of
  EF Core 10, so the real fix is a `SavingChangesAsync`-time cache-warming hook plus a startup fail-fast guard,
  not something solvable inside the converter alone

**WO-082 (P-505–P-509), Guards → Core Merge:**
- User's own ratified decision (against arch-lead's initial recommendation to leave the 12 packages alone)
- **Key design upgrade:** merge `SharedKernel.Guards` into `SharedKernel.Core` while PRESERVING the
  `SharedKernel.Guards` C# namespace — only the package/assembly changes, so every consumer's fix is a
  one-line `PackageReference` swap, never a source edit. This is what keeps a "breaking package merge" cheap.
- User estimated "~20 dependents" from memory; direct `grep` across the repo found only 4 real in-repo
  project-reference dependents: `SharedKernel.Validation`, `SharedKernel.Consumer.Tests`,
  `00.Governance`'s `SharedKernel.ArchitectureTests`(+`.Tests`)/`GuardPurityRules.cs`, `03.Domain`. Always grep
  before estimating blast radius on a merge/rename — see [[feedback_verify_shipped_code_not_docs]].
- `GuardPurityRules.cs` needs re-scoping (not a blind assembly retarget) to the `SharedKernel.Guards`
  namespace within Core, using the same fully-qualified-metadata-name technique 00.Governance's SK0035
  already established, since an assembly-wide purity rule would be too broad once Core also holds unrelated
  types (base exceptions, BCL extensions, railway extensions)

**WO-083 (P-510–P-523), Correctness & Hygiene Hardening:** ResultTry exception-message redaction (SEVERE —
was leaking raw driver exception text like connection strings all the way to external ProblemDetails
responses, bypassing 14.Presentation's own redaction control) + cancellation passthrough; single-flight
cancellation-token leak in `CachedEncryptionKeyProvider`/Azure provider; PBKDF2 iteration floor+ceiling;
`CryptographicKey.Material` length validation (16-byte key was silently producing AES-128 despite every doc
promising AES-256); atomic TOTP replay-guard try-mark closing a TOCTOU double-accept bug — flagged breaking
but zero-blast-radius since no domain has shipped an `ITotpReplayGuard` consumer yet (12.Security.Totp/P-452
still queued); `SmartEnum` static-init trap; thread-safety fix for mutable-singleton
`InMemoryLocalizationCatalog`; corrected "zero dependencies" false claims for `SharedKernel.Primitives` —
chose to fix the docs/NuGet description rather than remove the live `AddClock()` API (removing shipped public
API to fix a documentation-accuracy problem was judged the wrong trade); `TryAdd`/`TryAddEnumerable` DI
convention + fixed a confirmed `AddSharedKernelCryptography()` double-registration bug; additive
`[OptionsValidator]` source-gen path; stale package metadata/Package-Board correction; versioned/dated
IBAN/ISO reference tables + opt-in mod-97-only fallback; bounded format-guard regex cache (sequenced after
the Guards merge since the file moves); governance lock for the TryAdd convention.

**Declined/deferred, no phase written:** `TreatWarningsAsErrors` (F16) and `EnablePackageValidation` (F17) are
`Directory.Build.props`/devops-lead concerns — the phase-backlog model has no domain for build tooling, so
these were flagged to the user as devops-lead follow-ups rather than forced into a 01.Core phase. Key-material
zeroization and an automatic crypto-period/max-ops-per-key policy: noted as genuine future gaps, not blocking.
A structured `Error` metadata bag for RFC 9457 extensions and new fintech validators (LEI/ABA/SEPA): deferred
to their own future work orders. FIPS 140-3 posture: documentation-only, folded into the same sync-brain pass
rather than a phase.

**Corrected, not a new finding:** the audit's "verify P-476 actually covers `[LoggerMessage]`/`{@Object}`"
gap turned out to already be resolved — 00.Governance's own state-map shows `SK0035` shipped 8/8 with a
real-assembly-verified test (2026-08-26). Root CLAUDE.md's "P-476, not yet shipped" line was stale because
root propagation was deliberately withheld during a concurrent multi-implementer session and never corrected
afterward. Fixed via sync-brain, not a new phase. **Separately discovered while fixing this:** root CLAUDE.md
was ALSO stale on WO-080/P-487/P-490 (state-map showed them `●` Complete/closed 2026-09-04, root CLAUDE.md
still said "○ Pending, not yet shipped" in four places) — corrected in the same sync-brain pass even though
it wasn't part of the original ask, since I'd already found it while editing the same row.

No `state-map-phase` calls were made — all fourteen touched domains were already `●` Published on the Domain
Summary Board (the gate only fires for domains at `○` Not Started).

**Follow-up ruling (same day):** coordinator flagged 4 "IDENTIFIED GAPS" items as dropped without a recorded
decision (crypto-period enforcement was already settled in P-496's own rationale — no action needed). Ruled
without renumbering/reopening P-491–P-523:
- **P-524** (WO-083, ACCEPT-narrow): zero only the `byte[]` buffers 01.Core genuinely owns
  (`CryptographicOperations.ZeroMemory`) — explicitly declines an `IDisposable CryptographicKey` (use-after-
  dispose hazard against `CachedEncryptionKeyProvider`'s shared, multi-reader cache — a cached value should
  never be individually disposable by one caller) and `ReadOnlySpan<char>`/`char[]` `IOneWayHasher` overloads
  (the realistic caller already holds an immutable `string` by the time it reaches this API; a Span overload
  can't un-happen that upstream allocation) — both declines recorded inside P-524's own rationale rather than
  as separate entries, since they're sub-asks of the same finding, not independent gaps.
- **P-525** (WO-083, ACCEPT): LEI/ABA-routing-number/SEPA-creditor-identifier validators — same shape as
  existing IBAN/BIC/PAN/etc., pure catalog completeness, no architectural question.
- **P-526** (WO-083, ACCEPT, docs-only): FIPS 140-3 posture statement — surfaced a real gap the coordinator's
  framing caught that I hadn't: TOTP/HOTP's RFC-default `HotpAlgorithm.Sha1` is not FIPS-approved and nothing
  steered a FIPS-constrained consumer to `.Sha256`/`.Sha512`.
- **DECLINED** (recorded as its own `### DECLINED` block in state-map.md, not a Phase Backlog entry, no P-ID
  consumed): a structured metadata bag on `Error`. Two independent domains (14.Presentation's P-402 and P-408)
  already solved the concrete needs (multi-field errors, retry-after hint) at the `ProblemDetails`-construction
  boundary without touching `Error` — that's the proof the boundary-extension pattern is sufficient, and the
  decisive reason to decline touching the platform's most-depended-upon type's equality/serialization contract
  for a speculative RFC 9457 vocabulary. Revisit trigger recorded: a third independent domain hitting a need
  the boundary pattern can't express — not more speculation.

**Pattern worth remembering:** when a coordinator/reviewer explicitly asks "rule on X, accept-or-decline, on
the record" — write the decline itself into `state-map.md` as a discoverable, non-phase entry (I used a
`### DECLINED —` heading, distinct from the `### P-NNN` phase format so it doesn't consume a Phase ID or get
mistaken for actionable work), not just a changelog line. A changelog entry alone would have been exactly the
kind of "dropped without a recorded ruling" gap the coordinator was already complaining about — changelog
entries scroll off; a section under Phase Backlog stays discoverable to the next audit.
