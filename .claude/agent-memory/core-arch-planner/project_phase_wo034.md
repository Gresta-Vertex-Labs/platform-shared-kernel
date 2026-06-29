---
name: project_phase_wo034
description: WO-034 locked rename — IPasswordHasher to IOneWayHasher generalization; exact names and version bump for P-211/212/213 to apply
type: project
---

WO-034 renames the existing, already-shipped (`1.0.0`) password-hashing surface in
`SharedKernel.Cryptography` to a secret-agnostic contract. Mechanism unchanged (PBKDF2-HMACSHA256
via `Rfc2898DeriveBytes.Pbkdf2`, self-describing output, rehash-needed detection) — naming only.

**Locked names (P-210, written into `01.Core/CLAUDE.md` and `01.Core/state-map.md` 2026-06-26):**
- `IPasswordHasher` → `IOneWayHasher`
- `Pbkdf2PasswordHasher` → `Pbkdf2OneWayHasher`
- `PasswordVerificationResult` → `HashVerificationResult` (members unchanged: `Failed`/`Success`/`SuccessRehashNeeded`)
- `Hash(string password)` → `Hash(string secret)`
- `Verify(string hash, string password)` → `Verify(string hash, string secret)` (the `hash` param name was already generic — untouched)
- Files to rename: `IPasswordHasher.cs`→`IOneWayHasher.cs`, `Pbkdf2PasswordHasher.cs`→`Pbkdf2OneWayHasher.cs`, `PasswordVerificationResult.cs`→`HashVerificationResult.cs` (all under `01.Core/SharedKernel.Cryptography/Hashing/`)
- `AddSharedKernelCryptography` registration line changes from `services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();` to the renamed equivalent — no lifetime change (still singleton)
- Version bump: `1.0.0` → `2.0.0` (breaking public interface rename) — applied at P-213 closeout, not before

**Other files touched by the rename (verified via repo-wide grep before P-210):**
- `01.Core/SharedKernel.Cryptography/Extensions/CryptographyServiceCollectionExtensions.cs` (registration + `<see cref>` remarks)
- `01.Core/SharedKernel.Cryptography/Options/CryptographyOptions.cs` (likely just a `<see cref>` doc reference — verify)
- `01.Core/SharedKernel.Cryptography/SharedKernel.Cryptography.Tests/Hashing/Pbkdf2PasswordHasherTests.cs` → rename file + add ≥1 non-password (API key) test case (P-212)
- `01.Core/SharedKernel.Cryptography/SharedKernel.Cryptography.Tests/Extensions/CryptographyServiceCollectionExtensionsTests.cs`
- `01.Core/SharedKernel.Consumer.Tests/ConsumerDependencyGraphTests.cs` — this is the closest thing to a real external consumer (resolves via local NuGet feed, not project refs) — must be updated in P-212
- `01.Core/README.md` — hashing usage example, updated in P-213 to show password + non-password (API key) side by side

**Why this matters:** confirmed via grep that zero consumers outside `SharedKernel.Cryptography` itself
and its own test/consumer-verify projects referenced `IPasswordHasher` at decision time — clean rename
window, not a deprecation cycle. arch-lead's decision record lives at
`.claude/agent-memory/arch-lead/project_wo034_cryptography_generalization.md` — it also declined two
other asks in the same request (moving Cryptography to its own domain number, splitting into
`.Abstractions`+per-algorithm packages) for reasons unrelated to this rename; don't re-litigate those
in this domain's planning, they were arch-lead-level calls.

**New CLAUDE.md rule added:** `IOneWayHasher` must never regain a domain-specific name or
domain-specific parameter names — it's a permanent guardrail, not a one-time fix.

**Phase shape:** P-210 (Design, done by core-arch-planner — names locked), P-211 (Scaffold — mechanical
rename + build-clean proof), P-212 (Tests — update existing + add non-password case + fix consumer
test), P-213 (Docs+Published — README + re-pack at 2.0.0 + consumer-verify re-run). All four phases
written to `01.Core/state-map.md` under phase-key `SK.01.WO034` at `○` in the same session (2026-06-26).
See [[project_phase_wo033]] for the prior Cryptography phase sequence this one extends.
