using System.Text;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="string"/>.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Converts an identifier string to <c>snake_case</c>.
    /// </summary>
    /// <remarks>
    /// Handles PascalCase, camelCase, and strings that already contain underscores.
    /// Consecutive uppercase letters (e.g., <c>"HTMLParser"</c>) are treated as a single word:
    /// <c>"html_parser"</c>.
    /// </remarks>
    /// <param name="value">The string to convert.</param>
    /// <returns>The snake_case representation, or <see cref="string.Empty"/> if the input is empty.</returns>
    public static string ToSnakeCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length + 8);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (char.IsUpper(c))
            {
                bool isPrecededByLower = i > 0 && char.IsLower(value[i - 1]);
                bool isFollowedByLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (i > 0 && (isPrecededByLower || isFollowedByLower))
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts an identifier string to <c>camelCase</c>.
    /// </summary>
    /// <remarks>
    /// If the string starts with uppercase letters, they are lowercased until the first
    /// lowercase letter or word boundary is reached.
    /// </remarks>
    /// <param name="value">The string to convert.</param>
    /// <returns>The camelCase representation, or <see cref="string.Empty"/> if the input is empty.</returns>
    public static string ToCamelCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var pascal = value.ToPascalCase();
        if (pascal.Length == 0)
            return pascal;

        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>
    /// Converts an identifier string to <c>PascalCase</c>.
    /// </summary>
    /// <remarks>
    /// Word boundaries are detected at underscores, hyphens, spaces, and transitions from
    /// lowercase to uppercase.
    /// </remarks>
    /// <param name="value">The string to convert.</param>
    /// <returns>The PascalCase representation, or <see cref="string.Empty"/> if the input is empty.</returns>
    public static string ToPascalCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length);
        bool capitalizeNext = true;

        foreach (char c in value)
        {
            if (c is '_' or '-' or ' ')
            {
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
            capitalizeNext = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="value"/> is <c>null</c>, empty, or consists
    /// only of white-space characters.
    /// </summary>
    /// <param name="value">The string to test.</param>
    public static bool IsNullOrWhiteSpace(this string? value)
        => string.IsNullOrWhiteSpace(value);
}
