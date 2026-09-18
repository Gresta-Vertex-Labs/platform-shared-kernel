# SharedKernel.Validation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Dependencies: first-party only](https://img.shields.io/badge/third--party%20dependencies-none-success)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Validated value types for financial and identity data: `Iban`, `Bic`, `CardNumber`, `VatNumber`, `NationalId`,
> `CountryCode`, `CurrencyCode`, `PhoneNumber`, `Lei`, `AbaRoutingNumber` and `SepaCreditorId`. Parse once at the edge,
> and from then on the type itself guarantees the value is valid and normalized.**

A `string` named `iban` says nothing about whether anyone checked it, or in which form it is stored. `Iban` does: it
can only be created through `Iban.Create`, which checks the value against the SWIFT registry and returns the
normalized form, or a `Result` failure that says exactly what is wrong. The same applies to every type here.

| You get | So that |
| --- | --- |
| IBAN checks for all 89 registry countries, including each country's account-number structure | A letter typed for a digit in a German IBAN is caught even when the check digits happen to match |
| VAT format and check digit for the 27 EU states, UK, Northern Ireland, Switzerland, Norway and Türkiye (VKN) | `DE136695976` is accepted and `DE136695978` rejected, instead of both passing a loose pattern |
| Card number Luhn check and network detection (Visa, Mastercard, Amex, Discover, JCB, UnionPay, Diners Club, Maestro, Mir, Troy) | You can route by network, and require a known one when you need to |
| `CardNumber` and `NationalId` mask themselves in `ToString()` | A card or identity number that reaches a log is shown as `411111******1111` |
| One error code per failure, and messages that never repeat the input | Clients branch on `validation.iban.invalid_length`, and responses and logs never contain the rejected number |
| Every message defined with named values, and Turkish bundled | The HTTP boundary shows `"DE IBAN'ı 22 karakter olmalıdır, girilen 21 karakter."` to a Turkish caller |
| `IParsable<T>` and a JSON converter on every type | They bind from routes and query strings, and an invalid value in a JSON body fails deserialization |
| No exceptions for bad input | `Create` returns `Result<T>`; only `Parse` throws, for code that expects a valid value |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [The types](#the-types)
- [Coverage](#coverage)
- [Errors and translations](#errors-and-translations)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Validation
dotnet add package SharedKernel.Validation.FluentValidation   # optional: FluentValidation rules
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Depends on | `SharedKernel.Primitives` (`Result`, `Error`), `SharedKernel.Core` (guard clauses), `SharedKernel.Localization` (message definitions) |
| Namespaces | `SharedKernel.Validation`; guard clauses in `SharedKernel.Guards` |

## Quick start

Parse at the edge, keep the typed value:

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Validation;

Result<Iban> result = Iban.Create("de89 3704 0044 0532 0130 00");
if (result.IsFailure)
{
    return result.Error;   // validation.iban.invalid_check_digits, and so on
}

Iban iban = result.Value;
iban.Value;            // "DE89370400440532013000"
iban.CountryCode;      // DE
iban.ToPrintString();  // "DE89 3704 0044 0532 0130 00"
```

Every other type works the same way:

```csharp
VatNumber vat   = VatNumber.Create(CountryCode.Parse("TR", null), "4540536920").Value; // Türkiye VKN, prefix added
CardNumber card = CardNumber.Parse("4111 1111 1111 1111", null);   // card.Network == Visa, card.ToString() == "411111******1111"
NationalId id   = NationalId.Create(CountryCode.Parse("TR", null), "10000000146").Value; // id.ToString() == "*******0146"
bool ok         = PhoneNumber.IsValid("+90 (532) 123-45-67");      // true, stored as +905321234567
```

Use the types directly in requests. They bind from routes and read from JSON, so an invalid value never reaches your
handler:

```csharp
public sealed record CreatePayout(Iban Iban, CurrencyCode Currency, decimal Amount);

app.MapGet("/banks/{bic}", (Bic bic) => $"{bic.BankCode} in {bic.CountryCode}");
```

## The types

| Type | Accepts | Normalized value | Extra members |
| --- | --- | --- | --- |
| `Iban` | 89 SWIFT registry countries; exact length and account-number structure per country; MOD 97-10 | `DE89370400440532013000` | `CountryCode`, `CheckDigits`, `Bban`, `ToPrintString()` |
| `Bic` | 8 or 11 characters; valid country | `DEUTDEFF500` | `BankCode`, `CountryCode`, `LocationCode`, `BranchCode`, `IsHeadOffice` |
| `CardNumber` | 12–19 digits; Luhn | `4111111111111111` | `Network`, `Iin`, `Last4`, `Masked`; **`ToString()` is masked** |
| `VatNumber` | 32 prefixes; format and check digit per country | `DE136695976` | `Prefix`, `Number`, `SupportedPrefixes` |
| `NationalId` | Countries with a registered validator; Türkiye (TCKN) built in | `10000000146` | `Country`, `Masked`; **`ToString()` is masked** |
| `CountryCode` | ISO 3166-1 alpha-2, plus `XK` (Kosovo) | `TR` | |
| `CurrencyCode` | Active ISO 4217 currencies (same list as `SharedKernel.Domain`'s `CurrencyCatalog`) | `TRY` | |
| `PhoneNumber` | E.164: `+`, then 7–15 digits | `+905321234567` | |
| `Lei` | ISO 17442; MOD 97-10 | `5493001KJTIIGC8Y1R12` | |
| `AbaRoutingNumber` | 9 digits; Federal Reserve prefix ranges; weighted check digit | `011000015` | |
| `SepaCreditorId` | Country, check digits, business code, national identifier; MOD 97-10 | `DE98ZZZ09999999999` | `CountryCode`, `BusinessCode`, `NationalIdentifier` |

Every type:

- **Normalizes input.** It removes surrounding whitespace, the separators people type (spaces, hyphens, and for phone
  numbers also dots and parentheses), and upper-cases letters.
- **Has four entry points:**
  - `Create(string?) → Result<T>`, which never throws;
  - `IsValid(string?) → bool`;
  - `Parse`, which throws `FormatException` with the reason;
  - `TryParse`.
- **Compares by normalized value.** `default(T)` has an empty `Value`.

All types except `NationalId` implement `IValidatedValue<T>`, which gives generic code a single entry point (see
[recipe 4](#4-write-generic-code-over-any-identifier)).

`NationalId` needs a country beside the number, so it is created with `NationalId.Create(country, value)`, and it has no
JSON converter.

## Coverage

Each table is generated from, or checked against, the same data the code uses, so what is listed here is what the
package accepts.

### IBAN countries

All 89 countries of the SWIFT IBAN registry, release 99 (December 2024). The BBAN format is the national account
number after the country code and check digits: `n` digits, `a` upper-case letters, `c` letters or digits. `Create`
checks the length and this structure for every country.

<details>
<summary><b>Show all 89 countries</b></summary>

| Code | Country | Length | BBAN format |
| --- | --- | --- | --- |
| `AD` | Andorra | 24 | 8n, 12c |
| `AE` | United Arab Emirates | 23 | 3n, 16n |
| `AL` | Albania | 28 | 8n, 16c |
| `AT` | Austria | 20 | 16n |
| `AZ` | Azerbaijan | 28 | 4a, 20c |
| `BA` | Bosnia and Herzegovina | 20 | 16n |
| `BE` | Belgium | 16 | 12n |
| `BG` | Bulgaria | 22 | 4a, 6n, 8c |
| `BH` | Bahrain | 22 | 4a, 14c |
| `BI` | Burundi | 27 | 5n, 5n, 11n, 2n |
| `BR` | Brazil | 29 | 23n, 1a, 1c |
| `BY` | Belarus | 28 | 4c, 4n, 16c |
| `CH` | Switzerland | 21 | 5n, 12c |
| `CR` | Costa Rica | 22 | 18n |
| `CY` | Cyprus | 28 | 8n, 16c |
| `CZ` | Czech Republic | 24 | 20n |
| `DE` | Germany | 22 | 18n |
| `DJ` | Djibouti | 27 | 5n, 5n, 11n, 2n |
| `DK` | Denmark | 18 | 14n |
| `DO` | Dominican Republic | 28 | 4c, 20n |
| `EE` | Estonia | 20 | 16n |
| `EG` | Egypt | 29 | 25n |
| `ES` | Spain | 24 | 20n |
| `FI` | Finland | 18 | 14n |
| `FK` | Falkland Islands | 18 | 2a, 12n |
| `FO` | Faroe Islands | 18 | 14n |
| `FR` | France | 27 | 10n, 11c, 2n |
| `GB` | United Kingdom | 22 | 4a, 14n |
| `GE` | Georgia (country)|Georgia | 22 | 2a, 16n |
| `GI` | Gibraltar | 23 | 4a, 15c |
| `GL` | Greenland | 18 | 14n |
| `GR` | Greece | 27 | 7n, 16c |
| `GT` | Guatemala | 28 | 4c, 20c |
| `HN` | Honduras | 28 | 4a, 20n |
| `HR` | Croatia | 21 | 17n |
| `HU` | Hungary | 28 | 24n |
| `IE` | Republic of Ireland|Ireland | 22 | 4a, 6n, 8n |
| `IL` | Israel | 23 | 19n |
| `IQ` | Iraq | 23 | 4a, 15n |
| `IS` | Iceland | 26 | 22n |
| `IT` | Italy | 27 | 1a, 10n, 12c |
| `JO` | Jordan | 30 | 4a, 4n, 18c |
| `KW` | Kuwait | 30 | 4a, 22c |
| `KZ` | Kazakhstan | 20 | 3n, 13c |
| `LB` | Lebanon | 28 | 4n, 20c |
| `LC` | Saint Lucia | 32 | 4a, 24c |
| `LI` | Liechtenstein | 21 | 5n, 12c |
| `LT` | Lithuania | 20 | 16n |
| `LU` | Luxembourg | 20 | 3n, 13c |
| `LV` | Latvia | 21 | 4a, 13c |
| `LY` | Libya | 25 | 21n |
| `MC` | Monaco | 27 | 10n, 11c, 2n |
| `MD` | Moldova | 24 | 2c, 18c |
| `ME` | Montenegro | 22 | 18n |
| `MK` | North Macedonia | 19 | 3n, 10c, 2n |
| `MN` | Mongolia | 20 | 4n, 12n |
| `MR` | Mauritania | 27 | 23n |
| `MT` | Malta | 31 | 4a, 5n, 18c |
| `MU` | Mauritius | 30 | 4a, 19n, 3a |
| `NI` | Nicaragua | 28 | 4a, 20n |
| `NL` | Netherlands | 18 | 4a, 10n |
| `NO` | Norway | 15 | 11n |
| `OM` | Oman | 23 | 3n, 16c |
| `PK` | Pakistan | 24 | 4a, 16c |
| `PL` | Poland | 28 | 24n |
| `PS` | Palestinian territories | 29 | 4a, 21c |
| `PT` | Portugal | 25 | 21n |
| `QA` | Qatar | 29 | 4a, 21c |
| `RO` | Romania | 24 | 4a, 16c |
| `RS` | Serbia | 22 | 18n |
| `RU` | Russia | 33 | 14n, 15c |
| `SA` | Saudi Arabia | 24 | 2n, 18c |
| `SC` | Seychelles | 31 | 4a, 20n, 3a |
| `SD` | Sudan | 18 | 14n |
| `SE` | Sweden | 24 | 20n |
| `SI` | Slovenia | 19 | 15n |
| `SK` | Slovakia | 24 | 20n |
| `SM` | San Marino | 27 | 1a, 10n, 12c |
| `SO` | Somalia | 23 | 4n, 3n, 12n |
| `ST` | São Tomé and Príncipe | 25 | 21n |
| `SV` | El Salvador | 28 | 4a, 20n |
| `TL` | East Timor | 23 | 19n |
| `TN` | Tunisia | 24 | 20n |
| `TR` | Türkiye | 26 | 5n, 1n, 16c |
| `UA` | Ukraine | 29 | 6n, 19c |
| `VA` | Vatican City | 22 | 3n, 15n |
| `VG` | British Virgin Islands|Virgin Islands, British | 24 | 4a, 16n |
| `XK` | Kosovo | 20 | 4n, 10n, 2n |
| `YE` | Yemen | 30 | 4a, 4n, 18c |

</details>

### VAT and tax numbers

32 prefixes. "Check" is the check-digit algorithm the tax authority publishes; the value after the prefix is shown as
the national part (`Number`).

<details>
<summary><b>Show all 32 prefixes</b></summary>

| Prefix | Country | National part | Check |
| --- | --- | --- | --- |
| `AT` | Austria | `U` + 8 digits | Luhn-based |
| `BE` | Belgium | 10 digits, starting 0 or 1 (9-digit form expanded) | MOD 97 |
| `BG` | Bulgaria | 9 digits (companies) or 10 digits (individuals) | Weighted MOD 11 for 9 digits; format only for 10 |
| `CY` | Cyprus | 8 digits + check letter, not starting 12 | Letter from weighted sum |
| `CZ` | Czechia | 8 digits (companies), 9–10 digits (individuals) | MOD 11 for 8 digits and special 9-digit numbers starting 6; format only otherwise |
| `DE` | Germany | 9 digits, not starting 0 | ISO 7064 MOD 11,10 |
| `DK` | Denmark | 8 digits, not starting 0 | Weighted MOD 11 |
| `EE` | Estonia | 9 digits | Weighted MOD 10 |
| `EL` | Greece (`GR` accepted) | 9 digits | Doubling MOD 11 |
| `ES` | Spain | 9 characters: DNI, NIE (X/Y/Z), K/L/M or CIF | Letter table or Luhn, by type |
| `FI` | Finland | 8 digits | Weighted MOD 11 |
| `FR` | France | 2-character key + 9-digit SIREN | SIREN Luhn + key |
| `HR` | Croatia | 11 digits (OIB) | ISO 7064 MOD 11,10 |
| `HU` | Hungary | 8 digits | Weighted MOD 10 |
| `IE` | Ireland | 7 digits + 1–2 letters, or the older mixed form | Weighted MOD 23 letter |
| `IT` | Italy | 11 digits, valid province code | Luhn |
| `LT` | Lithuania | 9 or 12 digits | Two-pass weighted MOD 11 |
| `LU` | Luxembourg | 8 digits | MOD 89 |
| `LV` | Latvia | 11 digits | Weighted MOD 11 for companies; format only for personal codes |
| `MT` | Malta | 8 digits, not starting 0 | Weighted MOD 37 |
| `NL` | Netherlands | 9 digits + `B` + 2 digits (short forms padded) | 11-test or MOD 97 (numbers since 2020) |
| `PL` | Poland | 10 digits | Weighted MOD 11 |
| `PT` | Portugal | 9 digits, not starting 0 | MOD 11 |
| `RO` | Romania | 2–10 digits, not starting 0 | Weighted MOD 11 |
| `SE` | Sweden | 12 digits ending `01` | Luhn |
| `SI` | Slovenia | 8 digits, not starting 0 | MOD 11 |
| `SK` | Slovakia | 10 digits | Divisible by 11 |
| `XI` | Northern Ireland | As `GB` | As `GB` |
| `GB` | United Kingdom | 9 or 12 digits, or `GD`/`HA` + 3 digits | Weighted MOD 97 |
| `CH` | Switzerland | `CHE` + 9 digits + `MWST`, `TVA`, `IVA` or `TPV` | MOD 11 (UID) |
| `NO` | Norway | 9 digits + `MVA` | Weighted MOD 11 |
| `TR` | Türkiye | 10-digit VKN | VKN algorithm |

</details>

### Card networks

Detected from the issuer range and the card length. The most specific matching range wins.

| Network | Ranges | Lengths |
| --- | --- | --- |
| Visa | 4 | 13, 16, 19 |
| Mastercard | 51–55, 2221–2720 | 16 |
| American Express | 34, 37 | 15 |
| Discover | 6011, 644–649, 65 (incl. Troy co-branded) | 16–19 |
| JCB | 3528–3589 | 16–19 |
| UnionPay | 62 | 16–19 |
| Diners Club | 30, 36, 38, 39 | 14–19 |
| Maestro | 5018, 5020, 5038, 5893, 6304, 6759, 6761–6763 | 12–19 |
| Mir | 2200–2204 | 16–19 |
| Troy | 9792 | 16 |

### National IDs

| Country | Number | Check |
| --- | --- | --- |
| Türkiye (built in) | TCKN: 11 digits, not starting 0 | Two check digits from the odd and even position sums |
| Any other | Your `INationalIdValidator` | See [recipe 3](#3-add-a-countrys-national-id-check) |

## Errors and translations

Every failure is an `Error.Validation` whose code is in `ValidationErrorCodes`. Each code has exactly one message, and
null or blank input returns `validation.required` for every type.

| Type | Codes (`validation.` + …) |
| --- | --- |
| `Iban` | `iban.invalid_format`, `iban.unsupported_country`, `iban.invalid_length`, `iban.invalid_bban`, `iban.invalid_check_digits` |
| `Bic` | `bic.invalid_format`, `bic.unknown_country` |
| `CardNumber` | `card_number.invalid_format`, `card_number.invalid_check_digit`, `card_number.unknown_network` |
| `VatNumber` | `vat_number.unsupported_country`, `vat_number.invalid_format`, `vat_number.invalid_check_digit` |
| `NationalId` | `national_id.unsupported_country`, `national_id.invalid_format`, `national_id.invalid_check_digit` |
| `CountryCode`, `CurrencyCode` | `country_code.invalid_format`, `country_code.unknown`, `currency_code.invalid_format`, `currency_code.unknown` |
| `PhoneNumber`, `Lei`, `AbaRoutingNumber`, `SepaCreditorId` | `phone_number.invalid_format`, `lei.*`, `aba_routing_number.*`, `sepa_creditor_id.*` |

Messages are `SharedKernel.Localization` definitions in `ValidationMessages`, and the values travel in
`Error.MessageArguments`. For example, `iban.invalid_length` carries `country`, `expected` and `actual`.

Add the bundled Turkish translations, and `SharedKernel.Presentation.WebApi` translates every error in a ProblemDetails
response. English needs nothing, because it is each message's default text:

```csharp
builder.Services.AddLocalizationCatalog(catalog => catalog
    .AddValidationTranslations()   // Turkish for every message in this package
    .AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));   // your own, added after, win
```

No message repeats the rejected value. The only exception is two- and three-letter country and currency codes, which
are named in their own "not a country code" messages.

## How it works

### IBAN

In order, `Create` checks:

1. The general shape: two letters, two digits, then up to 30 letters or digits.
2. That the country is in the registry (release 99, December 2024, 89 countries).
3. The exact length for that country.
4. The national account number's structure, for example:
   - Germany: 18 digits;
   - UK: 4 letters then 14 digits;
   - Türkiye: 5 digits, a reserved digit, then 16 letters or digits.
5. The MOD 97-10 check digits.

The structure check catches errors the check digits alone can miss.

`Create(value, allowUnregisteredCountry: true)` accepts a valid ISO country that is not yet in the registry. It
checks only the shape and the check digits, for a country that adopts IBANs before the next package release. It
never weakens the checks for a registered country.

### VAT

The first two letters select the country's rules. Greece is `EL` in VIES, and `GR` is accepted and converted.
Switzerland is written `CHE…MWST` (or `TVA`/`IVA`/`TPV`) and Norway `NO…MVA`. Older short forms are expanded: a
9-digit Belgian number gets its leading `0`, and short Dutch numbers are zero-padded.

Every business number is checked in full. Numbers issued to individuals are checked for format only where their check
digit depends on a birth date or a separate personal-number scheme: 10-digit Bulgarian, 9- and 10-digit Czech, and
Latvian personal codes.

A number without its prefix, such as a VKN typed into a Turkish form, goes through `VatNumber.Create(country, number)`.

### Card numbers

The Luhn check validates the number. The network comes from the issuer range and the length: the most specific
matching prefix wins, and a network is only reported when it allows the card's length.

- **`Discover` for Troy cards that start with 65.** Those cards are co-branded with Discover. Troy's own range is 9792.
- **`UnionPay` for 622126–622925.** UnionPay issues those cards.

`Create(value, requireKnownNetwork: true)` rejects a card whose network is unknown.

### What "valid" means

Every type checks that a value is well-formed. None of them checks that it exists: that an account is open, a card has
funds, a VAT number is registered, or an LEI is current. Those questions go to the bank, the payment provider, VIES or
HMRC, and GLEIF.

## Recipes

### 1. Validate a request with FluentValidation

```csharp
using SharedKernel.Validation.FluentValidation;

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomer>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Iban).NotEmpty().MustBeValidIban();
        RuleFor(x => x.Country).NotEmpty().MustBeValidCountryCode();
        RuleFor(x => x.TaxNumber).MustBeValidVatNumber(x => x.Country);   // "4540536920" with Country "TR"
        RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.Country);
    }
}
```

See the [FluentValidation package README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/SharedKernel.Validation.FluentValidation/README.md)
for how failures carry their codes and values through the pipeline.

### 2. Guard a constructor argument

```csharp
using SharedKernel.Guards;

