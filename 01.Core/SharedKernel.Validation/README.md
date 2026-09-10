# SharedKernel.Validation

Culture-independent financial and identity format validators for the Platform.SharedKernel ecosystem. Pure C# — zero third-party NuGet dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` and `SharedKernel.Core` (the `Guard.Against.*` surface — formerly the separate `SharedKernel.Guards` package, merged into `SharedKernel.Core` by P-505/WO-082; same `SharedKernel.Guards`/`SharedKernel.Guards.Clauses` C# namespaces, unchanged).

## Included

| Validator | Checks |
|---|---|
| `IbanValidator` | Per-country length table + ISO 13616 mod-97 check digit |
| `BicValidator` | 8/11-character SWIFT format + embedded ISO 3166-1 country code |
| `PanValidator` | Luhn (mod-10) checksum; `DetectNetwork(...)` for Visa/Mastercard/Amex/Discover |
| `IsoCurrencyValidator` | ISO 4217 alphabetic currency codes |
| `IsoCountryValidator` | ISO 3166-1 alpha-2 country codes |
| `E164PhoneValidator` | E.164 phone number format |
| `VatValidator` | **Baseline, non-exhaustive** cross-jurisdiction VAT/tax-identifier format |
| `LeiValidator` | ISO 17442 Legal Entity Identifier — ISO/IEC 7064 MOD 97-10 check digits |
| `AbaRoutingNumberValidator` | US ABA bank routing number — (3, 7, 1)-weighted checksum |
| `SepaCreditorIdentifierValidator` | SEPA Creditor Identifier ("Gläubiger-ID") — ISO/IEC 7064 MOD 97-10 check digits |

Plus a pluggable, per-country `INationalIdValidatorRegistry` — `TckNationalIdValidator` (Turkey's TCKN checksum) ships as the built-in default at country code `"TR"`.

Every validator is dual-mode:

```csharp
bool           IsValid(string? value);
Result         Validate(string? value);   // Result — format checks carry no typed value
```

## Quick Start

```csharp
// Standalone Result call
Result result = IbanValidator.Validate("DE89370400440532013000");
if (result.IsFailure)
{
    return result.Error;   // e.g. ValidationErrorCodes.Iban.InvalidCheckDigit
}

// Guard.Against.* extension — same underlying validator, same error codes
using SharedKernel.Guards;
using SharedKernel.Validation.Guards;

Error? error = Guard.Against.InvalidIban(request.Iban);
if (error is not null)
{
    return Result<Account>.Failure(error);
}
```

## Table Refresh Cadence

`IbanValidator`'s per-country length table and the `IsoCurrencyValidator`/`IsoCountryValidator`
lookups are each fixed, compile-time tables — not a runtime call to any external registry. None
of ISO 13616, ISO 4217, or ISO 3166 publishes a single canonical "version number" the way software
does, so each validator instead exposes a `RegistryAsOf` constant stating when its table was last
verified against published registry references (e.g. `IbanValidator.RegistryAsOf`). These tables
are reviewed — and `RegistryAsOf` updated — alongside any work order that touches this package;
last reviewed WO-083/P-521 (2026-09-02).

`IbanValidator.Validate`/`.IsValid` also accept an opt-in `allowFallbackForUnknownCountry`
parameter (default `false`, preserving the hard-reject-on-unrecognized-country behavior above
unchanged). When explicitly set to `true`, a country prefix absent from the table skips the
country-specific length check and instead validates the value against ISO 13616's general shape
(bounded overall length, alphanumeric BBAN) plus the mod-97 check-digit algorithm alone — trading
away country-specific length/structure checking, but never checksum correctness:

```csharp
// Default: hard-rejects unrecognized country prefixes exactly as before.
Result strict = IbanValidator.Validate(value);

// Opt-in: accepts a mod-97-valid IBAN even under a country the table does not yet know about.
Result lenient = IbanValidator.Validate(value, allowFallbackForUnknownCountry: true);
```

## LEI, ABA Routing Number, and SEPA Creditor Identifier

Same dual-mode shape as every other validator in this package:

```csharp
// LEI (ISO 17442) — a 20-character alphanumeric identifier
Result lei = LeiValidator.Validate("506700GE1G29325QX363");

// US ABA bank routing number — 9 digits, (3, 7, 1)-weighted checksum
Result routing = AbaRoutingNumberValidator.Validate("111000025");

// SEPA Creditor Identifier ("Gläubiger-ID") — country + check digits + business code + national ID
Result creditorId = SepaCreditorIdentifierValidator.Validate("DE98ZZZ09999999999");

// Guard.Against.* extensions
Error? leiError = Guard.Against.InvalidLei(request.CounterpartyLei);
Error? routingError = Guard.Against.InvalidAbaRoutingNumber(request.RoutingNumber);
Error? creditorIdError = Guard.Against.InvalidSepaCreditorIdentifier(request.CreditorId);
```

## National ID Registry

```csharp
// Register (Program.cs) — pre-seeded with TckNationalIdValidator ("TR")
builder.Services.AddSharedKernelValidation()
    .AddNationalIdValidator<MySecondCountryNationalIdValidator>();

// Use
public class KycService(INationalIdValidatorRegistry registry)
{
    public Error? ValidateNationalId(string idNumber, string countryCode) =>
        Guard.Against.InvalidNationalId(idNumber, countryCode, registry);
}
```

`TryGetValidator` never throws for an unregistered country — it returns `false`.

## Rules

- `Guard.Throw.*` parity is intentionally out of scope for this package — the `Guard.Throw` nested class (now living in `SharedKernel.Core`, under the `SharedKernel.Guards` namespace, since P-505/WO-082) is hand-enumerated and hardcoded; extending it requires modifying `SharedKernel.Core` itself.
- `ValidationErrorCodes` is package-local — never added to `SharedKernel.Primitives.ErrorCodes`.
- `VatValidator` performs a format check only — it never validates a per-country checksum and a passing result is not proof of a real, registered VAT identifier.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
