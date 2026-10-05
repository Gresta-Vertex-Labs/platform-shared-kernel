using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Guards;

/// <summary>
/// Entry point for guard clauses: precondition checks that either return an <see cref="Error"/>
/// (<see cref="Against"/>) or throw (<see cref="Throw"/>).
/// </summary>
/// <remarks>
/// <para>
/// Both paths offer the same guards with the same names and parameters, and a violation produces the same
/// <see cref="Error"/> on either path. Choose by what a violation means where the check runs:
/// </para>
/// <list type="table">
///   <listheader><term>Path</term><description>Use when</description></listheader>
///   <item>
///     <term><see cref="Against"/></term>
///     <description>
///     Invalid input is an expected outcome the caller must handle: factory methods, command handlers,
///     anything returning <see cref="Result{T}"/>. Returns <see cref="Error"/>?.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Throw"/></term>
///     <description>
///     A violation means an invariant was broken: constructors, aggregate methods. Throws
///     <see cref="DomainException"/>, which a presentation layer renders from its <see cref="Error.Type"/>.
///     </description>
///   </item>
/// </list>
/// <para>
/// Every guard captures the checked argument's source text as the parameter name, so <c>nameof(...)</c> is
/// not needed. <c>using SharedKernel.Guards;</c> is the only import either path requires.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Functional: stop at the first violation, build the value only when every guard passed.
/// Result&lt;Customer&gt; customer = (Guard.Against.NullOrWhiteSpace(name) ?? Guard.Against.Email(email))
///     .ToResult(() =&gt; new Customer(name!, email!));
///
/// // Functional: report every violation at once.
/// ValidationResult validation = Guard.Collect(
///     Guard.Against.NullOrWhiteSpace(name),
///     Guard.Against.Email(email));
///
/// // Imperative: inside a constructor.
/// Guard.Throw.NullOrWhiteSpace(name);
/// </code>
/// </example>
public static class Guard
{
    private static readonly IGuardClause _against = new DefaultGuardClause();

    /// <summary>
    /// The functional guard path. Each guard returns <see langword="null"/> when the value is valid and a
    /// validation <see cref="Error"/> when it is not, and never throws because of the value it checks.
    /// </summary>
    /// <remarks>The guards are extension methods in <see cref="GuardClauseExtensions"/>; this property always returns the same instance.</remarks>
    public static IGuardClause Against => _against;

    /// <summary>Combines the results of several functional guards into one <see cref="ValidationResult"/>.</summary>
    /// <remarks>
    /// Unlike chaining with <c>??</c>, every guard has already run and every violation is kept, so a caller
    /// can report all invalid fields at once.
    /// </remarks>
    /// <param name="errors">The results of <see cref="Against"/> guards. <see langword="null"/> entries are guards that passed.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult"/> when every entry is <see langword="null"/>; otherwise a
    /// failed one carrying every non-null <see cref="Error"/>, in argument order.
    /// </returns>
    /// <example>
    /// <code>
    /// ValidationResult validation = Guard.Collect(
    ///     Guard.Against.NullOrWhiteSpace(command.Name),
    ///     Guard.Against.OutOfRange(command.Quantity, 1, 100),
    ///     Guard.Against.NotUtc(command.DeliverAt));
    ///
    /// if (!validation.IsValid)
    ///     return validation.Errors;   // e.g. map to a 400 with per-field details
    /// </code>
    /// </example>
    public static ValidationResult Collect(params ReadOnlySpan<Error?> errors)
    {
        List<Error>? failures = null;
        foreach (var error in errors)
        {
            if (error is not null)
                (failures ??= []).Add(error);
        }

        return failures is null ? ValidationResult.Success() : ValidationResult.Failure(failures);
    }

    private sealed class DefaultGuardClause : IGuardClause { }