public static Result<Payout> Create(string iban, decimal amount)
{
    if (Guard.Against.Invalid<Iban>(iban) is { } error)
    {
        return error;
    }

    return new Payout(Iban.Parse(iban, null), amount);
}
```

A blank argument returns Core's own `NullOrWhiteSpace` error, naming the parameter. For national IDs, use
`Guard.Against.InvalidNationalId(value, country)`.

### 3. Add a country's national ID check

```csharp
public sealed class DutchBsnValidator : INationalIdValidator
{
    public CountryCode Country => CountryCode.Parse("NL", null);

    public Result Validate(string number) =>
        number.Length == 9 && number.All(char.IsAsciiDigit) && ElevenTest(number)
            ? Result.Success()
            : ValidationMessages.NationalIdInvalidCheckDigit.ToError(ErrorType.Validation, "NL");

    private static bool ElevenTest(string n) =>
        (Enumerable.Range(0, 8).Sum(i => (9 - i) * (n[i] - '0')) - (n[8] - '0')) % 11 == 0;
}

builder.Services.AddSharedKernelValidation().AddNationalIdValidator<DutchBsnValidator>();

// Then inject NationalIdValidatorRegistry and pass it to NationalId.Create(country, value, registry).
```

Returning `ValidationMessages.NationalIdInvalidFormat` and `NationalIdInvalidCheckDigit` makes your errors translate
like the built-in ones.

### 4. Write generic code over any identifier

```csharp
static string Describe<T>(string input) where T : struct, IValidatedValue<T> =>
    T.Create(input) is { IsSuccess: true } ok ? ok.Value.Value : "invalid";

