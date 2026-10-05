using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A VAT or tax identification number with its country prefix, checked against that country's
/// format and check digit: <c>DE136695976</c>, <c>FR40303265045</c>, <c>TR4540536920</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Countries.</b> The 27 EU member states under their VIES prefixes (Greece is <c>EL</c>;
/// <c>GR</c> is accepted and converted), Northern Ireland (<c>XI</c>), the United Kingdom
/// (<c>GB</c>), Switzerland (<c>CHE</c>…<c>MWST</c>/<c>TVA</c>/<c>IVA</c>/<c>TPV</c>), Norway
/// (…<c>MVA</c>) and Türkiye (<c>TR</c> + the 10-digit VKN). Any other prefix fails with
/// <see cref="ValidationErrorCodes.VatNumber.UnsupportedCountry"/>.
/// </para>
/// <para>
/// <b>What is checked.</b> Every business number is checked for format and check digit. Numbers
/// issued to individuals whose check digit depends on a birth date or a separate personal-number
/// scheme are checked for format only: 10-digit Bulgarian, 9- and 10-digit Czech, and Latvian
/// personal codes.
/// </para>
/// <para>
/// <b>Input.</b> Spaces, hyphens, dots, commas and slashes are removed and letters upper-cased.
/// Short older forms are expanded: a 9-digit Belgian number gets its leading <c>0</c>, and a Dutch
/// number shorter than 9 digits before the <c>B</c> is zero-padded. A number without its prefix,
/// such as a VKN typed into a Turkish form, goes through <see cref="Create(CountryCode, string?)"/>.
/// </para>
/// <para>
/// A valid number is well-formed. Whether it is currently registered is answered by VIES (EU),
/// HMRC (UK) or the national tax authority.
/// </para>
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<VatNumber>))]
public readonly record struct VatNumber : IValidatedValue<VatNumber>
{
    private readonly string? _value;

    private VatNumber(string prefix, string number)
    {
        Prefix = prefix;
        Number = number;
        _value = prefix == "CH" ? "CHE" + number : prefix + number;
    }

    /// <summary>Gets the full number with its prefix, such as <c>DE136695976</c> or <c>CHE107787577IVA</c>.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Gets the two-letter VAT prefix: an ISO 3166-1 code, except <c>EL</c> for Greece and <c>XI</c>
    /// for Northern Ireland.
    /// </summary>
    public string Prefix { get; }

    /// <summary>Gets the national part after the prefix (after <c>CHE</c> for Switzerland).</summary>
    public string Number { get; }

    /// <summary>Gets the VAT prefixes this type can check.</summary>
    public static IReadOnlyCollection<string> SupportedPrefixes => VatRules.ByPrefix.Keys;

    /// <summary>Validates and normalizes a VAT number that starts with its country prefix.</summary>
    /// <param name="value">The VAT number, such as <c>DE 136 695 976</c>.</param>
    /// <returns>The VAT number, or the reason it is invalid.</returns>
    public static Result<VatNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string compact = Text.Compact(value, " -.,/");
        if (compact.Length < 3 || !Text.AllUpperLetters(compact.AsSpan(0, 2)))
        {
            return ValidationMessages.VatNumberUnsupportedCountry.ToError(ErrorType.Validation, string.Empty);
        }

        string prefix = compact[..2];
        string number = compact[2..];

        if (prefix == "GR")
        {
            prefix = "EL";
        }
        else if (prefix == "CH")
        {
            if (number[0] != 'E')
            {
                return ValidationMessages.VatNumberInvalidFormat.ToError(ErrorType.Validation, "CH");
            }

            number = number[1..];
        }

        return Check(prefix, number);
    }

    /// <summary>
    /// Validates the national part of a VAT number for <paramref name="country"/>, such as a VKN
    /// entered without the <c>TR</c> prefix.
    /// </summary>
    /// <param name="country">The country; <c>GR</c> means the <c>EL</c> prefix.</param>
    /// <param name="number">The number, with or without the country prefix.</param>
    /// <returns>The VAT number, or the reason it is invalid.</returns>
    public static Result<VatNumber> Create(CountryCode country, string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string prefix = country.Value == "GR" ? "EL" : country.Value;
        string compact = Text.Compact(number, " -.,/");
        return compact.StartsWith(prefix, StringComparison.Ordinal) || (prefix == "EL" && compact.StartsWith("GR", StringComparison.Ordinal))
            ? Create(compact)
            : Create(prefix == "CH" ? "CHE" + compact.TrimStart('E') : prefix + compact);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid VAT number with its prefix.</summary>
    /// <param name="value">The VAT number to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create(string?)"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static VatNumber Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<VatNumber>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out VatNumber result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The VAT number with its prefix.</returns>
    public override string ToString() => Value;

    private static Result<VatNumber> Check(string prefix, string number)
    {
        if (!VatRules.ByPrefix.TryGetValue(prefix, out Func<string, VatCheck>? rule))
        {
            return ValidationMessages.VatNumberUnsupportedCountry.ToError(ErrorType.Validation, prefix);
        }

        number = Expand(prefix, number);
        return rule(number) switch
        {
            VatCheck.Valid => new VatNumber(prefix, number),
            VatCheck.InvalidCheckDigit => ValidationMessages.VatNumberInvalidCheckDigit.ToError(ErrorType.Validation, prefix),
            _ => ValidationMessages.VatNumberInvalidFormat.ToError(ErrorType.Validation, prefix),
        };
    }

    // Older short forms that are still in circulation, expanded to the canonical length.
    private static string Expand(string prefix, string number)
    {
        if (prefix == "BE" && number.Length == 9 && Text.AllDigits(number))
        {
            return "0" + number;
        }

        int b = number.IndexOf('B', StringComparison.Ordinal);
        if (prefix == "NL" && b is > 0 and < 9 && number.Length == b + 3 && Text.AllDigits(number.AsSpan(0, b)))
        {
            return number[..b].PadLeft(9, '0') + number[b..];
        }

        return number;
    }
}
