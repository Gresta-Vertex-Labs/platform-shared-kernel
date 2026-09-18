using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace SharedKernel.Localization;

/// <summary>
/// A parsed message with named placeholders, such as <c>"Order {orderId} was not found."</c> or
/// <c>"Balance: {amount:N2}"</c>, that can be filled with values in a given culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Syntax.</b> A placeholder is <c>{name}</c> or <c>{name:format}</c>. The name starts with a
/// letter or underscore and continues with letters, digits or underscores; it is case-sensitive.
/// The optional format is any .NET format string (<c>N2</c>, <c>C</c>, <c>yyyy-MM-dd</c>, <c>D</c>)
/// and is applied with the culture passed to <see cref="Format"/>, so <c>{amount:N2}</c> renders
/// <c>1,234.50</c> in English and <c>1.234,50</c> in Turkish. Write <c>{{</c> and <c>}}</c> for a
/// literal brace.
/// </para>
/// <para>
/// <b>Positional placeholders such as <c>{0}</c> are rejected.</b> A translator can reorder named
/// placeholders freely, and a name says what the value is; a number does neither.
/// </para>
/// <para>
/// A value that is <see langword="null"/> renders as an empty string. A value that implements
/// <see cref="IFormattable"/> (numbers, dates, <see cref="Guid"/>, enums) is formatted with the
/// placeholder's format and the culture; anything else uses <see cref="object.ToString"/>.
/// </para>
/// <para>Instances are immutable and safe to share between threads.</para>
/// </remarks>
public sealed class MessageTemplate
{
    private readonly Segment[] _segments;

    private MessageTemplate(string text, Segment[] segments, string[] placeholderNames)
    {
        Text = text;
        _segments = segments;
        PlaceholderNames = placeholderNames;
    }

    /// <summary>Gets the template exactly as written, including braces and escapes.</summary>
    public string Text { get; }

    /// <summary>
    /// Gets the distinct placeholder names, in the order they first appear. Empty for a message
    /// without placeholders.
    /// </summary>
    public IReadOnlyList<string> PlaceholderNames { get; }

