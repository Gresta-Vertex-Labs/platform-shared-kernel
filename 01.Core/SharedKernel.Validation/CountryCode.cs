using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>An ISO 3166-1 alpha-2 country code in upper case, such as <c>TR</c> or <c>DE</c>.</summary>
/// <remarks>
/// Accepts the officially assigned codes plus <c>XK</c> (Kosovo), which ISO lists as user-assigned
/// but the IBAN registry, SWIFT and the European Commission use. Input is trimmed and upper-cased.
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<CountryCode>))]
public readonly record struct CountryCode : IValidatedValue<CountryCode>
{
    private readonly string? _value;

    private CountryCode(string value) => _value = value;

    /// <summary>Gets the two-letter code.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The code, in any case, with surrounding whitespace allowed.</param>
    /// <returns>The country code, or the reason it is invalid.</returns>
    public static Result<CountryCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string code = Text.Compact(value, []);
        if (code.Length != 2 || !Text.AllUpperLetters(code))
        {
            return ValidationMessages.CountryCodeInvalidFormat.ToError(ErrorType.Validation);
        }

        return IsoData.Countries.Contains(code)
            ? new CountryCode(code)
            : ValidationMessages.CountryCodeUnknown.ToError(ErrorType.Validation, code);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid country code.</summary>
    /// <param name="value">The code to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static CountryCode Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<CountryCode>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CountryCode result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The two-letter code.</returns>
    public override string ToString() => Value;

    // For a country code taken from an identifier that already validated it.
    internal static CountryCode FromTrusted(string code) => new(code);
}
