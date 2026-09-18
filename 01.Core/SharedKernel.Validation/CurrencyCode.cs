using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>An active ISO 4217 currency code in upper case, such as <c>TRY</c> or <c>EUR</c>.</summary>
/// <remarks>
/// <para>
/// Accepts the same codes as <c>SharedKernel.Domain</c>'s <c>CurrencyCatalog</c>: every active
/// transactional currency. Fund codes, special drawing rights and precious metals are rejected,
/// and so are withdrawn codes such as <c>ANG</c> (now <c>XCG</c>) and <c>ZWL</c> (now <c>ZWG</c>).
/// </para>
/// <para>
/// This type checks a code at the edge of the system. For amounts and minor units, use the domain's
/// <c>Money</c> and <c>Currency</c>.
/// </para>
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<CurrencyCode>))]
public readonly record struct CurrencyCode : IValidatedValue<CurrencyCode>
{
    private readonly string? _value;

    private CurrencyCode(string value) => _value = value;

    /// <summary>Gets the three-letter code.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The code, in any case, with surrounding whitespace allowed.</param>
    /// <returns>The currency code, or the reason it is invalid.</returns>
    public static Result<CurrencyCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string code = Text.Compact(value, []);
        if (code.Length != 3 || !Text.AllUpperLetters(code))
        {
            return ValidationMessages.CurrencyCodeInvalidFormat.ToError(ErrorType.Validation);
        }

        return IsoData.Currencies.Contains(code)
            ? new CurrencyCode(code)
            : ValidationMessages.CurrencyCodeUnknown.ToError(ErrorType.Validation, code);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid currency code.</summary>
    /// <param name="value">The code to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static CurrencyCode Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<CurrencyCode>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CurrencyCode result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The three-letter code.</returns>
    public override string ToString() => Value;
}
