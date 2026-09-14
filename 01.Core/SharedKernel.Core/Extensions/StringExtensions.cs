using System.Text;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Casing conversions for identifier strings.
/// </summary>
/// <remarks>
/// <para>
/// Every conversion splits the input into words the same way, then joins them in the target style. A word
/// boundary is an underscore, a hyphen, or whitespace; a lowercase letter or digit followed by an uppercase
/// letter (<c>myName</c>, <c>Order2Line</c>); or the last capital of an acronym followed by a lowercase
/// letter (<c>HTMLParser</c> becomes <c>HTML</c> + <c>Parser</c>). Separators at either end are dropped.
/// </para>
/// <para>
/// Letters are changed with invariant-culture rules, so the result is the same on every server.
/// </para>
/// </remarks>
public static class StringExtensions
{
    /// <summary>Converts an identifier to <c>snake_case</c>: <c>HTMLParser</c> becomes <c>html_parser</c>.</summary>
    /// <param name="value">The identifier to convert.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToSnakeCase(this string value) => JoinLower(value, '_');

    /// <summary>Converts an identifier to <c>kebab-case</c>: <c>HTMLParser</c> becomes <c>html-parser</c>.</summary>
    /// <param name="value">The identifier to convert.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToKebabCase(this string value) => JoinLower(value, '-');

    /// <summary>
    /// Converts an identifier to <c>PascalCase</c>: <c>html_parser</c> and <c>HTMLParser</c> both become
    /// <c>HtmlParser</c>.
    /// </summary>
    /// <param name="value">The identifier to convert.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToPascalCase(this string value) => JoinCapitalized(value, lowerFirstWord: false);

    /// <summary>
    /// Converts an identifier to <c>camelCase</c>: <c>html_parser</c> and <c>HTMLParser</c> both become
    /// <c>htmlParser</c>.
    /// </summary>
    /// <param name="value">The identifier to convert.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToCamelCase(this string value) => JoinCapitalized(value, lowerFirstWord: true);

    private static string JoinLower(string value, char separator)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 8);
        foreach (var (start, length) in Words(value))
        {
            if (builder.Length > 0)
                builder.Append(separator);

            for (var i = start; i < start + length; i++)
                builder.Append(char.ToLowerInvariant(value[i]));
        }

        return builder.ToString();
    }

    private static string JoinCapitalized(string value, bool lowerFirstWord)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var (start, length) in Words(value))
        {
            var capitalize = !(lowerFirstWord && builder.Length == 0);
            builder.Append(capitalize ? char.ToUpperInvariant(value[start]) : char.ToLowerInvariant(value[start]));

            for (var i = start + 1; i < start + length; i++)
                builder.Append(char.ToLowerInvariant(value[i]));
        }

        return builder.ToString();
    }

    private static List<(int Start, int Length)> Words(string value)
    {
        var words = new List<(int Start, int Length)>();
        var start = -1;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                if (start >= 0)
                    words.Add((start, i - start));

                start = -1;
                continue;
            }

            if (start < 0)
            {
                start = i;
                continue;
            }

            if (char.IsUpper(c) && IsBoundaryBeforeUpper(value, i))
            {
                words.Add((start, i - start));
                start = i;
            }
        }

        if (start >= 0)
            words.Add((start, value.Length - start));

        return words;
    }

    private static bool IsBoundaryBeforeUpper(string value, int index)
    {
        var previous = value[index - 1];
        if (char.IsLower(previous) || char.IsDigit(previous))
            return true;

        // The last capital of an acronym starts the next word: "HTMLParser" -> "HTML" + "Parser".
        return char.IsUpper(previous) && index + 1 < value.Length && char.IsLower(value[index + 1]);
    }
}
