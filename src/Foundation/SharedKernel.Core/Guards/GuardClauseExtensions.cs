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
/// The functional guard clauses: checks that return an <see cref="Error"/> describing a violation instead
/// of throwing. Reach them through <see cref="Guard.Against"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> Every guard returns <see langword="null"/> when the value is valid and a non-null
/// <see cref="ErrorType.Validation"/> error when it is not. A guard never throws because of the value it
/// checks: a <see langword="null"/> value is reported as a violation with code
/// <see cref="ErrorCodes.Validation.Required"/>. The only exceptions are
/// <see cref="ArgumentNullException"/> and <see cref="ArgumentException"/> for mistakes in the guard's own
/// arguments, such as a <see langword="null"/> or malformed regular-expression pattern.
/// </para>
/// <para>
/// <b>Parameter names.</b> The trailing <c>paramName</c> parameter is filled in by the compiler with the
/// source text of the checked argument (<see cref="CallerArgumentExpressionAttribute"/>), so
/// <c>Guard.Against.Null(request.Email)</c> reports <c>'request.Email'</c>. Pass a name explicitly only to
/// override it.
/// </para>
/// <para>
/// <b>Messages and codes.</b> Messages are formatted with <see cref="CultureInfo.InvariantCulture"/>, so
/// they are identical on every server. Branch and translate on <see cref="Error.Code"/>, never on the text.
/// </para>
/// <para>
/// <b>Composition.</b> Chain guards with <c>??</c> to stop at the first violation, pass several to
/// <see cref="Guard.Collect(ReadOnlySpan{Error})"/> to report all of them, and convert the outcome with
/// <see cref="GuardErrorExtensions"/>. Every guard here has a throwing twin on <see cref="Guard.Throw"/>.
/// </para>
/// <para>
/// <b>Extending.</b> Add a guard by writing an extension method on <see cref="IGuardClause"/> that follows
/// the same contract. Analyzer <c>SK0006</c> reports a <see langword="throw"/> inside guard code.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using SharedKernel.Guards;
///
/// public static Result&lt;Email&gt; Create(string? value) =&gt;
///     (Guard.Against.NullOrWhiteSpace(value)
///      ?? Guard.Against.LongerThan(value, 254)
///      ?? Guard.Against.Email(value))
///     .ToResult(() =&gt; new Email(value!));
/// </code>
/// </example>
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

    /// <summary>Checks that a reference-type value is not <see langword="null"/>.</summary>
    /// <typeparam name="T">The reference type being checked.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is not <see langword="null"/>; otherwise an error
    /// with code <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? Null<T>(
        this IGuardClause guard,
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
        => value is null ? Required(GuardDescriptions.Null, paramName) : null;

    /// <summary>Checks that a nullable value type has a value.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> has a value; otherwise an error with code
    /// <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? Null<T>(
        this IGuardClause guard,
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct
        => value.HasValue ? null : Required(GuardDescriptions.Null, paramName);

    /// <summary>Checks that a string is neither <see langword="null"/> nor empty.</summary>
    /// <remarks>A string containing only whitespace passes; use <see cref="NullOrWhiteSpace"/> to reject it.</remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> has at least one character; otherwise an error
    /// with code <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? NullOrEmpty(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => string.IsNullOrEmpty(value) ? Required(GuardDescriptions.NullOrEmpty, paramName) : null;

    /// <summary>Checks that a string contains at least one non-whitespace character.</summary>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> contains a non-whitespace character; otherwise an
    /// error with code <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? NullOrWhiteSpace(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => string.IsNullOrWhiteSpace(value) ? Required(GuardDescriptions.NullOrWhiteSpace, paramName) : null;

    // ── String length ─────────────────────────────────────────────────────────

    /// <summary>Checks that a string has at least <paramref name="minLength"/> characters.</summary>
    /// <remarks>
    /// Length is <see cref="string.Length"/>, which counts UTF-16 code units, not user-perceived characters.
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="minLength">The minimum permitted length, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is at least <paramref name="minLength"/> long; an
    /// error with code <see cref="ErrorCodes.Validation.MinLength"/> when it is shorter; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a string has at most <paramref name="maxLength"/> characters.</summary>
    /// <remarks>
    /// A <see langword="null"/> value is a violation, so check an optional value for <see langword="null"/>
    /// first. Length is <see cref="string.Length"/>, which counts UTF-16 code units.
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="maxLength">The maximum permitted length, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is at most <paramref name="maxLength"/> long; an
    /// error with code <see cref="ErrorCodes.Validation.MaxLength"/> when it is longer; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a number is zero or greater.</summary>
    /// <remarks>
    /// Works for every numeric type through generic math: <see cref="int"/>, <see cref="long"/>,
    /// <see cref="decimal"/>, <see cref="double"/>, <see cref="float"/>, <see cref="short"/>, and so on. A
    /// floating-point <c>NaN</c> is a violation.
    /// </remarks>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The number to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is zero or greater; otherwise an error with code
    /// <see cref="ErrorCodes.Validation.OutOfRange"/>.
    /// </returns>
    public static Error? Negative<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
        => value >= T.Zero ? null : OutOfRange(GuardDescriptions.Negative, paramName);

    /// <summary>Checks that a number is greater than zero.</summary>
    /// <remarks>
    /// Works for every numeric type through generic math. A floating-point <c>NaN</c> is a violation.
    /// </remarks>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The number to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is greater than zero; otherwise an error with code
    /// <see cref="ErrorCodes.Validation.OutOfRange"/>.
    /// </returns>
    public static Error? NegativeOrZero<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
        => value > T.Zero ? null : OutOfRange(GuardDescriptions.NegativeOrZero, paramName);

    // ── Comparison ────────────────────────────────────────────────────────────

    /// <summary>Checks that a value lies within the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    /// <remarks>
    /// Comparison uses <see cref="IComparable{T}.CompareTo(T)"/>. For floating-point types <c>NaN</c> compares
    /// below every number, so it is a violation. When <paramref name="min"/> is greater than
    /// <paramref name="max"/> no value can pass.
    /// </remarks>
    /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>, such as a number, <see cref="DateTimeOffset"/>, or <see cref="string"/>.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The smallest permitted value, inclusive.</param>
    /// <param name="max">The largest permitted value, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is within the range; an error with code
    /// <see cref="ErrorCodes.Validation.OutOfRange"/> when it is outside; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a value is not less than <paramref name="min"/>.</summary>
    /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The smallest permitted value, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is greater than or equal to <paramref name="min"/>;
    /// an error with code <see cref="ErrorCodes.Validation.OutOfRange"/> when it is less; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a value is not greater than <paramref name="max"/>.</summary>
    /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="max">The largest permitted value, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is less than or equal to <paramref name="max"/>; an
    /// error with code <see cref="ErrorCodes.Validation.OutOfRange"/> when it is greater; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a value is not the default value of its type.</summary>
    /// <remarks>
    /// Uses <see cref="EqualityComparer{T}.Default"/>, so it catches <c>0</c>, <see cref="Guid.Empty"/>,
    /// <c>default(DateTime)</c>, an all-default struct, and <see langword="null"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> differs from <c>default(T)</c>; otherwise an error
    /// with code <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? Default<T>(
        this IGuardClause guard,
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => EqualityComparer<T>.Default.Equals(value, default!) ? Required(GuardDescriptions.Default, paramName) : null;

    /// <summary>Checks that a GUID is not <see cref="Guid.Empty"/>.</summary>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The GUID to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is not <see cref="Guid.Empty"/>; otherwise an error
    /// with code <see cref="ErrorCodes.Validation.Required"/>.
    /// </returns>
    public static Error? InvalidGuid(
        this IGuardClause guard,
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value == Guid.Empty ? Required(GuardDescriptions.InvalidGuid, paramName) : null;

    /// <summary>
    /// Checks that an enum value is one of the type's named members, catching an out-of-range integer cast
    /// such as <c>(OrderStatus)99</c>.
    /// </summary>
    /// <remarks>
    /// The check is <see cref="Enum.IsDefined{TEnum}(TEnum)"/>. For a <see cref="FlagsAttribute"/> enum, a
    /// combination of flags passes only when that exact combination is itself a named member.
    /// </remarks>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The enum value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is a named member of <typeparamref name="TEnum"/>;
    /// otherwise an error with code <see cref="ErrorCodes.Validation.OutOfRange"/>.
    /// </returns>
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

    /// <summary>Checks that a <see cref="DateTimeOffset"/> is expressed in UTC.</summary>
    /// <remarks>
    /// The check is on the representation, not the instant: <c>2026-01-01T03:00+03:00</c> is the same moment
    /// as <c>2026-01-01T00:00Z</c> but is rejected. Convert with <see cref="DateTimeOffset.ToUniversalTime"/>
    /// when you only need the instant.
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <see cref="DateTimeOffset.Offset"/> is zero; otherwise an error with code
    /// <see cref="ErrorCodes.Validation.InvalidFormat"/>.
    /// </returns>
    public static Error? NotUtc(
        this IGuardClause guard,
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value.Offset == TimeSpan.Zero ? null : InvalidFormat(GuardDescriptions.NotUtc, paramName);

    /// <summary>Checks that a <see cref="DateTime"/> is of kind <see cref="DateTimeKind.Utc"/>.</summary>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is a violation: a value parsed or loaded without a kind carries
    /// no guarantee that it is UTC.
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>; otherwise an
    /// error with code <see cref="ErrorCodes.Validation.InvalidFormat"/>.
    /// </returns>
    public static Error? NotUtc(
        this IGuardClause guard,
        DateTime value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => value.Kind == DateTimeKind.Utc ? null : InvalidFormat(GuardDescriptions.NotUtc, paramName);

    // ── Format / Email ────────────────────────────────────────────────────────

    /// <summary>Checks that a string matches a regular expression.</summary>
    /// <remarks>
    /// <para>
    /// The pattern is matched with <see cref="Regex.IsMatch(string)"/>, so anchor it (<c>^…$</c>) to require
    /// a whole-string match. Each distinct pattern is compiled once and cached; the cache holds at most 256
    /// patterns and evicts the oldest first.
    /// </para>
    /// <para>
    /// Matching stops after 250 ms. A value that hits the limit is reported as not matching rather than
    /// throwing <see cref="RegexMatchTimeoutException"/>, so a pathological input cannot turn validation into
    /// an unhandled exception.
    /// </para>
    /// <para>
    /// The pattern is not included in the message, because the message can reach an HTTP response. For a
    /// fixed pattern, a <see cref="GeneratedRegexAttribute"/> method of your own passed to
    /// <see cref="True"/> avoids the cache and the runtime compilation.
    /// </para>
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="pattern">The regular expression to match.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> matches; an error with code
    /// <see cref="ErrorCodes.Validation.InvalidFormat"/> when it does not or when matching times out; an error
    /// with code <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
    public static Error? InvalidFormat(
        this IGuardClause guard,
        string? value,
        [StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (value is null)
            return Required(GuardDescriptions.Null, paramName);

        return IsMatch(GetOrCacheCompiledRegex(pattern), value)
            ? null
            : InvalidFormat(GuardDescriptions.InvalidFormat, paramName);
    }

    /// <summary>Checks that a string is a plausible email address.</summary>
    /// <remarks>
    /// The check is deliberately loose: one <c>@</c>, no whitespace, and a dot in the domain
    /// (<c>local@domain.tld</c>). It rejects obvious typos without rejecting valid but unusual addresses. The
    /// only real proof that an address works is delivering a message to it.
    /// </remarks>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> looks like an email address; an error with code
    /// <see cref="ErrorCodes.Validation.InvalidFormat"/> when it does not; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>, empty, or whitespace.
    /// </returns>
    public static Error? Email(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Required(GuardDescriptions.NullOrWhiteSpace, paramName);

        return IsMatch(EmailRegex(), value) ? null : InvalidFormat(GuardDescriptions.Email, paramName);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    /// <summary>Checks that a sequence contains at least one element.</summary>
    /// <remarks>
    /// Reads at most one element. A lazy sequence that cannot be enumerated twice loses that element for the
    /// caller, so materialize such a sequence before guarding it.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="source"/> has an element; otherwise an error with code
    /// <see cref="ErrorCodes.Validation.Required"/>, including when it is <see langword="null"/>.
    /// </returns>
    public static Error? Empty<T>(
        this IGuardClause guard,
        IEnumerable<T>? source,
        [CallerArgumentExpression(nameof(source))] string? paramName = null)
    {
        if (source is null)
            return Required(GuardDescriptions.Null, paramName);

        return source.Any() ? null : Required(GuardDescriptions.Empty, paramName);
    }

    /// <summary>Checks that a sequence contains no more than <paramref name="max"/> elements.</summary>
    /// <remarks>
    /// Uses the collection's count when it exposes one; otherwise reads at most <paramref name="max"/> + 1
    /// elements, so a long or unbounded sequence is never read to the end.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="max">The maximum permitted number of elements, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="source"/> has at most <paramref name="max"/> elements; an
    /// error with code <see cref="ErrorCodes.Validation.OutOfRange"/> when it has more; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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

    /// <summary>Checks that a sequence contains at least <paramref name="min"/> elements.</summary>
    /// <remarks>
    /// Uses the collection's count when it exposes one; otherwise reads at most <paramref name="min"/> elements.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="source">The sequence to check.</param>
    /// <param name="min">The minimum required number of elements, inclusive.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="source"/> has at least <paramref name="min"/> elements; an
    /// error with code <see cref="ErrorCodes.Validation.OutOfRange"/> when it has fewer; an error with code
    /// <see cref="ErrorCodes.Validation.Required"/> when it is <see langword="null"/>.
    /// </returns>
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
    /// Reports <paramref name="error"/> unless <paramref name="condition"/> holds. Use it for any rule the
    /// built-in guards do not cover.
    /// </summary>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="condition">The condition that must be <see langword="true"/> for the guard to pass.</param>
    /// <param name="error">The error to report when the condition is <see langword="false"/>.</param>
    /// <returns><see langword="null"/> when <paramref name="condition"/> is <see langword="true"/>; otherwise <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="error"/> is <see langword="null"/>. Checked eagerly: a <see langword="null"/> error would
    /// otherwise make a violation indistinguishable from a pass.
    /// </exception>
    public static Error? True(this IGuardClause guard, bool condition, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return condition ? null : error;
    }

    /// <summary>Reports <paramref name="error"/> when <paramref name="condition"/> holds.</summary>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="condition">The condition that must be <see langword="false"/> for the guard to pass.</param>
    /// <param name="error">The error to report when the condition is <see langword="true"/>.</param>
    /// <returns><see langword="null"/> when <paramref name="condition"/> is <see langword="false"/>; otherwise <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="error"/> is <see langword="null"/>. Checked eagerly: a <see langword="null"/> error would
    /// otherwise make a violation indistinguishable from a pass.
    /// </exception>
    public static Error? False(this IGuardClause guard, bool condition, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return condition ? error : null;
    }

    // ── SmartEnum ─────────────────────────────────────────────────────────────

    /// <summary>Checks that a raw value corresponds to a member of a <see cref="SmartEnum{TEnum, TValue}"/>.</summary>
    /// <remarks>Use it on values from outside the process, such as a request field or a database column.</remarks>
    /// <typeparam name="TEnum">The SmartEnum type.</typeparam>
    /// <typeparam name="TValue">The SmartEnum's underlying value type.</typeparam>
    /// <param name="guard">The guard entry point, <see cref="Guard.Against"/>.</param>
    /// <param name="value">The underlying value to look up.</param>
    /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
    /// <returns>
    /// <see langword="null"/> when a member of <typeparamref name="TEnum"/> has <paramref name="value"/>;
    /// otherwise, including when <paramref name="value"/> is <see langword="null"/>, an error with code
    /// <see cref="ErrorCodes.Validation.OutOfRange"/>.
    /// </returns>
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

    // A timeout means the input drove the pattern into catastrophic backtracking; report it as a mismatch.
    private static bool IsMatch(Regex regex, string value)
    {
        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

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
