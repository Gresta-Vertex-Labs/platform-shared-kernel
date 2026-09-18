namespace SharedKernel.Validation.Internal;

/// <summary>Normalization and character-class helpers shared by the identifier types.</summary>
internal static class Text
{
    /// <summary>
    /// Removes every character in <paramref name="separators"/> and surrounding whitespace, and
    /// upper-cases ASCII letters. Returns an empty string for input with nothing else in it.
    /// </summary>
    public static string Compact(string value, ReadOnlySpan<char> separators)
    {
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        int written = 0;
        foreach (char c in value.AsSpan().Trim())
        {
            if (separators.Contains(c))
            {
                continue;
            }

            buffer[written++] = char.IsAsciiLetterLower(c) ? (char)(c - 32) : c;
        }

        return new string(buffer[..written]);
    }

    public static bool AllDigits(ReadOnlySpan<char> value) => value.Length > 0 && !value.ContainsAnyExceptInRange('0', '9');

    public static bool AllUpperLetters(ReadOnlySpan<char> value) => value.Length > 0 && !value.ContainsAnyExceptInRange('A', 'Z');

    public static bool AllUpperAlphanumeric(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterUpper(c))
            {
                return false;
            }
        }

        return true;
    }
}
