# SharedKernel.Validation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Validated value types for financial and identity data — `Iban`, `Bic`, `CardNumber`, `VatNumber`, `NationalId`,
> `CountryCode`, `CurrencyCode`, `PhoneNumber`, `Lei`, `AbaRoutingNumber`, `SepaCreditorId`. Parse once at the edge, and
> from then on the type itself guarantees the value is valid and normalized.**

A `string` named `iban` says nothing about whether anyone checked it. `Iban` can only be created through `Iban.Create`,
which checks the value against the SWIFT registry and returns the normalized form, or a `Result` failure that says
exactly what is wrong.

| You get | So that |
| --- | --- |
| IBAN checks for all 89 registry countries, including each country's account-number structure | A letter typed for a digit in a German IBAN is caught even when the check digits happen to match |
| VAT format and check digit for the 27 EU states, UK, Northern Ireland, Switzerland, Norway and Türkiye (VKN) | `DE136695976` is accepted and `DE136695978` rejected |
| Card Luhn check and network detection (Visa, Mastercard, Amex, Discover, JCB, UnionPay, Diners Club, Maestro, Mir, Troy) | You can route by network, and require a known one |
| `CardNumber` and `NationalId` mask themselves in `ToString()` | A card or identity number that reaches a log shows as `411111******1111` |
| One error code per failure; messages never repeat the input | Clients branch on `validation.iban.invalid_length`; responses and logs never contain the rejected number |
| Localized messages with named values, Turkish bundled | A Turkish caller sees `"DE IBAN'ı 22 karakter olmalıdır, girilen 21 karakter."` |
| `IParsable<T>` and a JSON converter on every type | They bind from routes and query strings; an invalid value in a JSON body fails deserialization |

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
<PackageReference Include="SharedKernel.Validation" />
<!-- optional: FluentValidation rules -->
<PackageReference Include="SharedKernel.Validation.FluentValidation" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Core` (guard clauses), `SharedKernel.Localization` (message definitions) |
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

Use the types directly in requests — an invalid value never reaches your handler:

```csharp
public sealed record CreatePayout(Iban Iban, CurrencyCode Currency, decimal Amount);

app.MapGet("/banks/{bic}", (Bic bic) => $"{bic.BankCode} in {bic.CountryCode}");
```

No registration is needed for the types. `AddSharedKernelValidation()` is only for national ID validators (recipe 3), and
there is no configuration section.

## How it works

| Type | Accepts | Normalized value | Extra members |
| --- | --- | --- | --- |
| `Iban` | 89 SWIFT registry countries; exact length and account-number structure; MOD 97-10 | `DE89370400440532013000` | `CountryCode`, `CheckDigits`, `Bban`, `ToPrintString()` |
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

- **Every type normalizes input** — trims, removes typed separators (spaces, hyphens; for phone numbers also dots and
  parentheses), upper-cases letters — and compares by normalized `Value`. `default(T)` has an empty `Value`.
- **Four entry points:** `Create(string?) → Result<T>` (never throws), `IsValid`, `Parse` (throws `FormatException` with
  the reason), `TryParse`. All types except `NationalId` implement `IValidatedValue<T>`. `NationalId` needs a country
  beside the number (`NationalId.Create(country, value)`) and has no JSON converter.
- **IBAN:** `Create` checks the shape (two letters, two digits, up to 30 letters or digits), the registry country, the
  exact length, the national account-number structure (Germany 18 digits; UK 4 letters + 14 digits; Türkiye 5 digits, a
  reserved digit, 16 alphanumerics) and MOD 97-10. `Create(value, allowUnregisteredCountry: true)` accepts a valid ISO
  country not yet in the registry (shape and check digits only); it never weakens a registered country's checks.
- **VAT:** the first two letters select the rules. Greece is `EL` (`GR` accepted and converted); Switzerland
  `CHE…MWST`/`TVA`/`IVA`/`TPV`; Norway `NO…MVA`. Short Belgian and Dutch forms are expanded. Numbers issued to individuals
  are format-only where the check digit depends on personal data (10-digit Bulgarian, 9/10-digit Czech, Latvian personal
  codes). A number without its prefix goes through `VatNumber.Create(country, number)`.
- **Cards:** the most specific issuer range wins, and a network is reported only when it allows the card's length. Troy
  cards starting with 65 report `Discover` (co-branded); 622126–622925 report `UnionPay`.
  `Create(value, requireKnownNetwork: true)` rejects an unknown network.
- **"Valid" means well-formed, not existing.** No type checks that an account is open, a VAT number registered or an LEI
  current — those go to the bank, VIES/HMRC or GLEIF.

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

See [`SharedKernel.Validation.FluentValidation`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/SharedKernel.Validation.FluentValidation/README.md).

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

A blank argument returns Core's `NullOrWhiteSpace` error naming the parameter. For national IDs use
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

Returning `ValidationMessages.NationalIdInvalidFormat`/`NationalIdInvalidCheckDigit` makes your errors translate like the
built-in ones.

### 4. Write generic code over any identifier

```csharp
static string Describe<T>(string input) where T : struct, IValidatedValue<T> =>
    T.Create(input) is { IsSuccess: true } ok ? ok.Value.Value : "invalid";

