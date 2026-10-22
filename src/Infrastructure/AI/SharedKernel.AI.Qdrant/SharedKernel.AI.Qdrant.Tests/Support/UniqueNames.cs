namespace SharedKernel.AI.Qdrant.Tests.Support;

/// <summary>
/// Unique collection names for tests whose leak checks forbid substrings. A GUID in hex form can contain any digit run
/// (Qdrant's gRPC port "6334", for one), so the leak assertions would fail at random; these names are unique but carry
/// letters only, which cannot collide with a numeric, dotted or hyphenated forbidden token.
/// </summary>
internal static class UniqueNames
{
    /// <summary>Returns <c>{prefix}-{32 letters}</c>, unique per call.</summary>
    public static string Of(string prefix)
    {
        Span<char> letters = stackalloc char[32];
        var hex = Guid.NewGuid().ToString("N");
        for (var i = 0; i < letters.Length; i++)
        {
            // 0-9 map to g-p, a-f stay: sixteen distinct letters, no digit survives.
            letters[i] = hex[i] is >= '0' and <= '9' ? (char)('g' + (hex[i] - '0')) : hex[i];
        }

        return $"{prefix}-{new string(letters)}";
    }
}
