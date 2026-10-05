namespace SharedKernel.Validation.Internal;

/// <summary>
/// The check-digit algorithms shared by several identifiers. Every method expects input already
/// checked for the right characters; none of them validates its argument.
/// </summary>
internal static class Checksums
{
    /// <summary>
    /// ISO 7064 MOD 97-10 remainder of <paramref name="value"/>, with letters expanded to two
    /// digits (A = 10 … Z = 35). Folded digit by digit, so any length works without big integers.
    /// </summary>
    public static int Mod97(ReadOnlySpan<char> value)
    {
        int remainder = 0;
        foreach (char c in value)
        {
            remainder = char.IsAsciiDigit(c)
                ? (remainder * 10 + (c - '0')) % 97
                : (remainder * 100 + (c - 'A' + 10)) % 97;
        }

        return remainder;
    }

    /// <summary>
    /// The Luhn sum of <paramref name="digits"/> modulo 10, treating the last digit as the check
    /// digit: 0 means valid.
    /// </summary>
    public static int Luhn(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        bool doubled = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (doubled)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubled = !doubled;
        }

        return sum % 10;
    }

    /// <summary>The Luhn check digit that, appended to <paramref name="digits"/>, makes it valid.</summary>
    public static int LuhnCheckDigit(ReadOnlySpan<char> digits)
    {
        Span<char> withZero = digits.Length < 64 ? stackalloc char[digits.Length + 1] : new char[digits.Length + 1];
        digits.CopyTo(withZero);
        withZero[^1] = '0';
        return (10 - Luhn(withZero)) % 10;
    }

    /// <summary>ISO 7064 MOD 11,10 over all of <paramref name="digits"/>, check digit included: valid when true.</summary>
    public static bool Iso7064Mod11_10(ReadOnlySpan<char> digits)
    {
        int check = 5;
        foreach (char c in digits)
        {
            check = ((check == 0 ? 10 : check) * 2 % 11 + (c - '0')) % 10;
        }

        return check == 1;
    }

    /// <summary>The weighted sum of <paramref name="digits"/>: <c>Σ weights[i] × digits[i]</c> over the shorter of the two.</summary>
    public static int WeightedSum(ReadOnlySpan<char> digits, ReadOnlySpan<int> weights)
    {
        int sum = 0;
        int length = Math.Min(digits.Length, weights.Length);
        for (int i = 0; i < length; i++)
        {
            sum += weights[i] * (digits[i] - '0');
        }

        return sum;
    }

    /// <summary>The integer value of a short run of ASCII digits.</summary>
    public static int ToInt(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char c in digits)
        {
            value = value * 10 + (c - '0');
        }

        return value;
    }

    /// <summary>The integer value of a run of ASCII digits, which may be longer than an <see cref="int"/> holds.</summary>
    public static long ToLong(ReadOnlySpan<char> digits)
    {
        long value = 0;
        foreach (char c in digits)
        {
            value = value * 10 + (c - '0');
        }

        return value;
    }
}
