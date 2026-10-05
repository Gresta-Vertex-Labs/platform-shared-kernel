namespace SharedKernel.Validation.Internal;

/// <summary>
/// Issuer identification ranges per card network. Detection takes the range with the longest
/// matching prefix whose network also allows the card's length, so a more specific range
/// (Maestro 6759) wins over a broader one.
/// </summary>
internal static class CardNetworkTable
{
    private static readonly Range[] Ranges =
    [
        new(CardNetwork.Visa, "4", "4", [13, 16, 19]),
        new(CardNetwork.Mastercard, "51", "55", [16]),
        new(CardNetwork.Mastercard, "2221", "2720", [16]),
        new(CardNetwork.AmericanExpress, "34", "34", [15]),
        new(CardNetwork.AmericanExpress, "37", "37", [15]),
        new(CardNetwork.Discover, "6011", "6011", Lengths(16, 19)),
        new(CardNetwork.Discover, "644", "649", Lengths(16, 19)),
        new(CardNetwork.Discover, "65", "65", Lengths(16, 19)),
        new(CardNetwork.Jcb, "3528", "3589", Lengths(16, 19)),
        new(CardNetwork.UnionPay, "62", "62", Lengths(16, 19)),
        new(CardNetwork.DinersClub, "30", "30", Lengths(14, 19)),
        new(CardNetwork.DinersClub, "36", "36", Lengths(14, 19)),
        new(CardNetwork.DinersClub, "38", "39", Lengths(14, 19)),
        new(CardNetwork.Maestro, "5018", "5018", Lengths(12, 19)),
        new(CardNetwork.Maestro, "5020", "5020", Lengths(12, 19)),
        new(CardNetwork.Maestro, "5038", "5038", Lengths(12, 19)),
        new(CardNetwork.Maestro, "5893", "5893", Lengths(12, 19)),
        new(CardNetwork.Maestro, "6304", "6304", Lengths(12, 19)),
        new(CardNetwork.Maestro, "6759", "6759", Lengths(12, 19)),
        new(CardNetwork.Maestro, "6761", "6763", Lengths(12, 19)),
        new(CardNetwork.Mir, "2200", "2204", Lengths(16, 19)),
        new(CardNetwork.Troy, "9792", "9792", [16]),
    ];

    public static CardNetwork Detect(string digits)
    {
        CardNetwork best = CardNetwork.Unknown;
        int bestPrefixLength = 0;

        foreach (Range range in Ranges)
        {
            int prefixLength = range.Low.Length;
            if (prefixLength <= bestPrefixLength || digits.Length < prefixLength || !range.Lengths.Contains(digits.Length))
            {
                continue;
            }

            ReadOnlySpan<char> prefix = digits.AsSpan(0, prefixLength);
            if (prefix.CompareTo(range.Low, StringComparison.Ordinal) >= 0 && prefix.CompareTo(range.High, StringComparison.Ordinal) <= 0)
            {
                best = range.Network;
                bestPrefixLength = prefixLength;
            }
        }

        return best;
    }

    private static int[] Lengths(int from, int to) => [.. Enumerable.Range(from, to - from + 1)];

    // Low and High have the same number of digits, so an ordinal comparison of equal-length digit
    // strings is a numeric comparison.
    private sealed record Range(CardNetwork Network, string Low, string High, int[] Lengths);
}
