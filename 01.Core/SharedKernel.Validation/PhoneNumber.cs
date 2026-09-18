using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>A phone number in E.164 international format, such as <c>+905321234567</c>.</summary>
/// <remarks>
/// <para>
/// Input must start with <c>+</c> and the country code. Spaces, hyphens, dots and parentheses are
/// removed, so <c>"+90 (532) 123-45-67"</c> becomes <c>+905321234567</c>. The result is a <c>+</c>
/// and 7 to 15 digits, the first not zero.
/// </para>
/// <para>
/// This is a format check. It does not know each country's numbering plan, so it cannot tell
/// whether a number could be assigned; that needs a numbering-plan library or an SMS verification
/// step. A national number without a country code (<c>0532 123 45 67</c>) is rejected rather than
/// guessed.
/// </para>
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<PhoneNumber>))]
public readonly record struct PhoneNumber : IValidatedValue<PhoneNumber>
{
    private readonly string? _value;

    private PhoneNumber(string value) => _value = value;

    /// <summary>Gets the number in E.164 format: <c>+</c> followed by digits only.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The phone number in international format.</param>
    /// <returns>The phone number, or the reason it is invalid.</returns>
    public static Result<PhoneNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string number = Text.Compact(value, " -.()");
        return number.Length is >= 8 and <= 16 && number[0] == '+' && number[1] != '0' && Text.AllDigits(number.AsSpan(1))
            ? new PhoneNumber(number)
            : ValidationMessages.PhoneNumberInvalidFormat.ToError(ErrorType.Validation);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid E.164 phone number.</summary>
    /// <param name="value">The phone number to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static PhoneNumber Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<PhoneNumber>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PhoneNumber result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The E.164 number.</returns>
    public override string ToString() => Value;
}
