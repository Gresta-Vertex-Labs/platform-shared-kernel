namespace SharedKernel.Validation.NationalId;

/// <summary>
/// Validates Turkey's 11-digit T.C. Kimlik No. (TCKN) national identity number checksum.
/// </summary>
/// <remarks>
/// <para>
/// The built-in default national-identity-number validator, given this platform's primary
/// market. Registered automatically at country code <c>"TR"</c> by
/// <see cref="NationalIdValidatorRegistry"/>'s parameterless constructor.
/// </para>
/// <para>
/// Algorithm: for digits d1..d11 (d1 must be non-zero), let
/// <c>oddSum = d1+d3+d5+d7+d9</c> and <c>evenSum = d2+d4+d6+d8</c>. The 10th digit must equal
/// <c>((oddSum * 7) - evenSum) mod 10</c>, and the 11th digit must equal
/// <c>(oddSum + evenSum + d10) mod 10</c>.
/// </para>
/// </remarks>
public sealed class TckNationalIdValidator : INationalIdValidator
{
    /// <inheritdoc />
    public string CountryCode => "TR";

    /// <inheritdoc />
    public bool IsValid(string idNumber)
    {
        ArgumentNullException.ThrowIfNull(idNumber);

        if (idNumber.Length != 11)
        {
            return false;
        }

        Span<int> digits = stackalloc int[11];

        for (int i = 0; i < 11; i++)
        {
            if (!char.IsAsciiDigit(idNumber[i]))
            {
                return false;
            }

            digits[i] = idNumber[i] - '0';
        }

        if (digits[0] == 0)
        {
            return false;
        }

        int oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        int evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        int expectedD10 = Mod10((oddSum * 7) - evenSum);
        if (expectedD10 != digits[9])
        {
            return false;
        }

        int expectedD11 = Mod10(oddSum + evenSum + expectedD10);
        return expectedD11 == digits[10];
    }

    // C#'s % operator returns a negative remainder for a negative dividend — normalize to [0, 9].
    private static int Mod10(int value) => ((value % 10) + 10) % 10;
}
