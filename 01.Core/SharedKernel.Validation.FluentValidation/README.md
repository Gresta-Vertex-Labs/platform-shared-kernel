# SharedKernel.Validation.FluentValidation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![FluentValidation 11](https://img.shields.io/badge/FluentValidation-11-blue)](https://docs.fluentvalidation.net/)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **FluentValidation rules for IBANs, card numbers, VAT numbers, national IDs and every other
> [`SharedKernel.Validation`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/SharedKernel.Validation/README.md)
> identifier: `RuleFor(x => x.Iban).MustBeValidIban()`. Each failure carries the exact error code, values a translation
> can use, and the field path. It never carries the rejected value.**

Hand-written rules such as `.Must(BeAValidIban)` produce a generic code and a fixed English message. They can't tell a
wrong length from a wrong check digit, and they put the rejected value into `AttemptedValue`, where a card number ends
up in logs. These rules keep all of the detail `SharedKernel.Validation` produces, and leave out the value.

| You get | So that |
| --- | --- |
| One rule per identifier type, plus `MustBeValid<T, TValue>()` for any other | One line per field, running the same checks as the value types |
| The specific code per failure, such as `validation.iban.invalid_length` | Clients and dashboards see what is wrong, not just "IBAN invalid" |
| The error's values as placeholders, next to `{PropertyName}` and `{PropertyPath}` | The HTTP response shows the message in the caller's language and keys it by field |
| Rules that read the country from another property | A Turkish form's VKN or TCKN field is checked against its country field |
| No `AttemptedValue` and no `{PropertyValue}` | A card or national ID number can't reach a log through a validation result |
| Null passes, as with FluentValidation's own format rules | Required-ness stays with `NotEmpty()`, and an optional field isn't reported twice |

