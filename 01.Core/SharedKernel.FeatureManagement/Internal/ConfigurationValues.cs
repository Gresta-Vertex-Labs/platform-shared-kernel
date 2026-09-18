using System.Globalization;
using Microsoft.Extensions.Configuration;
using OpenFeature.Model;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// Converts a variant's configuration section to an OpenFeature <see cref="Value"/>. Configuration keeps every
/// value as text, so leaves stay strings; typed reading happens later against the target type.
/// </summary>
internal static class ConfigurationValues
{
    private const int MaxDepth = 64;

    public static Value ToValue(IConfigurationSection? section) => ToValue(section, 0);

    private static Value ToValue(IConfigurationSection? section, int depth)
    {
        if (section is null)
        {
            return new Value();
        }

        if (depth > MaxDepth)
        {
            throw new InvalidOperationException("Variant configuration is nested too deeply.");
        }

        List<IConfigurationSection> children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            return section.Value is null ? new Value() : new Value(section.Value);
        }

        if (IsList(children))
        {
            return new Value(children
                .OrderBy(static c => int.Parse(c.Key, NumberStyles.None, CultureInfo.InvariantCulture))
                .Select(c => ToValue(c, depth + 1))
                .ToList());
        }

        var members = new Dictionary<string, Value>(children.Count, StringComparer.OrdinalIgnoreCase);
        foreach (IConfigurationSection child in children)
        {
            members[child.Key] = ToValue(child, depth + 1);
        }

        return new Value(new Structure(members));
    }

    // Configuration represents a JSON array as children keyed "0", "1", ... with no gaps.
    private static bool IsList(List<IConfigurationSection> children)
    {
        var seen = new bool[children.Count];
        foreach (IConfigurationSection child in children)
        {
            if (!int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index >= children.Count
                || seen[index])
            {
                return false;
            }

            seen[index] = true;
        }

        return true;
    }
}

/// <summary>Reads a variant's scalar configuration value as a string, integer or number.</summary>
internal static class VariantValues
{
    public static bool TryGetString(IConfigurationSection? section, out string value, out string? error)
    {
        if (section?.Value is { } text && !section.GetChildren().Any())
        {
            value = text;
            error = null;
            return true;
        }

        value = string.Empty;
        error = section is null || !section.GetChildren().Any()
            ? "it has no configuration_value"
            : "its configuration_value is an object, not a single value";
        return false;
    }

    public static bool TryGetInteger(IConfigurationSection? section, out int value, out string? error)
    {
        if (!TryGetString(section, out string text, out error))
        {
            value = 0;
            return false;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        error = "its configuration_value is not a whole number";
        return false;
    }

    public static bool TryGetDouble(IConfigurationSection? section, out double value, out string? error)
    {
        if (!TryGetString(section, out string text, out error))
        {
            value = 0;
            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
        {
            return true;
        }

        error = "its configuration_value is not a number";
        return false;
    }
}