    /// <summary>
    /// The imperative guard path. Each method runs the <see cref="Against"/> guard of the same name and throws
    /// <see cref="DomainException"/> carrying its <see cref="Error"/> when the guard reports a violation.
    /// </summary>
    /// <remarks>
    /// Reference, string, and collection parameters are annotated <see cref="NotNullAttribute"/>, so after a
    /// guard returns the compiler treats the argument as non-null. The error codes and messages are those
    /// documented on <see cref="GuardClauseExtensions"/>.
    /// </remarks>
    public static class Throw
    {
        // [NotNull] tells callers the argument is non-null once the method returns. Each body proves that by
        // throwing on the guard's error, which flow analysis cannot follow into ThrowIfError, hence CS8777.
#pragma warning disable CS8777
        /// <summary>Throws when a reference-type value is <see langword="null"/>.</summary>
        /// <typeparam name="T">The reference type being checked.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public static void Null<T>(
            [NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : class
            => ThrowIfError(Against.Null(value, paramName));

        /// <summary>Throws when a nullable value type has no value.</summary>
        /// <typeparam name="T">The underlying value type.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> has no value.</exception>
        public static void Null<T>(
            [NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : struct
            => ThrowIfError(Against.Null(value, paramName));

        /// <summary>Throws when a string is <see langword="null"/> or empty.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or empty.</exception>
        public static void NullOrEmpty(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NullOrEmpty(value, paramName));

        /// <summary>Throws when a string is <see langword="null"/>, empty, or only whitespace.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>, empty, or only whitespace.</exception>
        public static void NullOrWhiteSpace(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NullOrWhiteSpace(value, paramName));

        /// <summary>Throws when a string is <see langword="null"/> or shorter than <paramref name="minLength"/>.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="minLength">The minimum permitted length, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or shorter than <paramref name="minLength"/>.</exception>
        public static void ShorterThan(
            [NotNull] string? value,
            int minLength,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.ShorterThan(value, minLength, paramName));

        /// <summary>Throws when a string is <see langword="null"/> or longer than <paramref name="maxLength"/>.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="maxLength">The maximum permitted length, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or longer than <paramref name="maxLength"/>.</exception>
        public static void LongerThan(
            [NotNull] string? value,
            int maxLength,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.LongerThan(value, maxLength, paramName));

        /// <summary>Throws when a number is less than zero.</summary>
        /// <typeparam name="T">The numeric type.</typeparam>
        /// <param name="value">The number to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is less than zero, or is <c>NaN</c>.</exception>
        public static void Negative<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumber<T>
            => ThrowIfError(Against.Negative(value, paramName));

        /// <summary>Throws when a number is zero or less.</summary>
        /// <typeparam name="T">The numeric type.</typeparam>
        /// <param name="value">The number to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is zero or less, or is <c>NaN</c>.</exception>
        public static void NegativeOrZero<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumber<T>
            => ThrowIfError(Against.NegativeOrZero(value, paramName));

        /// <summary>Throws when a value lies outside the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="min">The smallest permitted value, inclusive.</param>
        /// <param name="max">The largest permitted value, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or outside the range.</exception>
        public static void OutOfRange<T>(
            [NotNull] T value,
            T min,
            T max,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.OutOfRange(value, min, max, paramName));

        /// <summary>Throws when a value is less than <paramref name="min"/>.</summary>
        /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="min">The smallest permitted value, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or less than <paramref name="min"/>.</exception>
        public static void LessThan<T>(
            [NotNull] T value,
            T min,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.LessThan(value, min, paramName));

        /// <summary>Throws when a value is greater than <paramref name="max"/>.</summary>
        /// <typeparam name="T">A type that implements <see cref="IComparable{T}"/>.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="max">The largest permitted value, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/> or greater than <paramref name="max"/>.</exception>
        public static void GreaterThan<T>(
            [NotNull] T value,
            T max,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.GreaterThan(value, max, paramName));

        /// <summary>Throws when a value is the default value of its type.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> equals <c>default(T)</c>.</exception>
        public static void Default<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.Default(value, paramName));

        /// <summary>Throws when a GUID is <see cref="Guid.Empty"/>.</summary>
        /// <param name="value">The GUID to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
        public static void InvalidGuid(
            Guid value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.InvalidGuid(value, paramName));

        /// <summary>Throws when an enum value is not one of the type's named members.</summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        /// <param name="value">The enum value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is not a named member of <typeparamref name="TEnum"/>.</exception>
        public static void InvalidEnumValue<TEnum>(
            TEnum value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where TEnum : struct, Enum
            => ThrowIfError(Against.InvalidEnumValue(value, paramName));

        /// <summary>Throws when a <see cref="DateTimeOffset"/> is not expressed in UTC.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException">The offset of <paramref name="value"/> is not zero.</exception>
        public static void NotUtc(
            DateTimeOffset value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NotUtc(value, paramName));

        /// <summary>Throws when a <see cref="DateTime"/> is not of kind <see cref="DateTimeKind.Utc"/>.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException">The kind of <paramref name="value"/> is <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Unspecified"/>.</exception>
        public static void NotUtc(
            DateTime value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NotUtc(value, paramName));

        /// <summary>Throws when a string is <see langword="null"/> or does not match a regular expression.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="pattern">The regular expression to match. Anchor it to require a whole-string match.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>, does not match, or times out while matching.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
        public static void InvalidFormat(
            [NotNull] string? value,
            [StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.InvalidFormat(value, pattern, paramName));

        /// <summary>Throws when a string is not a plausible email address.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>, blank, or not shaped like <c>local@domain.tld</c>.</exception>
        public static void Email(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.Email(value, paramName));

        /// <summary>Throws when a sequence is <see langword="null"/> or has no elements.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The sequence to check.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="source"/> is <see langword="null"/> or empty.</exception>
        public static void Empty<T>(
            [NotNull] IEnumerable<T>? source,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.Empty(source, paramName));

        /// <summary>Throws when a sequence is <see langword="null"/> or has more than <paramref name="max"/> elements.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The sequence to check.</param>
        /// <param name="max">The maximum permitted number of elements, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="source"/> is <see langword="null"/> or has more than <paramref name="max"/> elements.</exception>
        public static void MaxCount<T>(
            [NotNull] IEnumerable<T>? source,
            int max,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.MaxCount(source, max, paramName));

        /// <summary>Throws when a sequence is <see langword="null"/> or has fewer than <paramref name="min"/> elements.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The sequence to check.</param>
        /// <param name="min">The minimum required number of elements, inclusive.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException"><paramref name="source"/> is <see langword="null"/> or has fewer than <paramref name="min"/> elements.</exception>
        public static void MinCount<T>(
            [NotNull] IEnumerable<T>? source,
            int min,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.MinCount(source, min, paramName));

        /// <summary>Throws <paramref name="error"/> as a <see cref="DomainException"/> unless <paramref name="condition"/> holds.</summary>
        /// <param name="condition">The condition that must be <see langword="true"/>.</param>
        /// <param name="error">The error carried by the exception when the condition is <see langword="false"/>.</param>
        /// <exception cref="DomainException"><paramref name="condition"/> is <see langword="false"/>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public static void True([DoesNotReturnIf(false)] bool condition, Error error)
            => ThrowIfError(Against.True(condition, error));

        /// <summary>Throws <paramref name="error"/> as a <see cref="DomainException"/> when <paramref name="condition"/> holds.</summary>
        /// <param name="condition">The condition that must be <see langword="false"/>.</param>
        /// <param name="error">The error carried by the exception when the condition is <see langword="true"/>.</param>
        /// <exception cref="DomainException"><paramref name="condition"/> is <see langword="true"/>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public static void False([DoesNotReturnIf(true)] bool condition, Error error)
            => ThrowIfError(Against.False(condition, error));

        /// <summary>Throws when a raw value does not correspond to a member of a <see cref="SmartEnum{TEnum, TValue}"/>.</summary>
        /// <typeparam name="TEnum">The SmartEnum type.</typeparam>
        /// <typeparam name="TValue">The SmartEnum's underlying value type.</typeparam>
        /// <param name="value">The underlying value to look up.</param>
        /// <param name="paramName">The name used in the message. Supplied by the compiler.</param>
        /// <exception cref="DomainException">No member of <typeparamref name="TEnum"/> has <paramref name="value"/>.</exception>
        public static void InvalidSmartEnum<TEnum, TValue>(
            TValue value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where TEnum : SmartEnum<TEnum, TValue>
            where TValue : IEquatable<TValue>
            => ThrowIfError(Against.InvalidSmartEnum<TEnum, TValue>(value, paramName));
#pragma warning restore CS8777

        private static void ThrowIfError(Error? error)
        {
            if (error is not null)
                throw new DomainException(error);
        }
    }
}
