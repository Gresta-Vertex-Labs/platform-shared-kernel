namespace SharedKernel.Validation.Errors;

/// <summary>
/// Well-known, stable error code constants for <c>SharedKernel.Validation</c>, organized by category.
/// </summary>
/// <remarks>
/// <para>
/// This is a package-local nested-static-class string-constant catalog. It is deliberately
/// NEVER added as a new nested category under <c>SharedKernel.Primitives.ErrorCodes</c> —
/// <c>ErrorCodes</c>'s own documented rule ("consuming packages may define additional local
/// constants without forking the SharedKernel") already covers this, and a whole
/// country/format-algorithm error-code catalog does not belong bloating the platform's
/// most-depended-upon primitives package.
/// </para>
/// </remarks>
public static class ValidationErrorCodes
{
    /// <summary>Codes for <see cref="Validators.IbanValidator"/> failures.</summary>
    public static class Iban
    {
        /// <summary>The value is not shaped like an IBAN, or its country prefix is not recognized.</summary>
        public const string InvalidFormat = "validation.iban.invalid_format";

        /// <summary>The value failed the ISO 13616 mod-97 check-digit validation.</summary>
        public const string InvalidCheckDigit = "validation.iban.invalid_check_digit";

        /// <summary>The value's length does not match its country's registered IBAN length.</summary>
        public const string InvalidLength = "validation.iban.invalid_length";
    }

    /// <summary>Codes for <see cref="Validators.BicValidator"/> failures.</summary>
    public static class Bic
    {
        /// <summary>The value is not a well-formed BIC, or its embedded country code is not recognized.</summary>
        public const string InvalidFormat = "validation.bic.invalid_format";
    }

    /// <summary>Codes for <see cref="Validators.PanValidator"/> failures.</summary>
    public static class Pan
    {
        /// <summary>The value is malformed or failed the Luhn (mod-10) checksum.</summary>
        public const string FailedLuhnCheck = "validation.pan.failed_luhn_check";

        /// <summary>No known card network's BIN range matched the value.</summary>
        public const string UnknownNetwork = "validation.pan.unknown_network";
    }

    /// <summary>Codes for <see cref="Validators.IsoCurrencyValidator"/> failures.</summary>
    public static class Currency
    {
        /// <summary>The value is not a recognized ISO 4217 currency code.</summary>
        public const string UnknownCode = "validation.currency.unknown_code";
    }

    /// <summary>Codes for <see cref="Validators.IsoCountryValidator"/> failures.</summary>
    public static class Country
    {
        /// <summary>The value is not a recognized ISO 3166-1 alpha-2 country code.</summary>
        public const string UnknownCode = "validation.country.unknown_code";
    }

    /// <summary>Codes for <see cref="Validators.E164PhoneValidator"/> failures.</summary>
    public static class Phone
    {
        /// <summary>The value is not a well-formed E.164 phone number.</summary>
        public const string InvalidFormat = "validation.phone.invalid_format";
    }

    /// <summary>Codes for <see cref="Validators.VatValidator"/> failures.</summary>
    public static class Vat
    {
        /// <summary>The value does not match the baseline cross-jurisdiction VAT number format.</summary>
        public const string InvalidFormat = "validation.vat.invalid_format";
    }

    /// <summary>Codes for national-identity-number validation failures.</summary>
    public static class NationalId
    {
        /// <summary>No <see cref="SharedKernel.Validation.NationalId.INationalIdValidator"/> is registered for the given country code.</summary>
        public const string UnknownCountry = "validation.national_id.unknown_country";

        /// <summary>The value failed its country's national-identity-number checksum algorithm.</summary>
        public const string InvalidChecksum = "validation.national_id.invalid_checksum";
    }
}
