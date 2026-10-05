using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>A United States ABA bank routing transit number: 9 digits, such as <c>011000015</c>.</summary>
/// <remarks>
/// Checked for the Federal Reserve prefix ranges (00–12 banks and the US government, 21–32 thrift
/// institutions, 61–72 electronic transactions, 80 traveler's checks) and the (3, 7, 1)-weighted
/// check digit. <c>000000000</c> passes the check-digit sum but is not a routing number, and is
/// rejected. Spaces and hyphens are removed.
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<AbaRoutingNumber>))]
public readonly record struct AbaRoutingNumber : IValidatedValue<AbaRoutingNumber>
{
    private readonly string? _value;

    private AbaRoutingNumber(string value) => _value = value;

    /// <summary>Gets the 9-digit routing number.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The routing number.</param>
    /// <returns>The routing number, or the reason it is invalid.</returns>
    public static Result<AbaRoutingNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string number = Text.Compact(value, " -");
        if (number.Length != 9 || !Text.AllDigits(number))
        {
            return ValidationMessages.AbaRoutingNumberInvalidFormat.ToError(ErrorType.Validation);
        }

        int prefix = Checksums.ToInt(number.AsSpan(0, 2));
        if (prefix is not (<= 12 or (>= 21 and <= 32) or (>= 61 and <= 72) or 80))
        {
            return ValidationMessages.AbaRoutingNumberInvalidPrefix.ToError(ErrorType.Validation);
        }

        bool checkDigitOk = number != "000000000" && Checksums.WeightedSum(number, [3, 7, 1, 3, 7, 1, 3, 7, 1]) % 10 == 0;
        return checkDigitOk
            ? new AbaRoutingNumber(number)
            : ValidationMessages.AbaRoutingNumberInvalidCheckDigit.ToError(ErrorType.Validation);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid routing number.</summary>
    /// <param name="value">The routing number to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static AbaRoutingNumber Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<AbaRoutingNumber>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AbaRoutingNumber result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The routing number.</returns>
    public override string ToString() => Value;
}
