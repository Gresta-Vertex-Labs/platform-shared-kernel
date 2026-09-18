using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation;

/// <summary>
/// Checks a Turkish identity number (T.C. Kimlik Numarası, TCKN): 11 digits, not starting with 0,
/// with two check digits.
/// </summary>
/// <remarks>
/// With the odd-position sum <c>o = d1 + d3 + d5 + d7 + d9</c> and the even-position sum
/// <c>e = d2 + d4 + d6 + d8</c>, the tenth digit is <c>(7o − e) mod 10</c> and the eleventh is
/// <c>(o + e + d10) mod 10</c>. Built into <see cref="NationalIdValidatorRegistry"/>.
/// </remarks>
public sealed class TurkishNationalIdValidator : INationalIdValidator
{
    private static readonly CountryCode Turkiye = CountryCode.Parse("TR", null);

    /// <inheritdoc />
    public CountryCode Country => Turkiye;

    /// <inheritdoc />
    public Result Validate(string number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (number.Length != 11 || number[0] == '0' || number.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            return ValidationMessages.NationalIdInvalidFormat.ToError(ErrorType.Validation, "TR");
        }

        int odd = D(number, 0) + D(number, 2) + D(number, 4) + D(number, 6) + D(number, 8);
        int even = D(number, 1) + D(number, 3) + D(number, 5) + D(number, 7);
        int tenth = ((7 * odd - even) % 10 + 10) % 10;
        int eleventh = (odd + even + tenth) % 10;

        return D(number, 9) == tenth && D(number, 10) == eleventh
            ? Result.Success()
            : ValidationMessages.NationalIdInvalidCheckDigit.ToError(ErrorType.Validation, "TR");
    }

    private static int D(string number, int index) => number[index] - '0';
}
