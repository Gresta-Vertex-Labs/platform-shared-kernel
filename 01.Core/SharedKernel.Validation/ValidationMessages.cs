using SharedKernel.Localization;
using SharedKernel.Primitives.Errors;
using Codes = SharedKernel.Validation.ValidationErrorCodes;

namespace SharedKernel.Validation;

/// <summary>
/// The message behind every error code this package returns, as <see cref="LocalizedMessage"/>
/// definitions: the English default text and the named values a translation can use.
/// </summary>
/// <remarks>
/// <para>
/// Errors carry their values in <c>Error.MessageArguments</c>, so a translation such as
/// <c>"{country} IBAN'ları {expected} karakter olmalıdır."</c> is filled in by
/// <c>SharedKernel.Localization</c>. Turkish ships with the package:
/// <see cref="ValidationLocalizationExtensions.AddValidationTranslations"/>.
/// </para>
/// <para>
/// No message repeats the rejected value. A card or national ID number must not end up in an error
/// response or a log, and the rule is simplest applied to every type. Country and currency codes
/// that are only two or three letters are named in their own messages.
/// </para>
/// <para>
/// A custom <see cref="INationalIdValidator"/> should return <see cref="NationalIdInvalidFormat"/>
/// and <see cref="NationalIdInvalidCheckDigit"/>, so its errors translate like the built-in ones.
/// </para>
/// </remarks>
public static class ValidationMessages
{
    /// <summary><c>validation.required</c>: null, empty or whitespace-only input.</summary>
    public static readonly LocalizedMessage Required = LocalizedMessage.Define(
        ErrorCodes.Validation.Required, "A value is required.");

