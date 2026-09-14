using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Guards;

/// <summary>
/// The functional guard path: extension methods on <see cref="IGuardClause"/>, reached through
/// <see cref="Guard.Against"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every guard returns <see langword="null"/> when it passes and a non-null
/// <see cref="ErrorType.Validation"/> <see cref="Error"/> when it is violated. A guard never throws, and a
/// <see langword="null"/> input is reported as a violation rather than an exception, so a guard is safe to
/// call on unvalidated input. Chain several with <c>??</c> to stop at the first failure, or pass them to
/// <see cref="Guard.Collect(ReadOnlySpan{Error})"/> to report every failure.
/// </para>
/// <para>
/// The <c>paramName</c> parameter is filled in by the compiler from the argument expression, as
/// <see cref="ArgumentNullException.ThrowIfNull(object, string)"/> does. Pass it explicitly only to
/// override that name.
/// </para>
/// <para>
/// Messages are formatted with <see cref="CultureInfo.InvariantCulture"/>, so they do not change with the
/// server's culture. Translate them through <c>Error.Code</c> rather than by parsing the text.
/// </para>
/// </remarks>
public static partial class GuardClauseExtensions
{
    private const string DefaultParamName = "value";
    private const int RegexTimeoutMilliseconds = 250;

    // The cache key is a caller-supplied pattern, so the cache is bounded: a pattern derived from
    // configuration or user input must not be able to grow it, and its JIT-compilation cost, without limit.
    // Eviction is oldest-first. Only genuine first-time insertions are queued, which keeps the bound exact
    // without the bookkeeping a true LRU would need for a cache this small and this rarely evicted.
    private const int MaxCachedPatterns = 256;
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> _regexCacheInsertionOrder = new();

    // ── Null / empty ──────────────────────────────────────────────────────────

    /// <summary>Returns an error if <paramref name="value"/> is <see langword="null"/>.</summary>
    /// <typeparam name="T">The reference type being checked.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Null<T>(
        this IGuardClause guard,
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
        => value is null ? Required(GuardDescriptions.Null, paramName) : null;

    /// <summary>Returns an error if the nullable value type <paramref name="value"/> has no value.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Null<T>(
        this IGuardClause guard,
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct
        => value.HasValue ? null : Required(GuardDescriptions.Null, paramName);

    /// <summary>Returns an error if <paramref name="value"/> is <see langword="null"/> or empty.</summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? NullOrEmpty(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => string.IsNullOrEmpty(value) ? Required(GuardDescriptions.NullOrEmpty, paramName) : null;

    /// <summary>
    /// Returns an error if <paramref name="value"/> is <see langword="null"/>, empty, or only whitespace.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? NullOrWhiteSpace(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => string.IsNullOrWhiteSpace(value) ? Required(GuardDescriptions.NullOrWhiteSpace, paramName) : null;

