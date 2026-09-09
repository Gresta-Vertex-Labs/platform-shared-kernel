using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// US ABA bank routing transit number format validator.
/// </summary>
/// <remarks>
/// A routing number is exactly 9 digits. It is validated via the published, position-weighted
/// (3, 7, 1)-repeating checksum: for digits <c>d1..d9</c>,
/// <c>3*(d1+d4+d7) + 7*(d2+d5+d8) + (d3+d6+d9)</c> must be congruent to 0 modulo 10. The weights
/// 3, 7, and 1 are each coprime to 10, which lets the checksum detect every single-digit error
/// and most adjacent-digit transpositions.
/// </remarks>
public static class AbaRoutingNumberValidator
{
    private static readonly int[] Weights = [3, 7, 1, 3, 7, 1, 3, 7, 1];

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed, checksum-valid ABA routing number.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as a 9-digit US ABA routing number.</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat,
                "ABA routing number must not be null or empty.");
        }

        string normalized = value.Trim();

        if (normalized.Length != 9)
        {
            return Error.Validation(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat,
                "ABA routing number must be exactly 9 digits.");
        }

        foreach (char c in normalized)
        {
            if (!char.IsAsciiDigit(c))
            {
                return Error.Validation(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat,
                    "ABA routing number must contain only digits.");
            }
        }

        return ChecksumPasses(normalized)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.AbaRoutingNumber.FailedChecksum,
                "ABA routing number failed the (3, 7, 1)-weighted checksum validation.");
    }

    // Published (3, 7, 1)-repeating-weight checksum:
    // 3*(d1+d4+d7) + 7*(d2+d5+d8) + (d3+d6+d9) ≡ 0 (mod 10).
    private static bool ChecksumPasses(string routingNumber)
    {
        int sum = 0;

        for (int i = 0; i < 9; i++)
        {
            sum += (routingNumber[i] - '0') * Weights[i];
        }

        return sum % 10 == 0;
    }
}