Describe<Lei>("5493001kjtiigc8y1r12");   // "5493001KJTIIGC8Y1R12"
```

### 5. Store a card number safely in logs and traces

```csharp
CardNumber card = CardNumber.Parse(input, null);
logger.LogCardAccepted(card);          // renders 411111******1111
await provider.ChargeAsync(card.Value); // the full number only where it must go
```

## Reference

### Entry points on every type

| Member | Returns | Throws |
| --- | --- | --- |
| `Create(string?)` | `Result<T>` | Never for bad input |
| `IsValid(string?)` | `bool` | Never |
| `Parse(string, IFormatProvider?)` | `T` | `FormatException` with the validation message; `ArgumentNullException` for null |
| `TryParse(string?, IFormatProvider?, out T)` | `bool` | Never |

### Other types

| Type | Purpose |
| --- | --- |
| `IValidatedValue<T>` | The shared contract: `Value` and `static Create`. Implement it for your own identifiers |
| `ValidatedValueJsonConverter<T>` | Reads and writes a type as a JSON string; already attached to every type |
| `CardNetwork` | The detected network |
| `INationalIdValidator`, `TurkishNationalIdValidator`, `NationalIdValidatorRegistry` | National ID checks per country |
| `ValidationErrorCodes`, `ValidationMessages` | Every code, and the message definition behind it |
| `ValidationLocalizationExtensions.AddValidationTranslations()` | Adds the Turkish translations to a catalog |
| `ValidationServiceCollectionExtensions` | `AddSharedKernelValidation()`, `AddNationalIdValidator<T>()` |
| `ValidationGuardExtensions` | `Guard.Against.Invalid<T>(value)`, `Guard.Against.InvalidNationalId(value, country)` |

## Pitfalls

- **Logging `card.Value`.** `ToString()` is masked and `Value` is not. Pass the `CardNumber` itself to logs, and read
  `Value` only for the payment call.
- **Serializing a `CardNumber`.** JSON writes the full number, because anything else would not read back. Keep card
  numbers out of response bodies, events and caches unless the full number is meant to be there.
- **Treating valid as verified.** A valid IBAN may be closed and a valid VAT number deregistered. Confirm with the
  authority when it matters.
- **A national number without the country code.** `PhoneNumber` rejects `0532 123 45 67`. Ask for the country code, or
  prepend it yourself before parsing.
- **`VatNumber.Create("4540536920")` without a prefix** fails with `unsupported_country`. Use
  `VatNumber.Create(country, number)` for forms that collect the country separately.
- **Checking that a number exists.** No type calls VIES, a bank or GLEIF; that belongs in an integration, not here.

## Design decisions

- **Value types instead of validator functions.** Once parsed, a value cannot be invalid or un-normalized, so nothing
  downstream checks it again, and a method that takes an `Iban` documents itself.
- **`Create` returns `Result`, `Parse` throws.** Bad input is expected at the edge and should not cost an exception.
  `Parse` exists for `IParsable<T>`, which is what gives route binding.
- **One code per message.** A translation is keyed by code, so a code that could mean two things could not be
  translated correctly.
- **No rejected values in messages.** Card and national ID numbers must not reach a log, and a rule that applies to
  every type is one nobody has to remember.
- **Masked `ToString()` for card and national ID numbers,** so string interpolation and structured logging are safe by
  default.
- **Registry data compiled in.** The IBAN registry, ISO lists and VAT rules are fixed per package version: a lookup
  never does I/O, and an update is an ordinary package release.
- **No phone numbering plans.** Checking whether a number could exist in a country needs a numbering-plan library
  several megabytes in size; E.164 format is what most systems store.

## AI quick reference

```text
TYPES       Iban Bic CardNumber VatNumber CountryCode CurrencyCode PhoneNumber Lei AbaRoutingNumber SepaCreditorId
            (all IValidatedValue<T>: Value, static Create(string?) -> Result<T>, IsValid, IParsable Parse/TryParse,
            JSON string via ValidatedValueJsonConverter<T>); NationalId.Create(CountryCode, string?, registry?).
