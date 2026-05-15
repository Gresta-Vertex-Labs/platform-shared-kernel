using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SharedKernel.Guards.Descriptions;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Guards.Clauses;

/// <summary>
/// Extension methods for <see cref="IGuardClause"/> providing the functional guard path.
/// All methods return <see langword="null"/> when the guard passes and a non-null
/// <see cref="Error"/> when it is violated.
/// </summary>
/// <remarks>
/// Never use <see cref="Error.None"/> as the "passed" sentinel — use actual <see langword="null"/>
/// so callers can distinguish a passed guard from an error-free error.
/// </remarks>
public static class GuardClauseExtensions
{
    // ── Regex cache — keyed by pattern string (for InvalidFormat) ─────────────
    // ConcurrentDictionary is AOT-safe; Regex compiled once per distinct pattern.
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    // Fixed compiled regex for email validation — created once at type-init.
    private static readonly Regex _emailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    // ── Null / empty ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// <see langword="null"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The reference type being checked.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? Null<T>(this IGuardClause guard, T? value, string paramName)
        where T : class
        => value is null
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.Null, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// <see langword="null"/> or <see cref="string.Empty"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? NullOrEmpty(this IGuardClause guard, string? value, string paramName)
        => string.IsNullOrEmpty(value)
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.NullOrEmpty, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// <see langword="null"/>, empty, or consists only of whitespace; otherwise returns
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? NullOrWhiteSpace(this IGuardClause guard, string? value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.NullOrWhiteSpace, paramName))
            : null;

    // ── String length ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> has fewer
    /// than <paramref name="minLength"/> characters; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="minLength">The minimum permitted length (inclusive).</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? ShorterThan(this IGuardClause guard, string value, int minLength, string paramName)
        => value.Length < minLength
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.ShorterThan, paramName, minLength))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> has more
    /// than <paramref name="maxLength"/> characters; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="maxLength">The maximum permitted length (inclusive).</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? LongerThan(this IGuardClause guard, string value, int maxLength, string paramName)
        => value.Length > maxLength
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.LongerThan, paramName, maxLength))
            : null;

    // ── Numeric — int ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative or zero; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NegativeOrZero(this IGuardClause guard, int value, string paramName)
        => value <= 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NegativeOrZero, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? Negative(this IGuardClause guard, int value, string paramName)
        => value < 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.Negative, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is not
    /// positive (i.e., zero or negative); otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NotPositive(this IGuardClause guard, int value, string paramName)
        => value <= 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NotPositive, paramName))
            : null;

    // ── Numeric — decimal ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative or zero; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NegativeOrZero(this IGuardClause guard, decimal value, string paramName)
        => value <= 0m
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NegativeOrZero, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? Negative(this IGuardClause guard, decimal value, string paramName)
        => value < 0m
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.Negative, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is not
    /// positive; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NotPositive(this IGuardClause guard, decimal value, string paramName)
        => value <= 0m
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NotPositive, paramName))
            : null;

    // ── Numeric — long ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative or zero; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NegativeOrZero(this IGuardClause guard, long value, string paramName)
        => value <= 0L
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NegativeOrZero, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// negative; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? Negative(this IGuardClause guard, long value, string paramName)
        => value < 0L
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.Negative, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is not
    /// positive; otherwise returns <see langword="null"/>.
    /// </summary>
    public static Error? NotPositive(this IGuardClause guard, long value, string paramName)
        => value <= 0L
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.NotPositive, paramName))
            : null;

    // ── Range ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is
    /// outside the inclusive range [<paramref name="min"/>, <paramref name="max"/>];
    /// otherwise returns <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">Any type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? OutOfRange<T>(this IGuardClause guard, T value, T min, T max, string paramName)
        where T : IComparable<T>
        => value.CompareTo(min) < 0 || value.CompareTo(max) > 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.OutOfRange, paramName, min, max))
            : null;

    // ── Default / Guid ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> equals
    /// the default value for <typeparamref name="T"/>; otherwise returns <see langword="null"/>.
    /// Uses <see cref="EqualityComparer{T}.Default"/> — no reflection, AOT-safe.
    /// </summary>
    /// <typeparam name="T">The type of the value being checked.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? Default<T>(this IGuardClause guard, T value, string paramName)
        => EqualityComparer<T>.Default.Equals(value, default!)
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.Default, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> equals
    /// <see cref="Guid.Empty"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The GUID to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? InvalidGuid(this IGuardClause guard, Guid value, string paramName)
        => value == Guid.Empty
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.InvalidGuid, paramName))
            : null;

    // ── Format / Email ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> does not
    /// match the compiled <paramref name="pattern"/>; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="Regex"/> for each distinct <paramref name="pattern"/> is compiled and cached
    /// in a static <see cref="ConcurrentDictionary{TKey,TValue}"/> — a new instance is never
    /// created per call. A bounded timeout of 250 ms prevents ReDoS.
    /// </remarks>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The string to validate.</param>
    /// <param name="pattern">The regular-expression pattern to match against.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? InvalidFormat(this IGuardClause guard, string value, string pattern, string paramName)
    {
        var regex = _regexCache.GetOrAdd(pattern, p =>
            new Regex(p, RegexOptions.Compiled, TimeSpan.FromMilliseconds(250)));

        return regex.IsMatch(value)
            ? null
            : Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.InvalidFormat, paramName, pattern));
    }

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="value"/> is not a
    /// valid email address; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Uses a static compiled <see cref="Regex"/> field — no per-call allocation.
    /// A <see langword="null"/> or empty <paramref name="value"/> is treated as invalid.
    /// </remarks>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="value">The string to validate.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? Email(this IGuardClause guard, string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !_emailRegex.IsMatch(value))
        {
            return Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.Email, paramName));
        }

        return null;
    }

    // ── Collections ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="source"/> contains
    /// no elements; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The enumerable is evaluated at most once per call via <c>Linq.Count()</c>.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? Empty<T>(this IGuardClause guard, IEnumerable<T> source, string paramName)
        => !source.Any()
            ? Error.Validation(ErrorCodes.Validation.Required,
                string.Format(GuardDescriptions.Empty, paramName))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="source"/> contains
    /// more than <paramref name="max"/> elements; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The enumerable is evaluated at most once per call.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="max">The maximum permitted number of elements.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? MaxCount<T>(this IGuardClause guard, IEnumerable<T> source, int max, string paramName)
        => source.Count() > max
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.MaxCount, paramName, max))
            : null;

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="source"/> contains
    /// fewer than <paramref name="min"/> elements; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The enumerable is evaluated at most once per call.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="min">The minimum required number of elements.</param>
    /// <param name="paramName">The parameter name used in the error message.</param>
    public static Error? MinCount<T>(this IGuardClause guard, IEnumerable<T> source, int min, string paramName)
        => source.Count() < min
            ? Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.MinCount, paramName, min))
            : null;

    // ── Boolean predicate ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the caller-supplied <paramref name="error"/> if <paramref name="condition"/> is
    /// <see langword="false"/>; returns <see langword="null"/> if <paramref name="condition"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// This guard enables arbitrary business-rule checks: the caller constructs the domain
    /// <see cref="Error"/> and this method conditionally surfaces it.
    /// </remarks>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="condition">The condition that must be <see langword="true"/> for the guard to pass.</param>
    /// <param name="error">The error to return when the condition is <see langword="false"/>.</param>
    public static Error? True(this IGuardClause guard, bool condition, Error error)
        => condition ? null : error;

    /// <summary>
    /// Returns the caller-supplied <paramref name="error"/> if <paramref name="condition"/> is
    /// <see langword="true"/>; returns <see langword="null"/> if <paramref name="condition"/> is
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="condition">The condition that must be <see langword="false"/> for the guard to pass.</param>
    /// <param name="error">The error to return when the condition is <see langword="true"/>.</param>
    public static Error? False(this IGuardClause guard, bool condition, Error error)
        => condition ? error : null;

    // ── SmartEnum ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ErrorType.Validation"/> error if <paramref name="id"/> does not
    /// correspond to a known <typeparamref name="TEnum"/> member; otherwise returns
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Calls <see cref="SmartEnum{TEnum,TValue}.TryFromValue"/> — zero reflection, AOT-safe.
    /// </remarks>
    /// <typeparam name="TEnum">The concrete SmartEnum type.</typeparam>
    /// <typeparam name="TValue">The underlying value type of the SmartEnum.</typeparam>
    /// <param name="guard">The guard clause entry-point.</param>
    /// <param name="id">The value to look up.</param>
    public static Error? InvalidSmartEnum<TEnum, TValue>(this IGuardClause guard, TValue id)
        where TEnum : SmartEnum<TEnum, TValue>
        where TValue : IEquatable<TValue>
        => SmartEnum<TEnum, TValue>.TryFromValue(id, out _)
            ? null
            : Error.Validation(ErrorCodes.Validation.OutOfRange,
                string.Format(GuardDescriptions.InvalidSmartEnum, typeof(TEnum).Name, id));
}
