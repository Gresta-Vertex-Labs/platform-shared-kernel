using System.Globalization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Localization.Internal;

/// <summary>
/// The shared implementation behind every <see cref="LocalizedMessage"/> arity: validates the
/// definition once, then builds errors and formats messages from positional values.
/// </summary>
internal sealed class MessageDefinition
{
    private readonly string[] _argumentNames;

    public MessageDefinition(string code, string defaultTemplate, string[] argumentNames, object?[] defaultValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplate);

        MessageTemplate template;
        try
        {
            template = MessageTemplate.Parse(defaultTemplate);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"The default text of message '{code}' is not valid. {ex.Message}", nameof(defaultTemplate), ex);
        }

        for (int i = 0; i < argumentNames.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(argumentNames[i]))
            {
                throw new ArgumentException($"Argument name {i + 1} of message '{code}' is null or blank.", nameof(argumentNames));
            }
        }

        var declared = new HashSet<string>(argumentNames, StringComparer.Ordinal);
        if (declared.Count != argumentNames.Length)
        {
            throw new ArgumentException($"Message '{code}' declares the same argument name twice.", nameof(argumentNames));
        }

        var used = new HashSet<string>(template.PlaceholderNames, StringComparer.Ordinal);
        if (!declared.SetEquals(used))
        {
            string declaredList = argumentNames.Length == 0 ? "none" : string.Join(", ", argumentNames);
            string usedList = used.Count == 0 ? "none" : string.Join(", ", template.PlaceholderNames);
            throw new ArgumentException(
                $"Message '{code}' declares the arguments [{declaredList}] but its default text \"{defaultTemplate}\" uses the placeholders [{usedList}]. They must match exactly.",
                nameof(argumentNames));
        }

        Code = code;
        DefaultTemplate = template;
        _argumentNames = argumentNames;

        // Formatting default values of each argument type proves every format suits its type
        // ({amount:Q} for a decimal fails here, when the definition is created, not on an error
        // path in production). Reference-type defaults are null and render empty, so only value
        // types are checked, which is where format strings matter.
        try
        {
            template.Format(CultureInfo.InvariantCulture, ToArguments(defaultValues));
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                $"A placeholder format in message '{code}' does not suit its argument type. {ex.Message}",
                nameof(defaultTemplate),
                ex);
        }
    }

    public string Code { get; }

    public MessageTemplate DefaultTemplate { get; }

    public Error ToError(ErrorType type, object?[] values)
    {
        if (type == ErrorType.None || !Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "An error needs a defined type other than ErrorType.None.");
        }

        IReadOnlyDictionary<string, object?> arguments = ToArguments(values);
        string message = DefaultTemplate.Format(CultureInfo.InvariantCulture, arguments);

        return new Error(Code, message, type) { MessageArguments = arguments };
    }

    public string Format(ILocalizationCatalog? catalog, CultureInfo culture, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(culture);

        IReadOnlyDictionary<string, object?> arguments = ToArguments(values);

        return catalog is not null && catalog.TryFormat(Code, culture, arguments, out string? translated)
            ? translated
            : DefaultTemplate.Format(culture, arguments);
    }

    private IReadOnlyDictionary<string, object?> ToArguments(object?[] values)
    {
        var arguments = new Dictionary<string, object?>(_argumentNames.Length, StringComparer.Ordinal);
        for (int i = 0; i < _argumentNames.Length; i++)
        {
            arguments[_argumentNames[i]] = values[i];
        }

        return arguments;
    }
}
