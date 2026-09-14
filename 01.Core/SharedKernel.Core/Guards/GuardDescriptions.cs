using System.Text;

namespace SharedKernel.Guards;

/// <summary>
/// Message templates for every guard violation, parsed once. Always formatted with
/// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> so an error message is identical on
/// every server, whatever its culture.
/// </summary>
internal static class GuardDescriptions
{
    // ── Null / empty ─────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat Null = CompositeFormat.Parse("'{0}' must not be null.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat NullOrEmpty = CompositeFormat.Parse("'{0}' must not be null or empty.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat NullOrWhiteSpace = CompositeFormat.Parse(
        "'{0}' must not be null, empty, or whitespace.");

    // ── String length ────────────────────────────────────────────────────────
    /// <summary>{0} = paramName, {1} = minLength</summary>
    internal static readonly CompositeFormat ShorterThan = CompositeFormat.Parse(
        "'{0}' must be at least {1} character(s) long.");

    /// <summary>{0} = paramName, {1} = maxLength</summary>
    internal static readonly CompositeFormat LongerThan = CompositeFormat.Parse(
        "'{0}' must be at most {1} character(s) long.");

    // ── Numeric / comparison ─────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat NegativeOrZero = CompositeFormat.Parse("'{0}' must be greater than zero.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat Negative = CompositeFormat.Parse("'{0}' must be zero or greater.");

    /// <summary>{0} = paramName, {1} = min, {2} = max</summary>
    internal static readonly CompositeFormat OutOfRange = CompositeFormat.Parse(
        "'{0}' must be between {1} and {2} (inclusive).");

    /// <summary>{0} = paramName, {1} = min</summary>
    internal static readonly CompositeFormat LessThan = CompositeFormat.Parse("'{0}' must be at least {1}.");

    /// <summary>{0} = paramName, {1} = max</summary>
    internal static readonly CompositeFormat GreaterThan = CompositeFormat.Parse("'{0}' must be at most {1}.");

    // ── Value ────────────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat Default = CompositeFormat.Parse(
        "'{0}' must not be the default value for its type.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat InvalidGuid = CompositeFormat.Parse("'{0}' must not be an empty GUID.");

    /// <summary>{0} = paramName, {1} = enum type name</summary>
    internal static readonly CompositeFormat InvalidEnumValue = CompositeFormat.Parse(
        "'{0}' is not a defined {1} value.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat NotUtc = CompositeFormat.Parse("'{0}' must be a UTC date and time.");

    // ── Format / Email ───────────────────────────────────────────────────────
    // The pattern is deliberately not part of the message: it is an implementation detail, and the
    // message can reach an HTTP response.
    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat InvalidFormat = CompositeFormat.Parse(
        "'{0}' is not in the required format.");

    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat Email = CompositeFormat.Parse("'{0}' is not a valid email address.");

    // ── Collections ──────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal static readonly CompositeFormat Empty = CompositeFormat.Parse("'{0}' must not be an empty collection.");

    /// <summary>{0} = paramName, {1} = max</summary>
    internal static readonly CompositeFormat MaxCount = CompositeFormat.Parse(
        "'{0}' must contain at most {1} element(s).");

    /// <summary>{0} = paramName, {1} = min</summary>
    internal static readonly CompositeFormat MinCount = CompositeFormat.Parse(
        "'{0}' must contain at least {1} element(s).");

    // ── SmartEnum ────────────────────────────────────────────────────────────
    /// <summary>{0} = paramName, {1} = value, {2} = SmartEnum type name</summary>
    internal static readonly CompositeFormat InvalidSmartEnum = CompositeFormat.Parse(
        "'{0}' value '{1}' is not a valid {2}.");
}