    /// <summary><see cref="Codes.Iban.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage IbanInvalidFormat = LocalizedMessage.Define(
        Codes.Iban.InvalidFormat,
        "An IBAN starts with a two-letter country code and two check digits, followed by letters and digits, 34 characters at most.");

    /// <summary><see cref="Codes.Iban.UnsupportedCountry"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> IbanUnsupportedCountry = LocalizedMessage.Define<string>(
        Codes.Iban.UnsupportedCountry, "{country} does not use IBANs.", "country");

    /// <summary><see cref="Codes.Iban.InvalidLength"/>, with the country code, the expected and the actual length.</summary>
    public static readonly LocalizedMessage<string, int, int> IbanInvalidLength = LocalizedMessage.Define<string, int, int>(
        Codes.Iban.InvalidLength,
        "An IBAN from {country} is {expected} characters long, not {actual}.",
        "country",
        "expected",
        "actual");

    /// <summary><see cref="Codes.Iban.InvalidBban"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> IbanInvalidBban = LocalizedMessage.Define<string>(
        Codes.Iban.InvalidBban, "The account number in this IBAN does not have the format {country} uses.", "country");

    /// <summary><see cref="Codes.Iban.InvalidCheckDigits"/>.</summary>
    public static readonly LocalizedMessage IbanInvalidCheckDigits = LocalizedMessage.Define(
        Codes.Iban.InvalidCheckDigits, "The IBAN check digits are not correct. Check it for a typing error.");

    /// <summary><see cref="Codes.Bic.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage BicInvalidFormat = LocalizedMessage.Define(
        Codes.Bic.InvalidFormat,
        "A BIC is 8 or 11 characters: a 4-letter bank code, a 2-letter country code, a 2-character location code and an optional 3-character branch code.");

    /// <summary><see cref="Codes.Bic.UnknownCountry"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> BicUnknownCountry = LocalizedMessage.Define<string>(
        Codes.Bic.UnknownCountry, "{country} in this BIC is not a country code.", "country");

    /// <summary><see cref="Codes.CardNumber.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage CardNumberInvalidFormat = LocalizedMessage.Define(
        Codes.CardNumber.InvalidFormat, "A card number is 12 to 19 digits.");

    /// <summary><see cref="Codes.CardNumber.InvalidCheckDigit"/>.</summary>
    public static readonly LocalizedMessage CardNumberInvalidCheckDigit = LocalizedMessage.Define(
        Codes.CardNumber.InvalidCheckDigit, "The card number is not valid. Check it for a typing error.");

    /// <summary><see cref="Codes.CardNumber.UnknownNetwork"/>.</summary>
    public static readonly LocalizedMessage CardNumberUnknownNetwork = LocalizedMessage.Define(
        Codes.CardNumber.UnknownNetwork, "The card network could not be identified from this card number.");

    /// <summary><see cref="Codes.CountryCode.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage CountryCodeInvalidFormat = LocalizedMessage.Define(
        Codes.CountryCode.InvalidFormat, "A country code is two letters, such as TR or DE.");

    /// <summary><see cref="Codes.CountryCode.Unknown"/>, with the code.</summary>
    public static readonly LocalizedMessage<string> CountryCodeUnknown = LocalizedMessage.Define<string>(
        Codes.CountryCode.Unknown, "{code} is not a country code.", "code");

    /// <summary><see cref="Codes.CurrencyCode.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage CurrencyCodeInvalidFormat = LocalizedMessage.Define(
        Codes.CurrencyCode.InvalidFormat, "A currency code is three letters, such as TRY or EUR.");

    /// <summary><see cref="Codes.CurrencyCode.Unknown"/>, with the code.</summary>
    public static readonly LocalizedMessage<string> CurrencyCodeUnknown = LocalizedMessage.Define<string>(
        Codes.CurrencyCode.Unknown, "{code} is not a currency code in use.", "code");

    /// <summary><see cref="Codes.PhoneNumber.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage PhoneNumberInvalidFormat = LocalizedMessage.Define(
        Codes.PhoneNumber.InvalidFormat,
        "A phone number is written in international format: a + sign, the country code and the number, 15 digits at most.");

    /// <summary><see cref="Codes.Lei.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage LeiInvalidFormat = LocalizedMessage.Define(
        Codes.Lei.InvalidFormat, "An LEI is 20 characters: 18 letters or digits followed by 2 check digits.");

    /// <summary><see cref="Codes.Lei.InvalidCheckDigits"/>.</summary>
    public static readonly LocalizedMessage LeiInvalidCheckDigits = LocalizedMessage.Define(
        Codes.Lei.InvalidCheckDigits, "The LEI check digits are not correct.");

    /// <summary><see cref="Codes.AbaRoutingNumber.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage AbaRoutingNumberInvalidFormat = LocalizedMessage.Define(
        Codes.AbaRoutingNumber.InvalidFormat, "A routing number is 9 digits.");

    /// <summary><see cref="Codes.AbaRoutingNumber.InvalidPrefix"/>.</summary>
    public static readonly LocalizedMessage AbaRoutingNumberInvalidPrefix = LocalizedMessage.Define(
        Codes.AbaRoutingNumber.InvalidPrefix, "A routing number starts with 00 to 12, 21 to 32, 61 to 72, or 80.");

    /// <summary><see cref="Codes.AbaRoutingNumber.InvalidCheckDigit"/>.</summary>
    public static readonly LocalizedMessage AbaRoutingNumberInvalidCheckDigit = LocalizedMessage.Define(
        Codes.AbaRoutingNumber.InvalidCheckDigit, "The routing number check digit is not correct.");

    /// <summary><see cref="Codes.SepaCreditorId.InvalidFormat"/>.</summary>
    public static readonly LocalizedMessage SepaCreditorIdInvalidFormat = LocalizedMessage.Define(
        Codes.SepaCreditorId.InvalidFormat,
        "A SEPA creditor identifier is a 2-letter country code, 2 check digits, a 3-character business code and a national identifier of up to 28 characters.");

    /// <summary><see cref="Codes.SepaCreditorId.UnknownCountry"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> SepaCreditorIdUnknownCountry = LocalizedMessage.Define<string>(
        Codes.SepaCreditorId.UnknownCountry, "{country} in this creditor identifier is not a country code.", "country");

    /// <summary><see cref="Codes.SepaCreditorId.InvalidCheckDigits"/>.</summary>
    public static readonly LocalizedMessage SepaCreditorIdInvalidCheckDigits = LocalizedMessage.Define(
        Codes.SepaCreditorId.InvalidCheckDigits, "The creditor identifier check digits are not correct.");

    /// <summary><see cref="Codes.VatNumber.UnsupportedCountry"/>, with the prefix as written (empty when there is none).</summary>
    public static readonly LocalizedMessage<string> VatNumberUnsupportedCountry = LocalizedMessage.Define<string>(
        Codes.VatNumber.UnsupportedCountry,
        "A VAT number starts with a supported country prefix; {country} is not one.",
        "country");

    /// <summary><see cref="Codes.VatNumber.InvalidFormat"/>, with the country prefix.</summary>
    public static readonly LocalizedMessage<string> VatNumberInvalidFormat = LocalizedMessage.Define<string>(
        Codes.VatNumber.InvalidFormat, "The VAT number does not have the format {country} uses.", "country");

    /// <summary><see cref="Codes.VatNumber.InvalidCheckDigit"/>, with the country prefix.</summary>
    public static readonly LocalizedMessage<string> VatNumberInvalidCheckDigit = LocalizedMessage.Define<string>(
        Codes.VatNumber.InvalidCheckDigit, "The {country} VAT number check digit is not correct.", "country");

    /// <summary><see cref="Codes.NationalId.UnsupportedCountry"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> NationalIdUnsupportedCountry = LocalizedMessage.Define<string>(
        Codes.NationalId.UnsupportedCountry, "National ID numbers from {country} cannot be checked.", "country");

    /// <summary><see cref="Codes.NationalId.InvalidFormat"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> NationalIdInvalidFormat = LocalizedMessage.Define<string>(
        Codes.NationalId.InvalidFormat, "The national ID number does not have the format {country} uses.", "country");

    /// <summary><see cref="Codes.NationalId.InvalidCheckDigit"/>, with the country code.</summary>
    public static readonly LocalizedMessage<string> NationalIdInvalidCheckDigit = LocalizedMessage.Define<string>(
        Codes.NationalId.InvalidCheckDigit, "The {country} national ID number check digit is not correct.", "country");
}
