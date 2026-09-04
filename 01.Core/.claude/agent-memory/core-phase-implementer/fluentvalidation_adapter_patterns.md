---
name: fluentvalidation-adapter-patterns
description: Correct patterns for building an IRuleBuilder<T,TProperty> extension adapter over a standalone validator in FluentValidation 11.x, discovered implementing SharedKernel.Validation.FluentValidation (P-444)
type: feedback
---

When adding a FluentValidation rule-builder extension method that delegates to a standalone
validator (a `Validate(value) -> Result` style call) and needs the FluentValidation failure to
carry the exact error code the standalone call produced:

- **Use `ruleBuilder.Custom((value, context) => { ... context.AddFailure(new ValidationFailure(context.PropertyPath, message) { ErrorCode = code }); })`.**
  Never `ruleBuilder.Must(predicate).WithErrorCode(fixedCode)` — `WithErrorCode` can only attach one
  fixed code per rule. Any validator whose `Validate` can fail with more than one distinct error
  code (e.g. an IBAN validator failing with `InvalidFormat`/`InvalidCheckDigit`/`InvalidLength`
  depending on what's wrong) will silently lose code-parity on every failure path but one if built
  with `Must`+`WithErrorCode`. `Custom` is required whenever the underlying call can produce more
  than one distinct failure code — check this before defaulting to `Must`.

- **`IRuleBuilder<T,TProperty>.Custom(...)` returns `IRuleBuilderOptionsConditions<T,TProperty>` in
  FluentValidation 11.x, NOT `IRuleBuilder<T,TProperty>` or `IRuleBuilderOptions<T,TProperty>`.**
  A method signature written from a design doc that just says "returns `IRuleBuilder<T,string>`" is
  describing the *input* parameter type, not this return type — using the wrong return type produces
  a genuine CS0266 compiler error. Confirmed empirically against FluentValidation 11.11.0, not
  assumed from memory.

- **`ValidationContext<T>.PropertyName` is deprecated (CS0618) in FluentValidation 11.x** — use
  `.PropertyPath` instead (identical value, no deprecation warning).

- **A rule built on `.Custom(...)` does not honor a chained `.WithMessage(...)`/`.WithErrorCode(...)`
  afterward** — `Custom` adds its own `ValidationFailure` directly, bypassing the normal
  message/error-code pipeline those two methods hook into. State this in the XML docs so a caller
  doesn't waste time chaining a no-op.

**Why this matters for future 01.Core phases:** any future FluentValidation adapter work in this
domain (or elsewhere in the platform, if one is ever authored outside `01.Core`) should default to
`Custom(...)` rather than `Must(...)+WithErrorCode(...)` unless the underlying check genuinely has
only one failure mode. Verified via `SharedKernel.Validation.FluentValidation`/`ValidationRuleBuilderExtensions.cs` (P-444, 2026-09-02).