NORMALIZE   Trim, remove typed separators, upper-case. Value is the canonical form; equality by Value.
IBAN        89 registry countries (release 99): length + BBAN structure + MOD 97-10. Create(v, allowUnregisteredCountry).
VAT         32 prefixes: EU27 (Greece EL; GR accepted), XI, GB, CH (CHE..MWST/TVA/IVA/TPV), NO (..MVA), TR (VKN).
            Create(string) needs the prefix; Create(CountryCode, number) accepts it without.
CARD        Luhn; Network in {Visa, Mastercard, AmericanExpress, Discover, Jcb, UnionPay, DinersClub, Maestro, Mir, Troy}.
            ToString()/Masked = first6 + *** + last4. Value = full PAN (JSON writes Value).
NATIONAL ID Registry per country; TR (TCKN) built in; AddSharedKernelValidation().AddNationalIdValidator<T>().
ERRORS      Error.Validation; codes ValidationErrorCodes.* (one message each); blank -> validation.required.
            Values in Error.MessageArguments; messages never include the input.
TRANSLATE   catalog.AddValidationTranslations() adds Turkish; English is the default text.
GUARDS      using SharedKernel.Guards; Guard.Against.Invalid<Iban>(value) -> Error?; InvalidNationalId(value, country).
FORBIDDEN   Logging card.Value; storing un-parsed strings after the edge; treating valid as existing/registered.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and every public member is documented.
- **Error codes are stable.** Changing one is a breaking change.
- **Reference data changes only with a release.** The IBAN registry, ISO 3166, ISO 4217 and VAT rules update in a
  new package version; a stricter rule for a country may reject values the previous version accepted.
- **`Create` never throws for bad input,** and no error message contains the rejected value.
- **Thread-safe.** The types are immutable, and all reference data is frozen.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
