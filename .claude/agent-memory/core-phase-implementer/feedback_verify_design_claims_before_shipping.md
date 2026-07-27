---
name: feedback-verify-design-claims-before-shipping
description: A core-arch-planner design doc can contain factual errors about the current codebase or BCL behavior — verify empirically, don't just implement the prose as given
metadata:
  type: feedback
---

When implementing P-293 (`IIdGenerator`/`UuidV7IdGenerator`), the pre-locked D-34 design text in `01.Core/CLAUDE.md` contained two factual errors that only surfaced during actual implementation/testing, not from reading the design alone:

1. **BCL behavior claim was wrong.** The design said "values generated in sequence sort as non-decreasing under the default `Guid` comparer." A test asserting this over 1000 GUIDs generated in a tight loop **failed empirically**. `Guid.CreateVersion7()` uses RFC 9562's "random" sub-method (not "monotonic random") — two values sharing the same millisecond timestamp have their remaining bits filled with independent random data, so there is no ordering guarantee between them. The guarantee only holds across values whose embedded millisecond timestamps actually differ.
2. **Codebase-state claim was wrong.** The design justified "no package-owned DI extension for `IIdGenerator`" partly on the premise that "`SharedKernel.Primitives` never references `Microsoft.Extensions.DependencyInjection.Abstractions`." This was already false before this phase — `ClockExtensions.AddClock()` has referenced that package since the domain's first session (`SharedKernel.Primitives.csproj` carries the `PackageReference`). The *decision* (no extension for this one abstraction) was still correct and is what T-37/DO-18 explicitly required, but the *stated rationale* for it was inaccurate.

**Why this matters:** A phase spec/design doc being "already locked" (marked `●` in the state-map) does not mean its prose is true — it means the shape was decided. Implementing the letter of a locked design (don't add a DI extension, do assert ordering) is still correct even when the design's own justification contains an error; the fix is to correct the justification via `sync-brain`, not to silently ship code that contradicts a test result, and not to skip writing the test just because the design implied it would pass trivially.

**How to apply:**
- When a task says "confirm X" or "assert Y holds," actually run it before trusting the design doc's phrasing — especially anything involving BCL/third-party runtime behavior (timestamp encoding, ordering guarantees, hashing/randomness) rather than pure application logic.
- Before writing a test around an external ordering/uniqueness/monotonicity guarantee, check whether the *exact* granularity claimed actually holds — "sorts non-decreasing" for a timestamp-prefixed random ID needs "across which resolution?" answered empirically, not assumed.
- If a design's rationale sentence references "no existing X" or "package never does Y" as justification, grep the actual `.csproj`/source before repeating it in new XML docs/README text — a design doc's supporting claims decay just like any other doc.
- When a correction is found, still call `sync-brain` (don't just silently fix code) — it is the append-only historical record other agents will read before touching the same file next. Root `CLAUDE.md`'s "What Goes Where" row for the same primitive should get the equivalent stale-qualifier cleanup in a **second, root-mode** `sync-brain` call (see the P-292 precedent this session mirrored) — sub-domain mode never touches the root file.
