using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// An International Bank Account Number (ISO 13616), validated against the SWIFT IBAN registry and
/// stored in electronic form: upper case, no spaces, such as <c>DE89370400440532013000</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Create(string?)"/> checks, in order: the general shape; that the country is in the
/// registry (89 countries, <see cref="RegistryRelease"/>); the country's exact length; the country's
/// national account number (BBAN) structure, for example 18 digits for Germany or 5 digits, a
/// reserved 0 and 16 letters or digits for Türkiye; and the MOD 97-10 check digits. The BBAN check
/// catches typing errors the check digits alone miss, such as a letter typed for a digit.
/// </para>
/// <para>
/// Input may contain spaces, hyphens and lower-case letters: <c>"de89 3704 0044 0532 0130 00"</c>
/// becomes <c>DE89370400440532013000</c>. <see cref="ToPrintString"/> gives the grouped form for
/// display.
/// </para>
/// <para>
/// A valid IBAN is well-formed, not necessarily an open account. Whether the account exists and
/// belongs to the payee is for the bank or a confirmation-of-payee service to answer.
/// </para>
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<Iban>))]
public readonly record struct Iban : IValidatedValue<Iban>
{
    /// <summary>The IBAN registry release the country table reflects.</summary>
    public const string RegistryRelease = IbanRegistry.ReleaseName;

    private const int MaxLength = 34;

    private readonly string? _value;

    private Iban(string value) => _value = value;

    /// <summary>Gets the IBAN in electronic form: upper case, no spaces.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the country: the first two characters.</summary>
    public CountryCode CountryCode => CountryCode.FromTrusted(Value.Length >= 2 ? Value[..2] : string.Empty);

    /// <summary>Gets the two check digits after the country code.</summary>
    public string CheckDigits => Value.Length >= 4 ? Value[2..4] : string.Empty;

    /// <summary>Gets the national account number (BBAN): everything after the check digits.</summary>
    public string Bban => Value.Length > 4 ? Value[4..] : string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/> against the IBAN registry.</summary>
    /// <param name="value">The IBAN, with or without spaces and hyphens, in any case.</param>
    /// <returns>The IBAN, or the reason it is invalid.</returns>
    public static Result<Iban> Create(string? value) => Create(value, allowUnregisteredCountry: false);

    /// <summary>
    /// Validates and normalizes <paramref name="value"/>, optionally accepting a country that is not
    /// yet in the registry.
    /// </summary>
    /// <param name="value">The IBAN, with or without spaces and hyphens, in any case.</param>
    /// <param name="allowUnregisteredCountry">
    /// When <see langword="true"/>, an IBAN whose country prefix is a valid ISO 3166-1 code but not in
    /// the registry is checked for the general shape (at most 34 letters and digits) and the check
    /// digits only. Use it when a country may adopt IBANs before this package's next release. It
    /// never weakens the checks for a registered country.
    /// </param>
    /// <returns>The IBAN, or the reason it is invalid.</returns>
    public static Result<Iban> Create(string? value, bool allowUnregisteredCountry)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string iban = Text.Compact(value, " -");
        if (iban.Length is < 5 or > MaxLength
            || !Text.AllUpperLetters(iban.AsSpan(0, 2))
            || !Text.AllDigits(iban.AsSpan(2, 2))
            || !Text.AllUpperAlphanumeric(iban))
        {
            return ValidationMessages.IbanInvalidFormat.ToError(ErrorType.Validation);
        }

        string country = iban[..2];
        if (IbanRegistry.Countries.TryGetValue(country, out IbanCountryFormat? format))
        {
            if (iban.Length != format.Length)
            {
                return ValidationMessages.IbanInvalidLength.ToError(ErrorType.Validation, country, format.Length, iban.Length);
            }

            if (!format.Matches(iban.AsSpan(4)))
            {
                return ValidationMessages.IbanInvalidBban.ToError(ErrorType.Validation, country);
            }
        }
        else if (!allowUnregisteredCountry || !IsoData.Countries.Contains(country))
        {
            return ValidationMessages.IbanUnsupportedCountry.ToError(ErrorType.Validation, country);
        }

        return HasValidCheckDigits(iban)
            ? new Iban(iban)
            : ValidationMessages.IbanInvalidCheckDigits.ToError(ErrorType.Validation);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid IBAN.</summary>
    /// <param name="value">The IBAN to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create(string?)"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <summary>Parses an IBAN, throwing when it is invalid.</summary>
    /// <param name="s">The IBAN.</param>
    /// <param name="provider">Ignored; IBANs are culture-independent.</param>
    /// <returns>The IBAN.</returns>
    /// <exception cref="FormatException"><paramref name="s"/> is not a valid IBAN; the message says why.</exception>
    public static Iban Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<Iban>(s);

    /// <summary>Attempts to parse an IBAN.</summary>
    /// <param name="s">The IBAN.</param>
    /// <param name="provider">Ignored; IBANs are culture-independent.</param>
    /// <param name="result">The IBAN, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is a valid IBAN.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Iban result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns the IBAN in groups of four for display, such as <c>DE89 3704 0044 0532 0130 00</c>.</summary>
    /// <returns>The print form.</returns>
    public string ToPrintString()
    {
        var builder = new StringBuilder(Value.Length + Value.Length / 4);
        for (int i = 0; i < Value.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(Value[i]);
        }

        return builder.ToString();
    }

    /// <summary>Returns <see cref="Value"/>, the electronic form.</summary>
    /// <returns>The IBAN.</returns>
    public override string ToString() => Value;

    // ISO 13616: move the country code and check digits to the end; the result modulo 97 is 1.
    private static bool HasValidCheckDigits(string iban)
    {
        Span<char> rearranged = stackalloc char[iban.Length];
        iban.AsSpan(4).CopyTo(rearranged);
        iban.AsSpan(0, 4).CopyTo(rearranged[(iban.Length - 4)..]);
        return Checksums.Mod97(rearranged) == 1;
    }
}
