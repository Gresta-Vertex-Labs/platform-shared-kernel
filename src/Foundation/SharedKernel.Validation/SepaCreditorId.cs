using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A SEPA Creditor Identifier, which identifies the collecting party in a SEPA Direct Debit, such
/// as <c>DE98ZZZ09999999999</c>.
/// </summary>
/// <remarks>
/// The layout is a 2-letter country code, 2 check digits, a 3-character creditor business code and
/// a national identifier of up to 28 letters or digits; 35 characters at most. The check digits
/// cover the country code and national identifier (ISO 7064 MOD 97-10) and skip the business code,
/// which the creditor chooses freely. Spaces are removed and letters upper-cased.
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<SepaCreditorId>))]
public readonly record struct SepaCreditorId : IValidatedValue<SepaCreditorId>
{
    private readonly string? _value;

    private SepaCreditorId(string value) => _value = value;

    /// <summary>Gets the creditor identifier in upper case without spaces.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the country.</summary>
    public CountryCode CountryCode => CountryCode.FromTrusted(Value.Length >= 2 ? Value[..2] : string.Empty);

    /// <summary>Gets the 3-character creditor business code.</summary>
    public string BusinessCode => Value.Length >= 7 ? Value[4..7] : string.Empty;

    /// <summary>Gets the national identifier after the business code.</summary>
    public string NationalIdentifier => Value.Length > 7 ? Value[7..] : string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The creditor identifier, in any case, with or without spaces.</param>
    /// <returns>The creditor identifier, or the reason it is invalid.</returns>
    public static Result<SepaCreditorId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string id = Text.Compact(value, " ");
        if (id.Length is < 8 or > 35
            || !Text.AllUpperLetters(id.AsSpan(0, 2))
            || !Text.AllDigits(id.AsSpan(2, 2))
            || !Text.AllUpperAlphanumeric(id.AsSpan(4)))
        {
            return ValidationMessages.SepaCreditorIdInvalidFormat.ToError(ErrorType.Validation);
        }

        string country = id[..2];
        if (!IsoData.Countries.Contains(country))
        {
            return ValidationMessages.SepaCreditorIdUnknownCountry.ToError(ErrorType.Validation, country);
        }

        // National identifier, then country code and check digits, modulo 97 is 1.
        string checkInput = string.Concat(id.AsSpan(7), id.AsSpan(0, 4));
        return Checksums.Mod97(checkInput) == 1
            ? new SepaCreditorId(id)
            : ValidationMessages.SepaCreditorIdInvalidCheckDigits.ToError(ErrorType.Validation);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid SEPA Creditor Identifier.</summary>
    /// <param name="value">The creditor identifier to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static SepaCreditorId Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<SepaCreditorId>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SepaCreditorId result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The creditor identifier.</returns>
    public override string ToString() => Value;
}
