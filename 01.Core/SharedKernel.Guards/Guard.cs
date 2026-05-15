using SharedKernel.Core.Exceptions;
using SharedKernel.Guards.Clauses;
using SharedKernel.Guards.Descriptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Guards;

/// <summary>
/// Static entry-point for the two-path guard system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Functional path</b> — chain extension methods off <see cref="Against"/> to receive an
/// <see cref="Error"/>? return value. <c>null</c> means the guard passed; a non-null
/// <see cref="Error"/> means it was violated.
/// </para>
/// <para>
/// <b>Imperative path</b> — call methods on <see cref="Throw"/> to throw
/// <see cref="DomainException"/> immediately on violation.
/// </para>
/// <example>
/// <code>
/// // Functional path
/// Error? error = Guard.Against.Null(value, nameof(value));
///
/// // Imperative path
/// Guard.Throw.Null(value, nameof(value));
/// </code>
/// </example>
/// </remarks>
public static class Guard
{
    // Singleton instance — callers use `Guard.Against` as the extension-method receiver.
    private static readonly IGuardClause _against = new DefaultGuardClause();

    /// <summary>
    /// Entry point for the functional guard path. Chain guard extension methods off this property.
    /// Each extension returns <see langword="null"/> on pass and a non-null <see cref="Error"/> on violation.
    /// </summary>
    public static IGuardClause Against => _against;

    // Private sealed implementation — never exposed to consumers.
    private sealed class DefaultGuardClause : IGuardClause { }

    /// <summary>
    /// Entry point for the imperative guard path. Methods mirror the functional extensions but
    /// throw <see cref="DomainException"/> on violation instead of returning an error.
    /// </summary>
    public static class Throw
    {
        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/>.</summary>
        public static void Null<T>(T? value, string paramName)
            where T : class
        {
            var error = Against.Null(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/> or empty.</summary>
        public static void NullOrEmpty(string? value, string paramName)
        {
            var error = Against.NullOrEmpty(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is <see langword="null"/>, empty, or whitespace.</summary>
        public static void NullOrWhiteSpace(string? value, string paramName)
        {
            var error = Against.NullOrWhiteSpace(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is shorter than <paramref name="minLength"/>.</summary>
        public static void ShorterThan(string value, int minLength, string paramName)
        {
            var error = Against.ShorterThan(value, minLength, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is longer than <paramref name="maxLength"/>.</summary>
        public static void LongerThan(string value, int maxLength, string paramName)
        {
            var error = Against.LongerThan(value, maxLength, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative or zero.</summary>
        public static void NegativeOrZero(int value, string paramName)
        {
            var error = Against.NegativeOrZero(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative or zero.</summary>
        public static void NegativeOrZero(decimal value, string paramName)
        {
            var error = Against.NegativeOrZero(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative or zero.</summary>
        public static void NegativeOrZero(long value, string paramName)
        {
            var error = Against.NegativeOrZero(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative.</summary>
        public static void Negative(int value, string paramName)
        {
            var error = Against.Negative(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative.</summary>
        public static void Negative(decimal value, string paramName)
        {
            var error = Against.Negative(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is negative.</summary>
        public static void Negative(long value, string paramName)
        {
            var error = Against.Negative(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is not positive (i.e., zero or negative).</summary>
        public static void NotPositive(int value, string paramName)
        {
            var error = Against.NotPositive(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is not positive (i.e., zero or negative).</summary>
        public static void NotPositive(decimal value, string paramName)
        {
            var error = Against.NotPositive(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is not positive (i.e., zero or negative).</summary>
        public static void NotPositive(long value, string paramName)
        {
            var error = Against.NotPositive(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is outside the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static void OutOfRange<T>(T value, T min, T max, string paramName)
            where T : IComparable<T>
        {
            var error = Against.OutOfRange(value, min, max, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> equals the default value for its type.</summary>
        public static void Default<T>(T value, string paramName)
        {
            var error = Against.Default(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is <see cref="Guid.Empty"/>.</summary>
        public static void InvalidGuid(Guid value, string paramName)
        {
            var error = Against.InvalidGuid(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> does not match the compiled <paramref name="pattern"/>.</summary>
        public static void InvalidFormat(string value, string pattern, string paramName)
        {
            var error = Against.InvalidFormat(value, pattern, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="value"/> is not a valid email address.</summary>
        public static void Email(string? value, string paramName)
        {
            var error = Against.Email(value, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="source"/> contains no elements.</summary>
        public static void Empty<T>(IEnumerable<T> source, string paramName)
        {
            var error = Against.Empty(source, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="source"/> contains more than <paramref name="max"/> elements.</summary>
        public static void MaxCount<T>(IEnumerable<T> source, int max, string paramName)
        {
            var error = Against.MaxCount(source, max, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>Throws if <paramref name="source"/> contains fewer than <paramref name="min"/> elements.</summary>
        public static void MinCount<T>(IEnumerable<T> source, int min, string paramName)
        {
            var error = Against.MinCount(source, min, paramName);
            if (error is not null) throw new DomainException(error);
        }

        /// <summary>
        /// Throws if <paramref name="condition"/> is <see langword="false"/>,
        /// using the caller-supplied <paramref name="error"/>.
        /// </summary>
        public static void True(bool condition, Error error)
        {
            var result = Against.True(condition, error);
            if (result is not null) throw new DomainException(result);
        }

        /// <summary>
        /// Throws if <paramref name="condition"/> is <see langword="true"/>,
        /// using the caller-supplied <paramref name="error"/>.
        /// </summary>
        public static void False(bool condition, Error error)
        {
            var result = Against.False(condition, error);
            if (result is not null) throw new DomainException(result);
        }

        /// <summary>
        /// Throws if <paramref name="id"/> does not correspond to a known
        /// <typeparamref name="TEnum"/> member.
        /// </summary>
        public static void InvalidSmartEnum<TEnum, TValue>(TValue id)
            where TEnum : SmartEnum<TEnum, TValue>
            where TValue : IEquatable<TValue>
        {
            var error = Against.InvalidSmartEnum<TEnum, TValue>(id);
            if (error is not null) throw new DomainException(error);
        }
    }
}
