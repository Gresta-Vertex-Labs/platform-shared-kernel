# SharedKernel.Validation

Culture-independent financial and identity format validators for the Platform.SharedKernel ecosystem. Pure C# — zero third-party NuGet dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` and `SharedKernel.Guards`.

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

- `Guard.Throw.*` parity is intentionally out of scope for this package — `SharedKernel.Guards`' `Guard.Throw` nested class is hand-enumerated and hardcoded inside that package; extending it requires modifying `SharedKernel.Guards` itself.
- `ValidationErrorCodes` is package-local — never added to `SharedKernel.Primitives.ErrorCodes`.
- `VatValidator` performs a format check only — it never validates a per-country checksum and a passing result is not proof of a real, registered VAT identifier.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
