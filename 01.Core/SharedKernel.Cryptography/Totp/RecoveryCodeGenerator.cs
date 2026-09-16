using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// The default <see cref="IRecoveryCodeGenerator"/>: 10 characters from the Base32 alphabet (50 bits), split into two
/// groups of five.
/// </summary>
/// <remarks>The Base32 alphabet has no 0, 1, 8 or 9, so O, I and B cannot be mistaken for a digit.</remarks>
public sealed class RecoveryCodeGenerator : IRecoveryCodeGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int GroupLength = 5;

    private readonly ISecureRandomGenerator _random;

    /// <summary>Creates the generator.</summary>
    /// <param name="random">The random source.</param>
    public RecoveryCodeGenerator(ISecureRandomGenerator random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GenerateCodes(int count = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 50);

        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (codes.Count < count)
        {
            string raw = _random.GetString(Alphabet, GroupLength * 2);
            codes.Add($"{raw[..GroupLength]}-{raw[GroupLength..]}");
        }

        return [.. codes];
    }

    /// <summary>
    /// Converts a code as typed into the canonical form to hash and compare: uppercase, without spaces or hyphens.
    /// </summary>
    /// <param name="code">The code as entered.</param>
    /// <returns>The normalized code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var builder = new System.Text.StringBuilder(code.Length);
        foreach (char c in code)
        {
            if (c is not (' ' or '-'))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}
