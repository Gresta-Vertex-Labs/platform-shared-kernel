---
name: project-core-domain
description: 01.Core package set after WO-086 (Execution added, Guards merged into Core; Foundation tier + three Adapters) and how to recover a crashed predecessor session's uncommitted work
metadata:
  type: project
---

> WO-086 (2026-09): `SharedKernel.Execution` (context, accessor, `RequestContextScope`, propagation, `TenantId`/`TenantScope`, `IUnitOfWork`, `IAuditTrailWriter`) joined `01.Core`, and `SharedKernel.Primitives.Health.IReadinessProbe` replaced every per-domain probe interface. The status snapshot below was rewritten for it; the lessons after it are unchanged.

`01.Core` ships thirteen packages: Primitives, Core (which absorbed `SharedKernel.Guards` in WO-082/P-505),
Configuration, Execution, FeatureManagement, Cryptography, Compression, Validation, DataPrivacy, Localization
— all **Foundation tier** (may reference Foundation only) — plus the **Adapter-tier** Validation.FluentValidation,
Cryptography.KeyVault.Azure and Cryptography.Argon2. The build enforces the tiers (`eng/SharedKernelTiers.targets`,
SKTIER000–006 are errors). Before starting any session, re-check `01.Core/state-map.md`'s Phase Key Registry and
root `state-map.md`'s Phase Backlog for anything dispatched since — this note goes stale fast, this domain gets
small additive work orders frequently.

**A prior session can leave uncommitted, undocumented work on disk that the state-map still shows as `○`.**
On 2026-09-09 a `core-phase-implementer` run completed nine WO-083-tail phases cleanly but crashed on an
API error while apparently mid-way through `P-518` (domain-wide `TryAdd`/`TryAddEnumerable` DI-registration
standardization) — its own state-map task rows for P-518 were still all `○`, yet `git diff` showed every
`C-107`–`C-114` code conversion already correctly applied across all eight DI extension methods, XML docs
already updated, and even the tricky `TryAddEnumerable` fix for `SharedKernel.Validation`'s
`AddNationalIdValidator<T>()` already done correctly. Only the tests (T-85–T-88), the `01.Core/README.md`
DI-conventions section (DO-48), the local-feed repack (P-51), and the state-map/changelog bookkeeping were
actually missing. **Always diff the working tree against a phase's task list before assuming "not started"
== "no code exists" — a crash can happen after the code write but before the state-map update.**

**Writing T-85/T-86-style tests for a `TryAdd*` DI conversion is not just paperwork — it found a second,
real regression the phase's own design missed:** `SharedKernel.Cryptography.KeyVault.Azure`'s
`AzureKeyVaultCryptographyOptionsValidator` was converted `AddSingleton<IValidateOptions<T>,...>()` →
`TryAddSingleton<IValidateOptions<T>,...>()` per the phase's literal instruction — but `IValidateOptions<T>`
is ALSO a genuine multi-implementation collection type (the options-validation pipeline runs *every*
registered validator for a type), and the preceding `AddValidatedOptions<T>(section)` call already
registers the BCL's own `DataAnnotationValidateOptions<T>` against that identical service type via
`ValidateDataAnnotations()`. `TryAddSingleton` saw the service type already claimed and silently dropped
the custom cross-field validator — two host-startup tests (`EmptyKeyNames_ThrowsAtHostStartup`,
`CurrentKeyIdNotInKeyNames_ThrowsAtHostStartup`) stopped throwing with zero compile error. Fixed via
`TryAddEnumerable`, same as the already-known `INationalIdValidator` case. **`IValidateOptions<T>` joins
`INationalIdValidator` as a second load-bearing example of "looks like a single-winner service, is actually
a collection" in this domain — check for this whenever converting a `AddSingleton<IValidateOptions<...>>`
call site to `TryAdd*`.**

**An optional parameter added to an existing method breaks method-group conversion at every call site that
passes the method as a delegate (`Attach(ruleBuilder, IbanValidator.Validate)`), even though every existing
*call* of that method (`IbanValidator.Validate(x)`) keeps compiling fine.** `P-521` added
`allowFallbackForUnknownCountry = false` to `IbanValidator.Validate(string?, bool)`; the arity change alone
broke `SharedKernel.Validation.FluentValidation`'s `MustBeValidIban<T>()`, which referenced
`IbanValidator.Validate` as a bare method group to satisfy `Func<string?, Result>`. `dotnet build` on the
single affected project would have caught this in seconds — a full-solution build is the only thing that
actually proves a change like this didn't break a sibling package.

**Why:** `01.Core` is the platform's most-depended-upon domain (every tier references Foundation), so almost every cross-domain security/audit
review ends up dispatching a small, additive, single-package-scope phase here even when the review's main
subject is a different domain.

**How to apply:** Read the domain's own `state-map.md` Active Work paragraph first for a quick read, but
verify against `git status`/`git diff` before trusting "○ = nothing done." Run a full `Platform.SharedKernel.slnx`
build before reporting any phase complete — never trust a predecessor's or your own in-progress claim of
"root clean" until you've run it yourself.
