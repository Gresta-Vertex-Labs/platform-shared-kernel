---
name: coordinated_breaking_wave_verification
description: How to verify 16.Testing's own build/tests are correct when a coordinated multi-domain breaking wave (e.g. WO-081) leaves sibling domains' production packages temporarily broken, since SharedKernel.Testing.csproj holds direct ProjectReferences to several NON-Abstractions production packages.
type: project
---

`16.Testing/SharedKernel.Testing.csproj` references not just `.Abstractions` packages but several
concrete production implementation packages directly — confirmed on disk (2026-09-08):
`07.Messaging.MassTransit`, `06.Persistence.EfCore`, `15.Integration.Webhooks`,
`17.Workflows.Temporal`, and (via `SharedKernel.Testing.SelfTests.csproj` only)
`02.Caching.FusionCache`. This is deliberate (16.Testing "may reference any layer") but has a sharp
consequence during a coordinated `01.Core`-first breaking wave (WO-081: P-497–P-502 across
`02.Caching`/`06.Persistence`/`07.Messaging`/`15.Integration`/`17.Workflows`/`16.Testing`):

**Why this matters:** when `01.Core` ships a breaking interface change (e.g. `ISymmetricEncryptionService`
gaining a required `associatedData` parameter) and this domain updates its own `Cryptography/` fakes to
match, `dotnet build SharedKernel.Testing.csproj` still FAILS — not because of anything in `16.Testing/`,
but because the other five domains' P-497–P-501 legs (migrating their own production call sites like
`CacheEncryptionSerializer.Encrypt(payload)`, `NullSymmetricEncryptionService`, `PayloadTransformMessageSerializer`)
had not yet landed. `16.Testing` is simultaneously "the wave's gate" (everyone downstream needs its fakes)
and itself gated by everyone downstream (via its own ProjectReferences) — this is not a contradiction,
just two different meanings of "depends on."

**How to apply — the verification technique used successfully this session:**
1. Run the REAL `dotnet build` first (no MSBuild property overrides) and capture the FULL error list.
   Attribute every single error to its own `.csproj` path (the bracketed suffix on each MSBuild error
   line) — if every error traces to a file OUTSIDE `16.Testing/`, that is proof your own code is clean,
   even though the overall build result is red.
2. Compare against a pre-change baseline captured with the SAME command before you touch anything —
   if the error SET is identical (same file, same line, same message) before and after your change,
   you introduced zero new errors.
3. For an actual green build/test signal beyond "these are someone else's errors," use
   `dotnet build <csproj> -p:BuildProjectReferences=false` — this compiles your project against
   whatever assemblies already sit in each referenced project's `bin/` output (stale, pre-wave, but
   still valid IL) instead of trying to rebuild them. Confirmed this lets `SharedKernel.Testing.csproj`
   and `.SelfTests.csproj` both build with 0 errors/0 warnings even while the other five domains'
   production code was mid-migration.
4. **Caveat, confirmed empirically**: `dotnet test` under that same `-p:BuildProjectReferences=false`
   harness can still throw a `TypeLoadException` at RUNTIME for any stale sibling assembly whose type
   no longer fully implements the freshly-rebuilt interface (e.g. `06.Persistence.EfCore`'s
   `NullSymmetricEncryptionService`, compiled before the AAD parameter existed, now missing four
   interface members) — but ONLY for tests that actually construct/resolve that specific stale type
   (e.g. an EF Core `DbContext` whose model-finalizing convention touches it). This is an artifact of
   the verification technique itself, not a regression — confirm by checking the failing tests are all
   in the OTHER domain's stale dependency chain (in this session: 10 `Persistence/` tests, zero
   `Cryptography/` tests), never assume it's your own bug without checking.
5. Never use this stale-reference technique's PASSING result as a substitute for reporting the real
   `dotnet build`'s red status honestly — report both: "real build red, every error traced to file X
   outside this domain" AND "stale-reference sanity build/test green, N pre-existing unrelated
   failures with cause Y."

This complements [[blocker_reconciliation_workflow]] — clearing the upstream (`01.Core`) blocker does
not mean the full-solution build goes green; five OTHER domains' own P-497–P-501 legs are a SEPARATE,
still-open blocker on the full build, one this domain's own session has no jurisdiction to close (per
the AC#4 evaluation already recorded at D-237 in this exact phase's own design).