It is a separate package so that code which doesn't use FluentValidation, such as a Temporal activity or a scheduled
job, never depends on it.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Rules](#rules)
- [Run validators in the request pipeline](#run-validators-in-the-request-pipeline)
- [What a failure contains](#what-a-failure-contains)
- [End to end: from rule to HTTP response](#end-to-end-from-rule-to-http-response)
- [Recipes](#recipes)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Validation.FluentValidation
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter |
| Depends on | `SharedKernel.Validation`, `SharedKernel.Application` (for `IRequestValidator<T>`), `FluentValidation` 11 |
| Namespace | `SharedKernel.Validation.FluentValidation` |

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

## Rules

| Rule | Checks | Notes |
| --- | --- | --- |
| `MustBeValidIban(allowUnregisteredCountry = false)` | `Iban`: 89 registry countries, length, account-number structure, check digits | `true` also accepts a valid ISO country not yet in the registry |
| `MustBeValidBic()` | `Bic` | |
| `MustBeValidCardNumber(requireKnownNetwork = false)` | `CardNumber`: 12–19 digits, Luhn | `true` also requires one of the 10 known networks |
| `MustBeValidCountryCode()` | `CountryCode`: ISO 3166-1 alpha-2 | |
| `MustBeValidCurrencyCode()` | `CurrencyCode`: active ISO 4217 | |
| `MustBeValidPhoneNumber()` | `PhoneNumber`: E.164 | |
| `MustBeValidLei()` | `Lei` | |
| `MustBeValidAbaRoutingNumber()` | `AbaRoutingNumber` | |
| `MustBeValidSepaCreditorId()` | `SepaCreditorId` | |
| `MustBeValidVatNumber()` | `VatNumber` with its prefix in the value | `DE136695976` |
| `MustBeValidVatNumber(x => x.Country)` | `VatNumber` of the country another property holds | Prefix optional: `4540536920` with `TR` |
| `MustBeValidNationalId(x => x.Country, registry = null)` | `NationalId` of that country | TCKN built in; pass a registry for more countries |
| `MustBeValid<T, TValue>()` | Any `IValidatedValue<TValue>` | Includes your own types |

The country-dependent rules skip when the country is missing or not a valid code, so a bad country is reported once,
by the country field's own rule.

## Run validators in the request pipeline

`AddFluentValidationRequestValidators(params Assembly[] assemblies)` makes every FluentValidation `IValidator<T>` for a
request run in the kernel pipeline's validation step (`SharedKernel.Application.Pipeline`'s `ValidationBehavior`). It
registers `FluentValidationRequestValidator<TRequest>` as the open-generic
`SharedKernel.Application.Validation.IRequestValidator<TRequest>` and every validator found in the assemblies you pass —
scoped, public and internal, each at most once. Pass the same assemblies as `AddSharedKernelApplication`. With no
assembly it registers only the bridge, and you register the validators yourself.

```csharp
// Program.cs
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Validation.FluentValidation;

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR());
builder.Services.AddFluentValidationRequestValidators(typeof(Program).Assembly);   // the bridge + the validators; idempotent
```

| Behaviour | Detail |
| --- | --- |
| Several validators for one request | Run one after another, never concurrently (a validator may use a scoped `DbContext`) |
| Each failure | One `Error.Validation(failure.ErrorCode, failure.ErrorMessage)`; `ErrorCodes.Validation.Failed` when a hand-built failure has no code |
| `Error.MessageArguments` | The failure's placeholder values plus `PropertyPath` and `PropertyName`; FluentValidation's `PropertyValue` is always dropped |
| No validator registered | No errors from the bridge; hand-written `IRequestValidator<T>` implementations run alongside it |

The pipeline itself never references FluentValidation: this package is the only bridge.

## What a failure contains

For `Iban = "DE8937040044053201300"`, one character short:

| `ValidationFailure` member | Value |
| --- | --- |
| `PropertyName` | `Iban`, or the full path for nested rules, such as `Accounts[0].Iban` |
| `ErrorCode` | `validation.iban.invalid_length` |
| `ErrorMessage` | `An IBAN from DE is 22 characters long, not 21.` |
| `FormattedMessagePlaceholderValues` | `country` = `DE`, `expected` = 22, `actual` = 21, `PropertyName` = `Iban`, `PropertyPath` = `Iban` |
| `AttemptedValue` | `null` |
| `CustomState` | The `Error` from `SharedKernel.Validation` |

Every code is listed in `ValidationErrorCodes`. Each has one message in `ValidationMessages` and a Turkish translation.

## End to end: from rule to HTTP response

With the platform's pipeline, nothing between the rule and the response needs code:

1. `SharedKernel.Application.Pipeline`'s `ValidationBehavior` runs your validators and turns each failure into an `Error`,
   keeping the code, the message and the placeholder values. The field path goes into
   `MessageArguments["PropertyPath"]`.
2. `SharedKernel.Presentation.WebApi` returns a ProblemDetails response:
   - `errors` holds the messages, keyed by field;
   - `errorCodes` holds the codes under the same keys;
   - each message is translated into the caller's language when a catalog with `AddValidationTranslations()` is
     registered.
3. `SharedKernel.Communication.Rest` reads both maps back into `Error` values on the calling service.

A Turkish caller sending the short IBAN gets:

```json
{
  "status": 400,
  "errors": { "Iban": ["DE IBAN'ı 22 karakter olmalıdır, girilen 21 karakter."] },
  "errorCodes": { "Iban": ["validation.iban.invalid_length"] }
}
```

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

`When` and `Unless` are the only options available after these rules; see [Pitfalls](#pitfalls).

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

Assert on the code and the values, not the English message text, which may be reworded or translated.

## Pitfalls

- **Expecting a null value to fail.** It passes. Add `NotEmpty()` for required fields.
- **`WithMessage`, `WithErrorCode`, `WithName` or `WithSeverity` after these rules.** They don't compile, because each
  failure sets its own code, which FluentValidation only allows through `Custom`, and `Custom` offers only `When` and
  `Unless`.
  - To reword a message, translate its code.
  - To set the display name used in `{PropertyName}`, put `WithName` on an earlier rule of the same property:
    `RuleFor(x => x.Bic).NotEmpty().WithName("Bank code").MustBeValidBic()`.
- **Reading `AttemptedValue` in a failure handler.** It is always null for these rules, by design.
- **An invalid country makes the VAT and national ID rules skip.** That is intentional: give the country field its own
  `NotEmpty().MustBeValidCountryCode()` rule.

## Design decisions

- **`Custom`, not a `PropertyValidator`.** A property validator in FluentValidation 11 has one error code per rule, but
  an IBAN can fail five ways, each with its own code and translation. Keeping the precise code matters more than the
  `WithMessage` chain, and translations cover rewording.
- **Null passes,** matching FluentValidation's built-in format validators such as `EmailAddress`.
- **Nullability-oblivious signatures.** The rules take `IRuleBuilder<T, string>` without nullable annotations, as
  FluentValidation's own do, so they apply to `string` and `string?` properties without warnings.
- **No rejected value, anywhere.** A validation result is often logged whole; leaving the value out is the only way to
  keep card and identity numbers out of logs by default.

## AI quick reference

```text
RULES      MustBeValidIban(allowUnregisteredCountry) MustBeValidBic() MustBeValidCardNumber(requireKnownNetwork)
           MustBeValidCountryCode() MustBeValidCurrencyCode() MustBeValidPhoneNumber() MustBeValidLei()
           MustBeValidAbaRoutingNumber() MustBeValidSepaCreditorId() MustBeValidVatNumber()
           MustBeValidVatNumber(x => x.Country) MustBeValidNationalId(x => x.Country, registry?)
           MustBeValid<T, TValue>() for any IValidatedValue<TValue>.
NULL       Passes. "" / whitespace -> validation.required. Use NotEmpty() for required fields.
FAILURE    ErrorCode = specific ValidationErrorCodes value; ErrorMessage = English default text;
           FormattedMessagePlaceholderValues = error values + PropertyName (display) + PropertyPath;
           AttemptedValue = null; CustomState = SharedKernel Error.
CHAIN      Only When/Unless after these rules. No WithMessage/WithErrorCode/WithName/WithSeverity.
PIPELINE   services.AddFluentValidationRequestValidators(typeof(Program).Assembly) registers the bridge + that assembly's
           validators (no assembly -> bridge only; register validators yourself) -> IRequestValidator<T> in
           ValidationBehavior keeps the code -> ProblemDetails errors (by field) + errorCodes -> REST client restores both.
COUNTRY    VAT/NationalId rules skip when the country is missing/invalid; validate the country field separately.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and every public member is documented.
- **The same checks as the value types.** Each rule calls the type's own `Create`, so a value the rule accepts always
  parses.
- **Error codes are stable,** and come from `SharedKernel.Validation`'s `ValidationErrorCodes`.
- **No rejected value** in any failure produced by these rules.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
