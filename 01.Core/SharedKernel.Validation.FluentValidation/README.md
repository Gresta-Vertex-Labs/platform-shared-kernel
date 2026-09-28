# SharedKernel.Validation.FluentValidation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
[![FluentValidation 11](https://img.shields.io/badge/FluentValidation-11-blue)](https://docs.fluentvalidation.net/)

> **FluentValidation rules for IBANs, card numbers, VAT numbers, national IDs and every other `SharedKernel.Validation`
> identifier — `RuleFor(x => x.Iban).MustBeValidIban()` — plus the bridge that runs FluentValidation validators in the
> kernel request pipeline. Each failure carries the exact error code, translatable values and the field path, never the
> rejected value.**

Hand-written `.Must(BeAValidIban)` rules produce a generic code and a fixed English message, cannot tell a wrong length
from a wrong check digit, and put the rejected value into `AttemptedValue`, where a card number ends up in logs.

| You get | So that |
| --- | --- |
| One rule per identifier type, plus `MustBeValid<T, TValue>()` for any other | One line per field, running the same checks as the value types |
| The specific code per failure, such as `validation.iban.invalid_length` | Clients and dashboards see what is wrong, not just "IBAN invalid" |
| The error's values as placeholders, next to `{PropertyName}` and `{PropertyPath}` | The HTTP response shows the message in the caller's language, keyed by field |
| Rules that read the country from another property | A Turkish form's VKN or TCKN field is checked against its country field |
| No `AttemptedValue` and no `{PropertyValue}` | A card or national ID number cannot reach a log through a validation result |
| `AddFluentValidationRequestValidators(assemblies)` | Existing `IValidator<T>` classes run in the kernel pipeline's validation step |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Validation.FluentValidation" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project (or wherever your validators live) |
| Depends on | [`SharedKernel.Validation`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/SharedKernel.Validation/README.md), `SharedKernel.Application` (for `IRequestValidator<T>`), `FluentValidation` 11 |
| Namespaces | `SharedKernel.Validation.FluentValidation` |

## Quick start

```csharp
using FluentValidation;
using SharedKernel.Validation.FluentValidation;

public sealed record CreateCustomer(string Iban, string? Bic, string Country, string? TaxNumber, string? NationalId, string? Card);

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomer>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Iban).NotEmpty().MustBeValidIban();
        RuleFor(x => x.Bic).MustBeValidBic();
        RuleFor(x => x.Country).NotEmpty().MustBeValidCountryCode();
        RuleFor(x => x.TaxNumber).MustBeValidVatNumber(x => x.Country);   // "4540536920" with Country "TR"
        RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.Country); // TCKN with Country "TR"
        RuleFor(x => x.Card).MustBeValidCardNumber(requireKnownNetwork: true);
    }
}
```

Run the validators in the request pipeline:

```csharp
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Validation.FluentValidation;

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.Services.AddFluentValidationRequestValidators(typeof(Program).Assembly);   // the bridge + the validators; idempotent
```

The package has no configuration section.

## How it works

### The pipeline bridge

`AddFluentValidationRequestValidators(params Assembly[])` registers `FluentValidationRequestValidator<TRequest>` as the
open-generic `IRequestValidator<TRequest>` of `SharedKernel.Application`, and every FluentValidation validator found in
the assemblies you pass — scoped, public and internal, each at most once. Pass the same assemblies as
`AddSharedKernelApplication`; with no assembly it registers only the bridge. The pipeline itself never references
FluentValidation.

| Behaviour | Detail |
| --- | --- |
| Several validators for one request | Run one after another, never concurrently (a validator may use a scoped `DbContext`) |
| Each failure | One `Error.Validation(failure.ErrorCode, failure.ErrorMessage)`; `ErrorCodes.Validation.Failed` when a hand-built failure has no code |
| `Error.MessageArguments` | The failure's placeholder values plus `PropertyPath` and `PropertyName`; FluentValidation's `PropertyValue` is always dropped |
| No validator registered | No errors from the bridge; hand-written `IRequestValidator<T>` implementations run alongside it |

### From rule to HTTP response

1. `ValidationBehavior` (`SharedKernel.Application.Pipeline`) runs your validators and turns each failure into an `Error`,
   keeping the code, message and values; the field path goes into `MessageArguments["PropertyPath"]`.
2. `SharedKernel.Presentation.WebApi` returns ProblemDetails with `errors` (messages by field) and `errorCodes` (codes
   under the same keys), translated when a catalog with `AddValidationTranslations()` is registered.
3. `SharedKernel.Communication.Rest` reads both maps back into `Error` values on the calling service.

A Turkish caller sending an IBAN one character short gets:

```json
{
  "status": 400,
  "errors": { "Iban": ["DE IBAN'ı 22 karakter olmalıdır, girilen 21 karakter."] },
  "errorCodes": { "Iban": ["validation.iban.invalid_length"] }
}
```

### What a failure contains

For `Iban = "DE8937040044053201300"`:

| `ValidationFailure` member | Value |
| --- | --- |
| `PropertyName` | `Iban`, or the full path for nested rules, such as `Accounts[0].Iban` |
| `ErrorCode` | `validation.iban.invalid_length` |
| `ErrorMessage` | `An IBAN from DE is 22 characters long, not 21.` |
| `FormattedMessagePlaceholderValues` | `country` = `DE`, `expected` = 22, `actual` = 21, `PropertyName` = `Iban`, `PropertyPath` = `Iban` |
| `AttemptedValue` | `null` |
| `CustomState` | The `Error` from `SharedKernel.Validation` |

- **Null passes**, as with FluentValidation's own format rules; `""`/whitespace fails with `validation.required`. Use
  `NotEmpty()` for required fields.
- The country-dependent rules skip when the country is missing or invalid, so a bad country is reported once, by the
  country field's own rule.
- Each rule calls the type's own `Create`, so a value the rule accepts always parses.

## Recipes

### 1. Required versus optional fields

```csharp
RuleFor(x => x.Iban).NotEmpty().MustBeValidIban();   // required: missing → NotEmptyValidator
RuleFor(x => x.Bic).MustBeValidBic();                // optional: null passes, "" → validation.required
```

### 2. A collection of accounts

```csharp
RuleForEach(x => x.Accounts).ChildRules(account =>
{
    account.RuleFor(a => a.Iban).NotEmpty().MustBeValidIban();
    account.RuleFor(a => a.Currency).NotEmpty().MustBeValidCurrencyCode();
});
// A bad second IBAN is reported as "Accounts[1].Iban" — also the key in the ProblemDetails errors map.
```

### 3. Only check a field in some cases

```csharp
RuleFor(x => x.Iban).MustBeValidIban().When(x => x.PaymentMethod == "transfer");
```

`When` and `Unless` are the only options available after these rules.

### 4. Your own identifier type

Implement `IValidatedValue<T>` from `SharedKernel.Validation`, and `MustBeValid<T, TValue>()` works for it:

```csharp
public readonly record struct OrderNumber : IValidatedValue<OrderNumber>
{
    private OrderNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<OrderNumber> Create(string? value) =>
        value is { Length: 10 } && value.StartsWith("ORD", StringComparison.Ordinal) && value[3..].All(char.IsAsciiDigit)
            ? new OrderNumber(value)
            : Error.Validation("order_number.invalid_format", "An order number is ORD followed by 7 digits.");

    public static OrderNumber Parse(string s, IFormatProvider? provider) =>
        Create(s) is { IsSuccess: true } ok ? ok.Value : throw new FormatException("Invalid order number.");

    public static bool TryParse(string? s, IFormatProvider? provider, out OrderNumber result)
    {
        Result<OrderNumber> created = Create(s);
        result = created.IsSuccess ? created.Value : default;
        return created.IsSuccess;
    }
}

RuleFor(x => x.OrderNumber).MustBeValid<ReturnRequest, OrderNumber>();
```

### 5. Assert a failure in a unit test

```csharp
ValidationFailure failure = Assert.Single(new CreateCustomerValidator().Validate(customer).Errors);

Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, failure.ErrorCode);
Assert.Equal(22, failure.FormattedMessagePlaceholderValues["expected"]);
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddFluentValidationRequestValidators(params Assembly[] assemblies)` | `IRequestValidator<>` → `FluentValidationRequestValidator<>` (open generic) and the assemblies' `IValidator<T>` classes (scoped, each once); idempotent |

### Rules (`ValidationRuleBuilderExtensions`, on `IRuleBuilder<T, string>`)

| Rule | Checks | Notes |
| --- | --- | --- |
| `MustBeValidIban(allowUnregisteredCountry = false)` | `Iban`: 89 registry countries, length, account-number structure, check digits | `true` also accepts a valid ISO country not yet in the registry |
| `MustBeValidBic()` | `Bic` | |
| `MustBeValidCardNumber(requireKnownNetwork = false)` | `CardNumber`: 12–19 digits, Luhn | `true` also requires one of the 10 known networks |
| `MustBeValidCountryCode()` | `CountryCode`: ISO 3166-1 alpha-2 | |
| `MustBeValidCurrencyCode()` | `CurrencyCode`: active ISO 4217 | |
| `MustBeValidPhoneNumber()` | `PhoneNumber`: E.164 | |
| `MustBeValidLei()` / `MustBeValidAbaRoutingNumber()` / `MustBeValidSepaCreditorId()` | `Lei` / `AbaRoutingNumber` / `SepaCreditorId` | |
| `MustBeValidVatNumber()` | `VatNumber` with its prefix in the value | `DE136695976` |
| `MustBeValidVatNumber(x => x.Country)` | `VatNumber` of the country another property holds | Prefix optional: `4540536920` with `TR` |
| `MustBeValidNationalId(x => x.Country, registry = null)` | `NationalId` of that country | TCKN built in; pass a `NationalIdValidatorRegistry` for more countries |
| `MustBeValid<T, TValue>()` | Any `IValidatedValue<TValue>` | Includes your own types |

### Errors

Codes come from `SharedKernel.Validation`'s `ValidationErrorCodes` (stable), each with one message in
`ValidationMessages` and a Turkish translation.

### Logging

The package does not log.

## Testing

Validators are plain classes: call `Validate(…)` and assert on `ErrorCode` and `FormattedMessagePlaceholderValues` (recipe
5), not the English text, which may be reworded or translated. For sample inputs use `ValidationSampleGenerator` from
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md).
To run the pipeline with the bridge, see `ApplicationPipelineTestHarness` in
[`SharedKernel.Application.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Application.Testing/README.md).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Expect a null value to fail | Add `NotEmpty()` for required fields | Null passes, like FluentValidation's own format rules |
| Chain `WithMessage`, `WithErrorCode`, `WithName` or `WithSeverity` after these rules | Translate the code to reword; put `WithName` on an earlier rule: `RuleFor(x => x.Bic).NotEmpty().WithName("Bank code").MustBeValidBic()` | They do not compile: each failure sets its own code, which FluentValidation allows only through `Custom`, and `Custom` offers only `When`/`Unless` |
| Read `AttemptedValue` in a failure handler | Use `CustomState` (the `Error`) or the placeholder values | It is always `null` for these rules, by design |
| Rely on the VAT or national ID rule to catch a bad country | Give the country field `NotEmpty().MustBeValidCountryCode()` | Country-dependent rules skip when the country is invalid |

## Design decisions

**Why `Custom` rather than a `PropertyValidator`?** A property validator in FluentValidation 11 has one error code per
rule, but an IBAN can fail five ways, each with its own code and translation. Keeping the precise code matters more than
the `WithMessage` chain; translations cover rewording.

**Why nullability-oblivious signatures?** The rules take `IRuleBuilder<T, string>` without nullable annotations, as
FluentValidation's own do, so they apply to `string` and `string?` properties without warnings.

**Why no rejected value anywhere?** A validation result is often logged whole; leaving the value out is the only way to
keep card and identity numbers out of logs by default.

**Why a separate package?** Code that does not use FluentValidation — a Temporal activity, a scheduled job — never
depends on it.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
