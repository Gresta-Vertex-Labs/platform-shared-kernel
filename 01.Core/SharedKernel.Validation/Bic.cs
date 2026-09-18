using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A Business Identifier Code (ISO 9362, SWIFT code) of 8 or 11 characters, such as
/// <c>DEUTDEFF</c> or <c>DEUTDEFF500</c>.
/// </summary>
/// <remarks>
/// The layout is a 4-letter bank code, a 2-letter ISO 3166-1 country code, a 2-character location
/// code and an optional 3-character branch code. Spaces are removed and letters upper-cased. An
/// 8-character BIC and the same BIC with branch <c>XXX</c> address the same head office but are
/// kept as written, so they are not equal.
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<Bic>))]
public readonly record struct Bic : IValidatedValue<Bic>
{
    private readonly string? _value;

    private Bic(string value) => _value = value;

    /// <summary>Gets the BIC: 8 or 11 upper-case characters.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the 4-letter bank code.</summary>
    public string BankCode => Value.Length >= 4 ? Value[..4] : string.Empty;

    /// <summary>Gets the country.</summary>
    public CountryCode CountryCode => CountryCode.FromTrusted(Value.Length >= 6 ? Value[4..6] : string.Empty);

    /// <summary>Gets the 2-character location code.</summary>
    public string LocationCode => Value.Length >= 8 ? Value[6..8] : string.Empty;

    /// <summary>Gets the 3-character branch code, or <see langword="null"/> for an 8-character BIC.</summary>
    public string? BranchCode => Value.Length == 11 ? Value[8..] : null;

    /// <summary>Gets whether this BIC addresses the head office: 8 characters, or branch <c>XXX</c>.</summary>
    public bool IsHeadOffice => Value.Length == 8 || BranchCode == "XXX";

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The BIC, in any case, with or without spaces.</param>
    /// <returns>The BIC, or the reason it is invalid.</returns>
    public static Result<Bic> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string bic = Text.Compact(value, " ");
        if (bic.Length is not (8 or 11)
            || !Text.AllUpperLetters(bic.AsSpan(0, 6))
            || !Text.AllUpperAlphanumeric(bic.AsSpan(6)))
        {
            return ValidationMessages.BicInvalidFormat.ToError(ErrorType.Validation);
        }

        string country = bic[4..6];
        return IsoData.Countries.Contains(country)
            ? new Bic(bic)
            : ValidationMessages.BicUnknownCountry.ToError(ErrorType.Validation, country);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid BIC.</summary>
    /// <param name="value">The BIC to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static Bic Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<Bic>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Bic result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The BIC.</returns>
    public override string ToString() => Value;
}
