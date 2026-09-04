# SharedKernel.Validation.FluentValidation

A thin FluentValidation rule-builder adapter over [`SharedKernel.Validation`](../SharedKernel.Validation/README.md)'s culture-independent format validators. Depends on `SharedKernel.Validation` and the third-party `FluentValidation` package — the one exception to this domain's usual "zero third-party NuGet dependencies" rule, deliberately kept out of `SharedKernel.Validation` itself so a FluentValidation-free consumer (a Temporal activity, a lightweight worker with no MediatR pipeline) never pulls it in transitively.

## Included

| Rule | Delegates to |
|---|---|
| `.MustBeValidIban()` | `IbanValidator.Validate` |
| `.MustBeValidBic()` | `BicValidator.Validate` |
| `.MustBeValidPan()` | `PanValidator.Validate` |
| `.MustBeValidCurrencyCode()` | `IsoCurrencyValidator.Validate` |
| `.MustBeValidCountryCode()` | `IsoCountryValidator.Validate` |
| `.MustBeValidPhoneNumber()` | `E164PhoneValidator.Validate` |
| `.MustBeValidVatNumber()` | `VatValidator.Validate` |
| `.MustBeValidNationalId(countryCodeSelector, registry)` | the registered `INationalIdValidator` for the resolved country |

Every rule sets `FluentValidation.Results.ValidationFailure.ErrorCode` to the **exact** `ValidationErrorCodes` constant the underlying validator produced — never a single rule-fixed code. `IbanValidator`, for example, can fail with three distinct codes (`InvalidFormat` / `InvalidCheckDigit` / `InvalidLength`); a failure through this adapter carries whichever one actually applies, identical to the standalone `SharedKernel.Validation` call.

## Quick Start

```csharp
using FluentValidation;
using SharedKernel.Validation.FluentValidation;
using SharedKernel.Validation.NationalId;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator(INationalIdValidatorRegistry nationalIdRegistry)
    {
        RuleFor(x => x.Iban).MustBeValidIban();
        RuleFor(x => x.Bic).MustBeValidBic();
        RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
        RuleFor(x => x.PayerNationalId)
            .MustBeValidNationalId(x => x.PayerCountryCode, nationalIdRegistry);
    }
}
```

## Composing with `05.Application.Behaviors`'s `ValidationBehavior`

No extra plumbing is required — `ValidationBehavior<TRequest,TResponse>` already runs every registered `IValidator<TRequest>` and aggregates every `ValidationFailure` it produces, regardless of how each rule was built. A validator using these rules inside an `AbstractValidator<TCommand>` already resolved by that pipeline behavior participates automatically.

As of this writing, `ValidationBehavior` projects each failure via `Error.Validation(failure.PropertyName, failure.ErrorMessage)` — the FluentValidation property name becomes the downstream `Error.Code`, not `failure.ErrorCode`. The finer-grained `ValidationErrorCodes` constant this package attaches is still available directly on the raw `ValidationFailure.ErrorCode` for any consumer that wants it instead.

## Rules

- Every rule is built on FluentValidation's `Custom(...)` extension (it needs to inspect *which* code the underlying validator returned, not just pass/fail). Chaining `.WithMessage(...)` or `.WithErrorCode(...)` afterward has **no effect** — the message and error code always come from the `SharedKernel.Validation` validator. `.When(...)`/`.Unless(...)` and other rule-level conditions still work normally.
- `SharedKernel.Validation` itself stays free of any FluentValidation reference — this package is the only bridge.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
