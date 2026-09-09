---
name: ref-di-tryadd-convention-lock-pattern
description: CoreArchitectureRules/NoPlainServiceCollectionRegistrationPredicate (P-523/WO-083) — locking TryAdd*/TryAddEnumerable without breaking legitimate TryAddEnumerable multi-impl registrations
metadata:
  type: reference
---

`00.Governance/SharedKernel.ArchitectureTests/Rules/CoreArchitectureRules.cs` +
`Predicates/NoPlainServiceCollectionRegistrationPredicate.cs` (P-523/WO-083,
`SK.00.CoreDiRegistrationConventionLock`) is the platform's first `01.Core`-domain architecture-
rule class, locking `01.Core`'s own P-518 DI-registration convention correction.

**The trap a naive reading of "lock the TryAdd convention" falls into:** asserting PRESENCE of one
particular `TryAdd*` verb (or a blanket "no `Add*` anywhere") is wrong in two different directions:

- `SharedKernel.Validation.AddNationalIdValidator<TValidator>()` deliberately uses
  `TryAddEnumerable`, not `TryAddSingleton` — it's a genuine multi-implementation collection
  resolved via `sp.GetServices<INationalIdValidator>()`; `TryAddSingleton` there would silently
  keep only the first country validator.
- `SharedKernel.Cryptography.KeyVault.Azure`'s `IValidateOptions<AzureKeyVaultCryptographyOptions>`
  registration also needs `TryAddEnumerable`, since `AddValidatedOptions` already registers the
  BCL's own `DataAnnotationValidateOptions<T>` against the same service type — `TryAddSingleton`
  there silently dropped the custom cross-field validator (a real regression caught only because
  two host-startup tests stopped throwing, no compile error).
- `SharedKernel.FeatureManagement` deliberately leaves the third-party
  `Microsoft.FeatureManagement.AddFeatureManagement(...)` call untouched.

**The correct rule shape:** assert ABSENCE of the forbidden verbs
(`ServiceCollectionServiceExtensions.AddSingleton`/`AddScoped`/`AddTransient`) only — never presence
of any one particular compliant verb. `TryAddEnumerable` passes structurally because it isn't in
the forbidden name set; no exemption list is needed for it, and none was needed for
`AddFeatureManagement` either (different method name AND different declaring type). Detection:
match on `Call`/`Callvirt` operand `MethodReference.Name` in `{AddSingleton, AddScoped,
AddTransient}` AND `DeclaringType.FullName ==
"Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"` (the `TryAdd*`
family lives on a DIFFERENT type, `...Extensions.ServiceCollectionDescriptorExtensions" — name-only
matching would already be unambiguous, but the declaring-type check is free precision).

**Before writing any "lock this convention" rule, read the shipped implementation's own design
record first** (state-map.md changelog / the producing domain's own notes) — don't infer the rule
shape from the phase's one-line "What is needed" prose alone. See also
[[feedback-do-not-edit-forbidden-domains-even-temporarily]] for how this phase's non-vacuous
verification was done without touching `01.Core`.