    // ── String length ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> has fewer than <paramref name="minLength"/> characters.
    /// A <see langword="null"/> value is a violation.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="minLength">The minimum permitted length, inclusive.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? ShorterThan(
        this IGuardClause guard,
        string? value,
        int minLength,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return value.Length < minLength
            ? Error.Validation(ErrorCodes.Validation.MinLength, Describe(GuardDescriptions.ShorterThan, paramName, minLength))
            : null;
    }

    /// <summary>
    /// Returns an error if <paramref name="value"/> has more than <paramref name="maxLength"/> characters.
    /// A <see langword="null"/> value is a violation; check optional values for <see langword="null"/> first.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="maxLength">The maximum permitted length, inclusive.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? LongerThan(
        this IGuardClause guard,
        string? value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return value.Length > maxLength
            ? Error.Validation(ErrorCodes.Validation.MaxLength, Describe(GuardDescriptions.LongerThan, paramName, maxLength))
            : null;
    }

    // ── Numeric ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> is less than zero. Works for every numeric type.
    /// </summary>
    /// <remarks>A floating-point <c>NaN</c> is a violation.</remarks>
    /// <typeparam name="T">Any numeric type, such as <see cref="int"/>, <see cref="decimal"/>, or <see cref="double"/>.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Negative<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
        => value >= T.Zero ? null : OutOfRange(GuardDescriptions.Negative, paramName);

    /// <summary>
    /// Returns an error if <paramref name="value"/> is zero or less. Works for every numeric type.
    /// </summary>
    /// <remarks>A floating-point <c>NaN</c> is a violation.</remarks>
    /// <typeparam name="T">Any numeric type, such as <see cref="int"/>, <see cref="decimal"/>, or <see cref="double"/>.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? NegativeOrZero<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
        => value > T.Zero ? null : OutOfRange(GuardDescriptions.NegativeOrZero, paramName);

    // ── Comparison ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> is outside the inclusive range
    /// [<paramref name="min"/>, <paramref name="max"/>]. A <see langword="null"/> value is a violation.
    /// </summary>
    /// <remarks>
    /// For floating-point types <c>NaN</c> compares below every number, so it is a violation.
    /// </remarks>
    /// <typeparam name="T">Any type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? OutOfRange<T>(
        this IGuardClause guard,
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return value.CompareTo(min) < 0 || value.CompareTo(max) > 0
            ? Error.Validation(
                ErrorCodes.Validation.OutOfRange,
                string.Format(CultureInfo.InvariantCulture, GuardDescriptions.OutOfRange, Name(paramName), min, max))
            : null;
    }

    /// <summary>
    /// Returns an error if <paramref name="value"/> is less than <paramref name="min"/>. A
    /// <see langword="null"/> value is a violation.
    /// </summary>
    /// <typeparam name="T">Any type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The smallest permitted value, inclusive.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? LessThan<T>(
        this IGuardClause guard,
        T value,
        T min,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return value.CompareTo(min) < 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange, Describe(GuardDescriptions.LessThan, paramName, min))
            : null;
    }

    /// <summary>
    /// Returns an error if <paramref name="value"/> is greater than <paramref name="max"/>. A
    /// <see langword="null"/> value is a violation.
    /// </summary>
    /// <typeparam name="T">Any type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="max">The largest permitted value, inclusive.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? GreaterThan<T>(
        this IGuardClause guard,
        T value,
        T max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return value.CompareTo(max) > 0
            ? Error.Validation(ErrorCodes.Validation.OutOfRange, Describe(GuardDescriptions.GreaterThan, paramName, max))
            : null;
    }

    // ── Value ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> equals the default value for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value being checked.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Default<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => EqualityComparer<T>.Default.Equals(value, default!) ? Required(GuardDescriptions.Default, paramName) : null;

    /// <summary>Returns an error if <paramref name="value"/> is <see cref="Guid.Empty"/>.</summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The GUID to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? InvalidGuid(
        this IGuardClause guard,
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value == Guid.Empty ? Required(GuardDescriptions.InvalidGuid, paramName) : null;

    /// <summary>
    /// Returns an error if <paramref name="value"/> is not one of the named members of
    /// <typeparamref name="TEnum"/>, as happens when an out-of-range integer is cast to an enum.
    /// </summary>
    /// <remarks>
    /// The check is <see cref="Enum.IsDefined{TEnum}(TEnum)"/>, so for a <see cref="FlagsAttribute"/> enum a
    /// combination of flags is only accepted when that combination is itself a named member.
    /// </remarks>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? InvalidEnumValue<TEnum>(
        this IGuardClause guard,
        TEnum value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where TEnum : struct, Enum
        => Enum.IsDefined(value)
            ? null
            : Error.Validation(
                ErrorCodes.Validation.OutOfRange,
                Describe(GuardDescriptions.InvalidEnumValue, paramName, typeof(TEnum).Name));

    /// <summary>
    /// Returns an error if <paramref name="value"/> is not UTC, that is, if its
    /// <see cref="DateTimeOffset.Offset"/> is not zero.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? NotUtc(
        this IGuardClause guard,
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value.Offset == TimeSpan.Zero ? null : InvalidFormat(GuardDescriptions.NotUtc, paramName);

    /// <summary>
    /// Returns an error if <paramref name="value"/> is not UTC, that is, if its <see cref="DateTime.Kind"/>
    /// is not <see cref="DateTimeKind.Utc"/>. <see cref="DateTimeKind.Unspecified"/> is a violation.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? NotUtc(
        this IGuardClause guard,
        DateTime value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value.Kind == DateTimeKind.Utc ? null : InvalidFormat(GuardDescriptions.NotUtc, paramName);

    // ── Format / Email ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> does not match <paramref name="pattern"/>. A
    /// <see langword="null"/> value is a violation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each distinct pattern is compiled once and cached, with a 250 ms match timeout against ReDoS. The
    /// cache holds at most 256 patterns and evicts the oldest first. For a fixed pattern, prefer a
    /// <see cref="GeneratedRegexAttribute"/> field of your own and <see cref="True"/>.
    /// </para>
    /// <para>
    /// The pattern is not included in the error message, because the message can reach an HTTP response.
    /// A pattern that is not a valid regular expression is a programming error and throws
    /// <see cref="ArgumentException"/>.
    /// </para>
    /// </remarks>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The string to validate.</param>
    /// <param name="pattern">The regular expression the whole value must match.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? InvalidFormat(
        this IGuardClause guard,
        string? value,
        [StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return GetOrCacheCompiledRegex(pattern).IsMatch(value)
            ? null
            : InvalidFormat(GuardDescriptions.InvalidFormat, paramName);
    }

    /// <summary>
    /// Returns an error if <paramref name="value"/> is not a plausible email address. A
    /// <see langword="null"/>, empty, or whitespace value is a violation.
    /// </summary>
    /// <remarks>
    /// The check is deliberately loose (<c>local@domain.tld</c>, no whitespace, a single <c>@</c>). The only
    /// real proof that an address works is delivering a message to it.
    /// </remarks>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The string to validate.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Email(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Required(GuardDescriptions.NullOrWhiteSpace, paramName);

        return EmailRegex().IsMatch(value) ? null : InvalidFormat(GuardDescriptions.Email, paramName);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="source"/> is <see langword="null"/> or contains no elements.
    /// </summary>
    /// <remarks>Reads at most one element.</remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? Empty<T>(
        this IGuardClause guard,
        IEnumerable<T>? source,
        [CallerArgumentExpression(nameof(source))] string? paramName = null)
    {
        if (source is null)
            return Required(GuardDescriptions.Null, paramName);

        return source.Any() ? null : Required(GuardDescriptions.Empty, paramName);
    }

    /// <summary>
    /// Returns an error if <paramref name="source"/> contains more than <paramref name="max"/> elements. A
    /// <see langword="null"/> sequence is a violation.
    /// </summary>
    /// <remarks>
    /// Uses the collection's count when it has one; otherwise reads at most <paramref name="max"/> + 1
    /// elements, so a long or unbounded sequence is not read to the end.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="max">The maximum permitted number of elements.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? MaxCount<T>(
        this IGuardClause guard,
        IEnumerable<T>? source,
        int max,
        [CallerArgumentExpression(nameof(source))] string? paramName = null)
    {
        if (source is null)
            return Required(GuardDescriptions.Null, paramName);

        var limit = max < int.MaxValue ? max + 1 : int.MaxValue;
        return CountAtMost(source, limit) > max
            ? Error.Validation(ErrorCodes.Validation.OutOfRange, Describe(GuardDescriptions.MaxCount, paramName, max))
            : null;
    }

    /// <summary>
    /// Returns an error if <paramref name="source"/> contains fewer than <paramref name="min"/> elements. A
    /// <see langword="null"/> sequence is a violation.
    /// </summary>
    /// <remarks>
    /// Uses the collection's count when it has one; otherwise reads at most <paramref name="min"/> elements.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="min">The minimum required number of elements.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? MinCount<T>(
        this IGuardClause guard,
        IEnumerable<T>? source,
        int min,
        [CallerArgumentExpression(nameof(source))] string? paramName = null)
    {
        if (source is null)
            return Required(GuardDescriptions.Null, paramName);

        return CountAtMost(source, min) < min
            ? Error.Validation(ErrorCodes.Validation.OutOfRange, Describe(GuardDescriptions.MinCount, paramName, min))
            : null;
    }

    // ── Boolean predicate ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="error"/> if <paramref name="condition"/> is <see langword="false"/>, and
    /// <see langword="null"/> if it is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Use this for any business rule the built-in guards do not cover: you build the <see cref="Error"/>,
    /// and the guard surfaces it when the condition does not hold.
    /// </remarks>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="condition">The condition that must be <see langword="true"/> for the guard to pass.</param>
    /// <param name="error">The error to return when the condition is <see langword="false"/>.</param>
    public static Error? True(this IGuardClause guard, bool condition, Error error)
        => condition ? null : error;

    /// <summary>
    /// Returns <paramref name="error"/> if <paramref name="condition"/> is <see langword="true"/>, and
    /// <see langword="null"/> if it is <see langword="false"/>.
    /// </summary>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="condition">The condition that must be <see langword="false"/> for the guard to pass.</param>
    /// <param name="error">The error to return when the condition is <see langword="true"/>.</param>
    public static Error? False(this IGuardClause guard, bool condition, Error error)
        => condition ? error : null;

    // ── SmartEnum ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an error if <paramref name="value"/> does not correspond to a member of
    /// <typeparamref name="TEnum"/>.
    /// </summary>
    /// <typeparam name="TEnum">The concrete SmartEnum type.</typeparam>
    /// <typeparam name="TValue">The underlying value type of the SmartEnum.</typeparam>
    /// <param name="guard">The guard clause entry point.</param>
    /// <param name="value">The underlying value to look up.</param>
    /// <param name="paramName">The name used in the error message. Supplied by the compiler.</param>
    public static Error? InvalidSmartEnum<TEnum, TValue>(
        this IGuardClause guard,
        TValue value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where TEnum : SmartEnum<TEnum, TValue>
        where TValue : IEquatable<TValue>
        => SmartEnum<TEnum, TValue>.TryFromValue(value, out _)
            ? null
            : Error.Validation(
                ErrorCodes.Validation.OutOfRange,
                string.Format(
                    CultureInfo.InvariantCulture,
                    GuardDescriptions.InvalidSmartEnum,
                    Name(paramName),
                    value,
                    typeof(TEnum).Name));

    // ── Helpers ───────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex EmailRegex();

    private static string Name(string? paramName) => paramName ?? DefaultParamName;

    private static string Describe(CompositeFormat format, string? paramName)
        => string.Format(CultureInfo.InvariantCulture, format, Name(paramName));

    private static string Describe<TArg>(CompositeFormat format, string? paramName, TArg arg)
        => string.Format(CultureInfo.InvariantCulture, format, Name(paramName), arg);

    private static Error Required(CompositeFormat format, string? paramName)
        => Error.Validation(ErrorCodes.Validation.Required, Describe(format, paramName));

    private static Error OutOfRange(CompositeFormat format, string? paramName)
        => Error.Validation(ErrorCodes.Validation.OutOfRange, Describe(format, paramName));

    private static Error InvalidFormat(CompositeFormat format, string? paramName)
        => Error.Validation(ErrorCodes.Validation.InvalidFormat, Describe(format, paramName));

    private static int CountAtMost<T>(IEnumerable<T> source, int limit)
    {
        if (source.TryGetNonEnumeratedCount(out var count))
            return count;

        count = 0;
        using var enumerator = source.GetEnumerator();
        while (count < limit && enumerator.MoveNext())
            count++;

        return count;
    }

    private static Regex GetOrCacheCompiledRegex(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var cached))
            return cached;

        var compiled = new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds));

        if (!_regexCache.TryAdd(pattern, compiled))
        {
            // Another thread cached this pattern first; reuse its instance.
            return _regexCache.TryGetValue(pattern, out var winner) ? winner : compiled;
        }

        _regexCacheInsertionOrder.Enqueue(pattern);
        while (_regexCache.Count > MaxCachedPatterns && _regexCacheInsertionOrder.TryDequeue(out var oldest))
            _regexCache.TryRemove(oldest, out _);

        return compiled;
    }
}
