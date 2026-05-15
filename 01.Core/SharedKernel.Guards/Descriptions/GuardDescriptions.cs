namespace SharedKernel.Guards.Descriptions;

/// <summary>
/// Internal repository of const string message templates for all guard violations.
/// Use <c>string.Format</c> at the call site with the documented <c>{0}</c>/<c>{1}</c> placeholders.
/// This class is not part of the public API.
/// </summary>
internal static class GuardDescriptions
{
    // ── Null / empty ─────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal const string Null             = "'{0}' must not be null.";

    /// <summary>{0} = paramName</summary>
    internal const string NullOrEmpty      = "'{0}' must not be null or empty.";

    /// <summary>{0} = paramName</summary>
    internal const string NullOrWhiteSpace = "'{0}' must not be null, empty, or whitespace.";

    // ── String length ────────────────────────────────────────────────────────
    /// <summary>{0} = paramName, {1} = minLength</summary>
    internal const string ShorterThan      = "'{0}' must be at least {1} character(s) long.";

    /// <summary>{0} = paramName, {1} = maxLength</summary>
    internal const string LongerThan       = "'{0}' must be at most {1} character(s) long.";

    // ── Numeric ───────────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal const string NegativeOrZero   = "'{0}' must be greater than zero.";

    /// <summary>{0} = paramName</summary>
    internal const string Negative         = "'{0}' must be zero or greater.";

    /// <summary>{0} = paramName</summary>
    internal const string NotPositive      = "'{0}' must be a positive number (greater than zero).";

    // ── Range ─────────────────────────────────────────────────────────────────
    /// <summary>{0} = paramName, {1} = min, {2} = max (note: three placeholders)</summary>
    internal const string OutOfRange       = "'{0}' must be between {1} and {2} (inclusive).";

    // ── Default / Guid ────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal const string Default          = "'{0}' must not be the default value for its type.";

    /// <summary>{0} = paramName</summary>
    internal const string InvalidGuid      = "'{0}' must not be an empty GUID.";

    // ── Format / Email ────────────────────────────────────────────────────────
    /// <summary>{0} = paramName, {1} = pattern</summary>
    internal const string InvalidFormat    = "'{0}' does not match the required format '{1}'.";

    /// <summary>{0} = paramName</summary>
    internal const string Email            = "'{0}' is not a valid email address.";

    // ── Collections ───────────────────────────────────────────────────────────
    /// <summary>{0} = paramName</summary>
    internal const string Empty            = "'{0}' must not be an empty collection.";

    /// <summary>{0} = paramName, {1} = max</summary>
    internal const string MaxCount         = "'{0}' must contain at most {1} element(s).";

    /// <summary>{0} = paramName, {1} = min</summary>
    internal const string MinCount         = "'{0}' must contain at least {1} element(s).";

    // ── SmartEnum ─────────────────────────────────────────────────────────────
    /// <summary>{0} = enum type name, {1} = id</summary>
    internal const string InvalidSmartEnum = "'{1}' is not a valid value for {0}.";
}
