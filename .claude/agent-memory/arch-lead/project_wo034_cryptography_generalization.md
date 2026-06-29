---
name: wo034-cryptography-generalization
description: Decision record for WO-034 — declined domain move + abstractions split for SharedKernel.Cryptography, accepted IPasswordHasher rename generalization
metadata:
  type: project
---

User asked three things about the just-shipped (WO-033, `1.0.0`, packed) `SharedKernel.Cryptography` package in `01.Core`:
1. Move it out of `01.Core` into its own domain
2. Split into `.Abstractions` + per-algorithm concrete packages
3. Remove/generalize password-specific hashing — it's auth vocabulary, not a primitive

**Decisions (all three independently reasoned, not rubber-stamped):**

1. **Declined the domain move.** `01.Core` is defined by "references nothing else in the platform," not by topic grouping. Cryptography qualifies by design (only refs `SharedKernel.Primitives` + `SharedKernel.Configuration`, zero third-party NuGet, zero ASP.NET/JWT/OIDC dep) — same bet `SharedKernel.Guards` already made. New domain numbers are justified by *infrastructure surface* (containers, vendors, network calls) — Caching/Persistence/Messaging have that; Cryptography is pure BCL logic, the opposite case. A dedicated domain number would cost a folder/CLAUDE.md/state-map/agent-pair for zero dependency-graph change.

2. **Declined the `.Abstractions` + per-provider split.** That convention exists specifically for *multiple real infrastructure providers* (Meilisearch vs ElasticSearch, S3 vs MinIO — operationally distinct systems ops configures differently). Cryptography has exactly one provider (the BCL) and already solves "pick an algorithm" via **keyed DI** — `IAsymmetricSignatureService` ships both `RsaSignatureService` and `EcdsaSignatureService` behind one interface, selected via `RsaSignatureServiceKey`/`EcdsaSignatureServiceKey` keyed singletons at resolve time. Splitting into per-algorithm packages would multiply 6 packages into a dozen+ for zero swap-cost benefit while fragmenting a pattern that already works. **Precedent for future algorithm additions (e.g., Argon2id): add a second keyed singleton in the same package, mirroring RSA/ECDSA — never a new package.**

3. **Accepted (as an Upgrade) the password-hashing generalization — this was the one real finding.** The *mechanism* (slow salted KDF, self-describing output, rehash-needed detection) is correctly general-purpose and was kept. The *naming* (`IPasswordHasher`, `Hash(string password)`, `PasswordVerificationResult`) bakes auth-domain vocabulary into a `01.Core` primitive — exactly the leak `01.Core` exists to prevent (it's why Cryptography wasn't placed in `12.Security` to begin with). Verified via repo-wide grep that zero consumers outside `SharedKernel.Cryptography` itself and its own test/consumer-verify projects reference `IPasswordHasher` today — package was packed (`1.0.0`) but not yet adopted anywhere, so this is a clean rename window, not a deprecation cycle.

**Phases written:** P-210–P-213, WO-034, all in `01.Core` (Design rename lock → Scaffold mechanical apply → Tests incl. new non-password case → Docs/Published re-pack at bumped version). All four phases stay inside `01.Core` — no other domain touched.

**Process notes:**
- `01.Core` was already `●` Published on the Domain Summary Board → did NOT call `state-map-phase` (only eligible for `○` domains). Phases queued in backlog only, to be picked up by `/dispatch-phase`, same pattern WO-030 used for already-Published domains.
- Did NOT call `sync-brain` yet — deferred to P-213 (Docs/Published) when the rename is actually implemented and names are final, rather than writing speculative names into root `CLAUDE.md` while phases are still `○ Pending`.
- Next phase ID after this work order: **P-214**. Next work order: **WO-035**.

See [[project_phase_numbering]] for the running phase-numbering ledger this entry extends.