    /// <summary>Parses <paramref name="text"/> into a template.</summary>
    /// <param name="text">The message text. Must not be null, empty, or whitespace-only.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null, empty, or whitespace-only.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="text"/> has an unclosed or unescaped brace, an empty or positional
    /// placeholder name, or an invalid character in a name. The message gives the position.
    /// </exception>
    public static MessageTemplate Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return TryParseCore(text, out MessageTemplate? template, out string? error)
            ? template
            : throw new FormatException(error);
    }

    /// <summary>Attempts to parse <paramref name="text"/> into a template.</summary>
    /// <param name="text">The message text.</param>
    /// <param name="template">The parsed template when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="text"/> is not blank and is a valid template;
    /// otherwise <see langword="false"/>. Never throws.
    /// </returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out MessageTemplate? template)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            template = null;
            return false;
        }

        return TryParseCore(text, out template, out _);
    }

    /// <summary>Fills the placeholders with <paramref name="arguments"/>.</summary>
    /// <param name="culture">The culture used to format numbers, dates and other formattable values.</param>
    /// <param name="arguments">The values, keyed by placeholder name. Extra entries are ignored.</param>
    /// <returns>The formatted message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> or <paramref name="arguments"/> is null.</exception>
    /// <exception cref="ArgumentException">A placeholder has no entry in <paramref name="arguments"/>.</exception>
    /// <exception cref="FormatException">A placeholder's format is not valid for its value, for example <c>{amount:Q}</c> for a decimal.</exception>
    public string Format(CultureInfo culture, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string name in PlaceholderNames)
        {
            if (!arguments.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"The message \"{Text}\" has a placeholder {{{name}}} but no value was supplied for it.",
                    nameof(arguments));
            }
        }

        return FormatCore(culture, arguments);
    }

    /// <summary>
    /// Attempts to fill the placeholders with <paramref name="arguments"/>. Never throws for
    /// missing values or bad formats, so it is safe on an error path.
    /// </summary>
    /// <param name="culture">The culture used to format numbers, dates and other formattable values.</param>
    /// <param name="arguments">The values, keyed by placeholder name. Extra entries are ignored.</param>
    /// <param name="message">The formatted message when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when a placeholder has no value or a format is not valid for its
    /// value; otherwise <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> or <paramref name="arguments"/> is null.</exception>
    public bool TryFormat(
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        [NotNullWhen(true)] out string? message)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string name in PlaceholderNames)
        {
            if (!arguments.ContainsKey(name))
            {
                message = null;
                return false;
            }
        }

        try
        {
            message = FormatCore(culture, arguments);
            return true;
        }
        catch (FormatException)
        {
            message = null;
            return false;
        }
    }

    /// <summary>Returns <see cref="Text"/>.</summary>
    /// <returns>The template exactly as written.</returns>
    public override string ToString() => Text;

    private string FormatCore(CultureInfo culture, IReadOnlyDictionary<string, object?> arguments)
    {
        if (_segments.Length == 1 && _segments[0].Name is null)
        {
            return _segments[0].Literal!;
        }

        var builder = new StringBuilder(Text.Length + 16);
        foreach (Segment segment in _segments)
        {
            if (segment.Name is null)
            {
                builder.Append(segment.Literal);
                continue;
            }

            object? value = arguments[segment.Name];
            builder.Append(value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(segment.Format, culture),
                _ => value.ToString(),
            });
        }

        return builder.ToString();
    }

    private static bool TryParseCore(
        string text,
        [NotNullWhen(true)] out MessageTemplate? template,
        [NotNullWhen(false)] out string? error)
    {
        var segments = new List<Segment>();
        var names = new List<string>();
        var literal = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }

                return Fail($"a '}}' at position {i} has no matching '{{'. Write '}}}}' for a literal brace.", out template, out error);
            }

            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }

            int start = i;
            int close = text.IndexOf('}', start + 1);
            if (close < 0)
            {
                return Fail($"the '{{' at position {start} is never closed. Write '{{{{' for a literal brace.", out template, out error);
            }

            string body = text.Substring(start + 1, close - start - 1);
            int colon = body.IndexOf(':');
            string name = colon < 0 ? body : body[..colon];
            string? format = colon < 0 ? null : body[(colon + 1)..];

            if (body.Contains('{'))
            {
                return Fail($"the placeholder at position {start} contains a '{{'.", out template, out error);
            }

            if (name.Length == 0)
            {
                return Fail($"the placeholder at position {start} has no name.", out template, out error);
            }

            if (char.IsAsciiDigit(name[0]))
            {
                return Fail(
                    $"the placeholder {{{name}}} at position {start} is positional. Placeholders are named, for example {{orderId}}.",
                    out template,
                    out error);
            }

            if (!IsValidName(name))
            {
                return Fail(
                    $"the placeholder name '{name}' at position {start} is not valid. Use letters, digits and underscores, starting with a letter or underscore.",
                    out template,
                    out error);
            }

            if (format is { Length: 0 })
            {
                return Fail($"the placeholder {{{name}:}} at position {start} has an empty format.", out template, out error);
            }

            if (literal.Length > 0)
            {
                segments.Add(new Segment(literal.ToString(), null, null));
                literal.Clear();
            }

            segments.Add(new Segment(null, name, format));
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            i = close + 1;
        }

        if (literal.Length > 0 || segments.Count == 0)
        {
            segments.Add(new Segment(literal.ToString(), null, null));
        }

        template = new MessageTemplate(text, [.. segments], [.. names]);
        error = null;
        return true;

        static bool Fail(string reason, out MessageTemplate? template, out string? error)
        {
            template = null;
            error = $"The message template is not valid: {reason}";
            return false;
        }
    }

    private static bool IsValidName(string name)
    {
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct Segment(string? Literal, string? Name, string? Format);
}
