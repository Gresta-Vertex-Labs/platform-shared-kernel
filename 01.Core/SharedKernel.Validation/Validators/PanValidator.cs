using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent payment-card PAN (Primary Account Number) validator: Luhn (mod-10)
/// checksum validation plus card-network detection from the leading digits (BIN/IIN range).
/// </summary>
public static class PanValidator
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> passes the Luhn checksum.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>
    /// Validates <paramref name="value"/> as a PAN: digits only (spaces/hyphens are stripped
    /// before validation), 12-19 digits in length, and passing the Luhn checksum.
    /// </summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Pan.FailedLuhnCheck, "PAN must not be null or empty.");
        }

        string digits = StripSeparators(value);

        if (digits.Length is < 12 or > 19 || !IsAllDigits(digits))
        {
            return Error.Validation(ValidationErrorCodes.Pan.FailedLuhnCheck,
                "PAN must contain 12-19 digits (spaces and hyphens are ignored).");
        }

        return PassesLuhnCheck(digits)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Pan.FailedLuhnCheck, "PAN failed the Luhn (mod-10) checksum.");
    }

    /// <summary>
    /// Detects the card network from <paramref name="value"/>'s leading digits (BIN/IIN range).
    /// Returns <see cref="CardNetwork.Unknown"/> for an unrecognized or malformed value — this
    /// method never throws and does not require <paramref name="value"/> to pass <see cref="Validate"/>.
    /// </summary>
    public static CardNetwork DetectNetwork(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CardNetwork.Unknown;
        }

        string digits = StripSeparators(value);

        if (digits.Length == 0 || !IsAllDigits(digits))
        {
            return CardNetwork.Unknown;
        }

        if (digits.Length is 15 && (digits.StartsWith("34", StringComparison.Ordinal) || digits.StartsWith("37", StringComparison.Ordinal)))
        {
            return CardNetwork.Amex;
        }

        if (digits[0] == '4' && digits.Length is 13 or 16 or 19)
        {
            return CardNetwork.Visa;
        }

        if (digits.Length == 16)
        {
            if (TryParsePrefix(digits, 2, out int twoDigitPrefix) && twoDigitPrefix is >= 51 and <= 55)
            {
                return CardNetwork.Mastercard;
            }

            if (TryParsePrefix(digits, 4, out int fourDigitPrefix) && fourDigitPrefix is >= 2221 and <= 2720)
            {
                return CardNetwork.Mastercard;
            }

            if (digits.StartsWith("6011", StringComparison.Ordinal) || digits.StartsWith("65", StringComparison.Ordinal))
            {
                return CardNetwork.Discover;
            }

            if (TryParsePrefix(digits, 3, out int threeDigitPrefix) && threeDigitPrefix is >= 644 and <= 649)
            {
                return CardNetwork.Discover;
            }

            if (TryParsePrefix(digits, 6, out int sixDigitPrefix) && sixDigitPrefix is >= 622126 and <= 622925)
            {
                return CardNetwork.Discover;
            }
        }

        return CardNetwork.Unknown;
    }

    private static bool TryParsePrefix(string digits, int length, out int value) =>
        int.TryParse(digits.AsSpan(0, length), out value);

    private static string StripSeparators(string value)
    {
        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        int written = 0;

        foreach (char c in value)
        {
            if (c is ' ' or '-')
            {
                continue;
            }

            buffer[written++] = c;
        }

        return new string(buffer[..written]);
    }

    private static bool IsAllDigits(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    // Luhn (mod-10) algorithm: from the rightmost digit, double every second digit; if a
    // doubled digit exceeds 9, subtract 9. The PAN is valid when the total digit sum is a
    // multiple of 10.
    private static bool PassesLuhnCheck(string digits)
    {
        int sum = 0;
        bool doubleDigit = false;

        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
