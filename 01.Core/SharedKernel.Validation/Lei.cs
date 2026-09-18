using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A Legal Entity Identifier (ISO 17442): 18 letters or digits followed by 2 check digits, such as
/// <c>5493001KJTIIGC8Y1R12</c>.
/// </summary>
/// <remarks>
/// Checked with ISO 7064 MOD 97-10 over the whole code. A valid LEI is well-formed; whether it is
/// issued and current is answered by the GLEIF registry.
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<Lei>))]
public readonly record struct Lei : IValidatedValue<Lei>
{
    private readonly string? _value;

    private Lei(string value) => _value = value;

    /// <summary>Gets the 20-character LEI in upper case.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The LEI, in any case, with or without spaces.</param>
    /// <returns>The LEI, or the reason it is invalid.</returns>
    public static Result<Lei> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string lei = Text.Compact(value, " ");
        if (lei.Length != 20 || !Text.AllUpperAlphanumeric(lei.AsSpan(0, 18)) || !Text.AllDigits(lei.AsSpan(18)))
        {
            return ValidationMessages.LeiInvalidFormat.ToError(ErrorType.Validation);
        }

        return Checksums.Mod97(lei) == 1
            ? new Lei(lei)
            : ValidationMessages.LeiInvalidCheckDigits.ToError(ErrorType.Validation);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid LEI.</summary>
    /// <param name="value">The LEI to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static Lei Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<Lei>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Lei result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The LEI.</returns>
    public override string ToString() => Value;
}
