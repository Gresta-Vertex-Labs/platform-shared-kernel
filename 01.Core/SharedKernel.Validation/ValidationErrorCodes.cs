namespace SharedKernel.Validation;

/// <summary>
/// The error codes the identifier types return, one per distinct failure. Every code has exactly
/// one message in <see cref="ValidationMessages"/>, so a translation keyed by the code always fits.
/// </summary>
/// <remarks>
/// Null, empty and whitespace-only input returns <c>ErrorCodes.Validation.Required</c>
/// (<c>validation.required</c>) from <c>SharedKernel.Primitives</c> for every type. Codes are part
/// of the public contract: dashboards, client code and translations key on them.
/// </remarks>
public static class ValidationErrorCodes
{
    /// <summary>Codes returned by <see cref="SharedKernel.Validation.Iban"/>.</summary>
    public static class Iban
    {
        /// <summary>Not two letters and two digits followed by letters and digits, or longer than 34 characters.</summary>
        public const string InvalidFormat = "validation.iban.invalid_format";

        /// <summary>The country prefix is not in the IBAN registry.</summary>
        public const string UnsupportedCountry = "validation.iban.unsupported_country";

        /// <summary>The length does not match the registry length for the country.</summary>
        public const string InvalidLength = "validation.iban.invalid_length";

        /// <summary>The national account number (BBAN) does not match the country's structure.</summary>
        public const string InvalidBban = "validation.iban.invalid_bban";

        /// <summary>The ISO 13616 MOD 97-10 check digits are wrong.</summary>
        public const string InvalidCheckDigits = "validation.iban.invalid_check_digits";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.Bic"/>.</summary>
    public static class Bic
    {
        /// <summary>Not 8 or 11 characters in the bank, country, location, branch layout.</summary>
        public const string InvalidFormat = "validation.bic.invalid_format";

        /// <summary>The embedded country code is not an ISO 3166-1 code.</summary>
        public const string UnknownCountry = "validation.bic.unknown_country";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.CardNumber"/>.</summary>
    public static class CardNumber
    {
        /// <summary>Not 12 to 19 digits.</summary>
        public const string InvalidFormat = "validation.card_number.invalid_format";

        /// <summary>The Luhn check digit is wrong.</summary>
        public const string InvalidCheckDigit = "validation.card_number.invalid_check_digit";

        /// <summary>No known network matches, when a known network is required.</summary>
        public const string UnknownNetwork = "validation.card_number.unknown_network";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.CountryCode"/>.</summary>
    public static class CountryCode
    {
        /// <summary>Not two letters.</summary>
        public const string InvalidFormat = "validation.country_code.invalid_format";

        /// <summary>Two letters, but not an ISO 3166-1 alpha-2 code.</summary>
        public const string Unknown = "validation.country_code.unknown";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.CurrencyCode"/>.</summary>
    public static class CurrencyCode
    {
        /// <summary>Not three letters.</summary>
        public const string InvalidFormat = "validation.currency_code.invalid_format";

        /// <summary>Three letters, but not an active ISO 4217 currency code.</summary>
        public const string Unknown = "validation.currency_code.unknown";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.PhoneNumber"/>.</summary>
    public static class PhoneNumber
    {
        /// <summary>Not a + sign followed by 7 to 15 digits, the first not zero.</summary>
        public const string InvalidFormat = "validation.phone_number.invalid_format";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.Lei"/>.</summary>
    public static class Lei
    {
        /// <summary>Not 18 letters or digits followed by 2 digits.</summary>
        public const string InvalidFormat = "validation.lei.invalid_format";

        /// <summary>The ISO 7064 MOD 97-10 check digits are wrong.</summary>
        public const string InvalidCheckDigits = "validation.lei.invalid_check_digits";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.AbaRoutingNumber"/>.</summary>
    public static class AbaRoutingNumber
    {
        /// <summary>Not 9 digits.</summary>
        public const string InvalidFormat = "validation.aba_routing_number.invalid_format";

        /// <summary>The first two digits are outside the Federal Reserve ranges.</summary>
        public const string InvalidPrefix = "validation.aba_routing_number.invalid_prefix";

        /// <summary>The (3, 7, 1)-weighted check digit is wrong.</summary>
        public const string InvalidCheckDigit = "validation.aba_routing_number.invalid_check_digit";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.SepaCreditorId"/>.</summary>
    public static class SepaCreditorId
    {
        /// <summary>Not a country code, 2 digits, a 3-character business code and a 1–28 character identifier.</summary>
        public const string InvalidFormat = "validation.sepa_creditor_id.invalid_format";

        /// <summary>The country code is not an ISO 3166-1 code.</summary>
        public const string UnknownCountry = "validation.sepa_creditor_id.unknown_country";

        /// <summary>The ISO 7064 MOD 97-10 check digits are wrong.</summary>
        public const string InvalidCheckDigits = "validation.sepa_creditor_id.invalid_check_digits";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.VatNumber"/>.</summary>
    public static class VatNumber
    {
        /// <summary>No country prefix, or a prefix this package has no rules for.</summary>
        public const string UnsupportedCountry = "validation.vat_number.unsupported_country";

        /// <summary>The number does not have the format its country uses.</summary>
        public const string InvalidFormat = "validation.vat_number.invalid_format";

        /// <summary>The country's check digit is wrong.</summary>
        public const string InvalidCheckDigit = "validation.vat_number.invalid_check_digit";
    }

    /// <summary>Codes returned by <see cref="SharedKernel.Validation.NationalId"/> and national ID validators.</summary>
    public static class NationalId
    {
        /// <summary>No validator is registered for the country.</summary>
        public const string UnsupportedCountry = "validation.national_id.unsupported_country";

        /// <summary>The number does not have the format its country uses.</summary>
        public const string InvalidFormat = "validation.national_id.invalid_format";

        /// <summary>The country's check digit is wrong.</summary>
        public const string InvalidCheckDigit = "validation.national_id.invalid_check_digit";
    }
}
