using System.Text.Json;

namespace SharedKernel.Localization.Internal;

/// <summary>
/// Reads a translation file: a JSON object whose string values are message templates, with nested
/// objects joined into dotted codes. Uses <see cref="JsonDocument"/>, so no reflection is involved.
/// </summary>
internal static class JsonTranslationReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static List<(string Code, MessageTemplate Template)> Read(Stream utf8Json, string source)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{source} is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{source} must contain a JSON object of message codes and templates.");
            }

            var entries = new List<(string, MessageTemplate)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ReadObject(document.RootElement, prefix: null, source, entries, seen);
            return entries;
        }
    }

    private static void ReadObject(
        JsonElement element,
        string? prefix,
        string source,
        List<(string, MessageTemplate)> entries,
        HashSet<string> seen)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new FormatException($"{source} has an empty key{(prefix is null ? string.Empty : $" under '{prefix}'")}.");
            }

            string code = prefix is null ? property.Name : $"{prefix}.{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    ReadObject(property.Value, code, source, entries, seen);
                    break;

                case JsonValueKind.String:
                    if (!seen.Add(code))
                    {
                        throw new FormatException($"{source} defines '{code}' more than once.");
                    }

                    string text = property.Value.GetString()!;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new FormatException($"{source} has an empty translation for '{code}'. Remove the entry to fall back to the default text.");
                    }

                    try
                    {
                        entries.Add((code, MessageTemplate.Parse(text)));
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException($"{source}, '{code}': {ex.Message}", ex);
                    }

                    break;

                default:
                    throw new FormatException(
                        $"{source}, '{code}': expected a string or an object, found {property.Value.ValueKind}.");
            }
        }
    }
}
