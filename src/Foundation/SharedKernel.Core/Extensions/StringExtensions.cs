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
/// Letters are changed with invariant-culture rules, so the result is the same on every server (a Turkish
/// culture does not turn <c>I</c> into a dotless <c>ı</c>).
/// </para>
/// <list type="table">
///   <listheader><term>Input</term><description>snake / kebab / camel / Pascal</description></listheader>
///   <item><term><c>OrderLineItem</c></term><description><c>order_line_item</c> / <c>order-line-item</c> / <c>orderLineItem</c> / <c>OrderLineItem</c></description></item>
///   <item><term><c>HTMLParser</c></term><description><c>html_parser</c> / <c>html-parser</c> / <c>htmlParser</c> / <c>HtmlParser</c></description></item>
///   <item><term><c>user-id</c></term><description><c>user_id</c> / <c>user-id</c> / <c>userId</c> / <c>UserId</c></description></item>
///   <item><term><c>Order2Line</c></term><description><c>order2_line</c> / <c>order2-line</c> / <c>order2Line</c> / <c>Order2Line</c></description></item>
/// </list>
/// <para>
/// Acronyms are not preserved: <c>HTMLParser</c> converts to <c>HtmlParser</c> in Pascal case. These methods are
/// meant for identifiers such as column, key, or route names, not for arbitrary prose.
/// </para>
/// </remarks>
public static class StringExtensions
{
    /// <summary>Converts an identifier to <c>snake_case</c>: <c>HTMLParser</c> becomes <c>html_parser</c>.</summary>
    /// <param name="value">The identifier to convert.</param>
    /// <returns>The words of <paramref name="value"/> in lower case, joined by underscores; empty when it has no words.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToSnakeCase(this string value) => JoinLower(value, '_');

    /// <summary>Converts an identifier to <c>kebab-case</c>: <c>HTMLParser</c> becomes <c>html-parser</c>.</summary>
    /// <param name="value">The identifier to convert.</param>
    /// <returns>The words of <paramref name="value"/> in lower case, joined by hyphens; empty when it has no words.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToKebabCase(this string value) => JoinLower(value, '-');

    /// <summary>
    /// Converts an identifier to <c>PascalCase</c>: <c>html_parser</c> and <c>HTMLParser</c> both become
    /// <c>HtmlParser</c>.
    /// </summary>
    /// <param name="value">The identifier to convert.</param>
    /// <returns>The words of <paramref name="value"/>, each capitalized, joined without separators; empty when it has no words.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string ToPascalCase(this string value) => JoinCapitalized(value, lowerFirstWord: false);

    /// <summary>
    /// Converts an identifier to <c>camelCase</c>: <c>html_parser</c> and <c>HTMLParser</c> both become
    /// <c>htmlParser</c>.
    /// </summary>
    /// <param name="value">The identifier to convert.</param>
    /// <returns>The words of <paramref name="value"/> joined without separators, the first in lower case and the rest capitalized; empty when it has no words.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
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