Describe<Lei>("5493001kjtiigc8y1r12");   // "5493001KJTIIGC8Y1R12"
```

### 5. Keep a card number out of logs

```csharp
CardNumber card = CardNumber.Parse(input, null);
logger.LogCardAccepted(card);           // renders 411111******1111
await provider.ChargeAsync(card.Value);  // the full number only where it must go
```

### 6. Translate the messages

```csharp
builder.Services.AddLocalizationCatalog(catalog => catalog
    .AddValidationTranslations()   // Turkish for every message in this package
    .AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));   // your own, added after, win
```

English needs nothing — it is each message's default text. `SharedKernel.Presentation.WebApi` then translates every error
in a ProblemDetails response.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddSharedKernelValidation()` | `NationalIdValidatorRegistry` over every registered `INationalIdValidator` (singleton, `TryAdd`) |
| `AddNationalIdValidator<TValidator>()` | One more `INationalIdValidator` (`TryAddEnumerable`) |
| `LocalizationCatalogBuilder.AddValidationTranslations()` | The bundled Turkish translations |

### Types

| Type | Purpose |
| --- | --- |
| `IValidatedValue<T>` | `Value` and `static Create`; implement it for your own identifiers |
| `ValidatedValueJsonConverter<T>` | Reads and writes a type as a JSON string; attached to every type except `NationalId` |
| `CardNetwork` | The detected network |
| `INationalIdValidator`, `TurkishNationalIdValidator`, `NationalIdValidatorRegistry` | National ID checks per country |
| `ValidationErrorCodes`, `ValidationMessages` | Every code, and the `LocalizedMessage` behind it |
| `ValidationGuardExtensions` (namespace `SharedKernel.Guards`) | `Guard.Against.Invalid<T>(value)`, `Guard.Against.InvalidNationalId(value, country, registry?)` |

### Errors

Every failure is `Error.Validation` with a code from `ValidationErrorCodes` and exactly one message; null or blank input
returns `validation.required` for every type. Values travel in `Error.MessageArguments` (for example
`iban.invalid_length` carries `country`, `expected`, `actual`). No message repeats the rejected value, except two- and
three-letter country and currency codes in their own "not a country code" messages. Codes are stable.

| Type | Codes (`validation.` + …) |
| --- | --- |
| `Iban` | `iban.invalid_format`, `iban.unsupported_country`, `iban.invalid_length`, `iban.invalid_bban`, `iban.invalid_check_digits` |
| `Bic` | `bic.invalid_format`, `bic.unknown_country` |
| `CardNumber` | `card_number.invalid_format`, `card_number.invalid_check_digit`, `card_number.unknown_network` |
| `VatNumber` | `vat_number.unsupported_country`, `vat_number.invalid_format`, `vat_number.invalid_check_digit` |
| `NationalId` | `national_id.unsupported_country`, `national_id.invalid_format`, `national_id.invalid_check_digit` |
| `CountryCode`, `CurrencyCode` | `country_code.invalid_format`, `country_code.unknown`, `currency_code.invalid_format`, `currency_code.unknown` |
| `PhoneNumber`, `Lei`, `AbaRoutingNumber`, `SepaCreditorId` | `phone_number.invalid_format`, `lei.*`, `aba_routing_number.*`, `sepa_creditor_id.*` |

