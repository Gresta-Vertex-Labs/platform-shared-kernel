using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Guards;

/// <summary>
/// Entry point for the two guard paths.
/// </summary>
/// <remarks>
/// <para>
/// <b>Functional path</b>: <see cref="Against"/>. Each guard returns <see langword="null"/> when it passes
/// and a non-null <see cref="Error"/> when it is violated. Use it where a failure is an expected outcome:
/// factory methods and <see cref="Result{T}"/>-returning code.
/// </para>
/// <para>
/// <b>Imperative path</b>: <see cref="Throw"/>. The same guards, with the same names and parameters, throw
/// <see cref="DomainException"/> carrying the same <see cref="Error"/>. Use it in constructors and anywhere
/// a violation means an invariant was broken.
/// </para>
/// <para>
/// The parameter name is captured by the compiler, so <c>nameof(...)</c> is not needed.
/// </para>
/// <example>
/// <code>
/// // Functional path: first failure wins
/// Result&lt;Customer&gt; result = (Guard.Against.NullOrWhiteSpace(name) ?? Guard.Against.Email(email))
///     .ToResult(() => new Customer(name, email));
///
/// // Functional path: every failure collected
/// ValidationResult validation = Guard.Collect(
///     Guard.Against.NullOrWhiteSpace(name),
///     Guard.Against.Email(email));
///
/// // Imperative path
/// Guard.Throw.NullOrWhiteSpace(name);
/// </code>
/// </example>
/// </remarks>
public static class Guard
{
    private static readonly IGuardClause _against = new DefaultGuardClause();

    /// <summary>
    /// Entry point for the functional guard path. Each guard returns <see langword="null"/> on pass and a
    /// non-null <see cref="Error"/> on violation.
    /// </summary>
    public static IGuardClause Against => _against;

    /// <summary>
    /// Runs every guard result into one <see cref="ValidationResult"/>, keeping every failure in order.
    /// </summary>
    /// <param name="errors">The results of several <see cref="Against"/> guards.</param>
    /// <returns>
    /// A successful <see cref="ValidationResult"/> when every entry is <see langword="null"/>; otherwise a
    /// failed one carrying every non-null <see cref="Error"/>.
    /// </returns>
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
    /// Entry point for the imperative guard path. Every method mirrors a guard on <see cref="Against"/>
    /// and throws <see cref="DomainException"/> carrying that guard's <see cref="Error"/> on violation.
    /// </summary>
    public static class Throw
    {
        // [NotNull] tells callers the argument is non-null once the method returns. Each body proves that by
        // throwing on the guard's error, which flow analysis cannot follow into ThrowIfError, hence CS8777.
#pragma warning disable CS8777
        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/>.</summary>
        public static void Null<T>(
            [NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : class
            => ThrowIfError(Against.Null(value, paramName));

        /// <summary>Throws if the nullable value type <paramref name="value"/> has no value.</summary>
        public static void Null<T>(
            [NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : struct
            => ThrowIfError(Against.Null(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/> or empty.</summary>
        public static void NullOrEmpty(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NullOrEmpty(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/>, empty, or only whitespace.</summary>
        public static void NullOrWhiteSpace(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NullOrWhiteSpace(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/> or shorter than <paramref name="minLength"/>.</summary>
        public static void ShorterThan(
            [NotNull] string? value,
            int minLength,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.ShorterThan(value, minLength, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/> or longer than <paramref name="maxLength"/>.</summary>
        public static void LongerThan(
            [NotNull] string? value,
            int maxLength,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.LongerThan(value, maxLength, paramName));

        /// <summary>Throws if <paramref name="value"/> is less than zero.</summary>
        public static void Negative<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumber<T>
            => ThrowIfError(Against.Negative(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is zero or less.</summary>
        public static void NegativeOrZero<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumber<T>
            => ThrowIfError(Against.NegativeOrZero(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is outside [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static void OutOfRange<T>(
            [NotNull] T value,
            T min,
            T max,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.OutOfRange(value, min, max, paramName));

        /// <summary>Throws if <paramref name="value"/> is less than <paramref name="min"/>.</summary>
        public static void LessThan<T>(
            [NotNull] T value,
            T min,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.LessThan(value, min, paramName));

        /// <summary>Throws if <paramref name="value"/> is greater than <paramref name="max"/>.</summary>
        public static void GreaterThan<T>(
            [NotNull] T value,
            T max,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
            => ThrowIfError(Against.GreaterThan(value, max, paramName));

        /// <summary>Throws if <paramref name="value"/> equals the default value for its type.</summary>
        public static void Default<T>(
            T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.Default(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see cref="Guid.Empty"/>.</summary>
        public static void InvalidGuid(
            Guid value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.InvalidGuid(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is not a named member of <typeparamref name="TEnum"/>.</summary>
        public static void InvalidEnumValue<TEnum>(
            TEnum value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where TEnum : struct, Enum
            => ThrowIfError(Against.InvalidEnumValue(value, paramName));

        /// <summary>Throws if <paramref name="value"/> does not have a zero offset.</summary>
        public static void NotUtc(
            DateTimeOffset value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NotUtc(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is not of kind <see cref="DateTimeKind.Utc"/>.</summary>
        public static void NotUtc(
            DateTime value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.NotUtc(value, paramName));

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/> or does not match <paramref name="pattern"/>.</summary>
        public static void InvalidFormat(
            [NotNull] string? value,
            [StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.InvalidFormat(value, pattern, paramName));

        /// <summary>Throws if <paramref name="value"/> is not a plausible email address.</summary>
        public static void Email(
            [NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            => ThrowIfError(Against.Email(value, paramName));

        /// <summary>Throws if <paramref name="source"/> is <see langword="null"/> or contains no elements.</summary>
        public static void Empty<T>(
            [NotNull] IEnumerable<T>? source,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.Empty(source, paramName));

        /// <summary>Throws if <paramref name="source"/> is <see langword="null"/> or contains more than <paramref name="max"/> elements.</summary>
        public static void MaxCount<T>(
            [NotNull] IEnumerable<T>? source,
            int max,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.MaxCount(source, max, paramName));

        /// <summary>Throws if <paramref name="source"/> is <see langword="null"/> or contains fewer than <paramref name="min"/> elements.</summary>
        public static void MinCount<T>(
            [NotNull] IEnumerable<T>? source,
            int min,
            [CallerArgumentExpression(nameof(source))] string? paramName = null)
            => ThrowIfError(Against.MinCount(source, min, paramName));

        /// <summary>Throws <paramref name="error"/> if <paramref name="condition"/> is <see langword="false"/>.</summary>
        public static void True([DoesNotReturnIf(false)] bool condition, Error error)
            => ThrowIfError(Against.True(condition, error));

        /// <summary>Throws <paramref name="error"/> if <paramref name="condition"/> is <see langword="true"/>.</summary>
        public static void False([DoesNotReturnIf(true)] bool condition, Error error)
            => ThrowIfError(Against.False(condition, error));

        /// <summary>Throws if <paramref name="value"/> does not correspond to a member of <typeparamref name="TEnum"/>.</summary>
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