### Logging

The package does not log.

### Coverage

The tables below are checked against the data the code uses (a test compares them), so what is listed is what the
package accepts. Reference data changes only with a release; a stricter rule for a country may reject values the
previous version accepted.

### IBAN countries

All 89 countries of the SWIFT IBAN registry, release 99 (December 2024). BBAN format: `n` digits, `a` upper-case
letters, `c` letters or digits.

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
| `GE` | Georgia | 22 | 2a, 16n |
| `GI` | Gibraltar | 23 | 4a, 15c |
| `GL` | Greenland | 18 | 14n |
| `GR` | Greece | 27 | 7n, 16c |
| `GT` | Guatemala | 28 | 4c, 20c |
| `HN` | Honduras | 28 | 4a, 20n |
| `HR` | Croatia | 21 | 17n |
| `HU` | Hungary | 28 | 24n |
| `IE` | Ireland | 22 | 4a, 6n, 8n |
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
| `VG` | British Virgin Islands | 24 | 4a, 16n |
| `XK` | Kosovo | 20 | 4n, 10n, 2n |
| `YE` | Yemen | 30 | 4a, 4n, 18c |

</details>

### VAT and tax numbers

32 prefixes; "Check" is the algorithm the tax authority publishes, applied to the national part (`Number`).

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

National IDs: Türkiye's TCKN (11 digits, not starting 0, two check digits from the odd and even position sums) is built
in; any other country uses your `INationalIdValidator` (recipe 3).

## Testing

The types are pure; use them directly. For valid and invalid sample inputs, use `ValidationSampleGenerator` from
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
(`SharedKernel.Testing.Validation`): `ValidIban("DE")`, `InvalidIban()`, `ValidBic()`, `ValidPan(CardNetwork.Visa)`,
`ValidVat(…)`, `ValidNationalId(…)`, `ValidE164Phone()`, `ValidCurrencyCode()`, `ValidCountryCode()` and their `Invalid…`
counterparts.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Log `card.Value` | Pass the `CardNumber` itself to logs | `ToString()` is masked; `Value` is not |
| Serialize a `CardNumber` into responses, events or caches casually | Keep card numbers out unless the full number is meant to be there | JSON writes the full number, since anything else would not read back |
| Treat valid as verified | Confirm with the authority when it matters | A valid IBAN may be closed and a valid VAT number deregistered |
| Parse a national phone number (`0532 123 45 67`) | Ask for, or prepend, the country code | `PhoneNumber` is E.164 only |
| `VatNumber.Create("4540536920")` without a prefix | `VatNumber.Create(country, number)` | It fails with `unsupported_country` |
| Keep un-parsed strings after the edge | Pass the typed value inward | The type is the proof the value was checked |

## Design decisions

**Why value types instead of validator functions?** Once parsed, a value cannot be invalid or un-normalized, so nothing
downstream checks it again, and a method that takes an `Iban` documents itself.

**Why does `Create` return `Result` while `Parse` throws?** Bad input is expected at the edge and should not cost an
exception; `Parse` exists for `IParsable<T>`, which gives route binding.

**Why one code per message, and no rejected values in messages?** A translation is keyed by code; and card and national
ID numbers must never reach a log — a rule that applies to every type is one nobody has to remember.

**Why compile the registry data in?** Lookups never do I/O, and an update is an ordinary package release.

**Why no phone numbering plans?** Checking whether a number could exist needs a multi-megabyte numbering-plan library;
E.164 format is what most systems store.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
