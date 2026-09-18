using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation;

/// <summary>
/// A value that can only exist in a valid, normalized form, such as <see cref="Iban"/> or
/// <see cref="CardNumber"/>. Every identifier type in this package implements it.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
/// <remarks>
/// <para>
/// Lets generic code work with any identifier: <c>Guard.Against.Invalid&lt;Iban&gt;(value)</c>,
/// FluentValidation's <c>MustBeValid&lt;Iban&gt;()</c>, and <see cref="ValidatedValueJsonConverter{T}"/>.
/// </para>
/// <para>
/// <see cref="IParsable{TSelf}"/> makes the types bind directly from route and query values in
/// ASP.NET Core minimal APIs and MVC. <c>Parse</c> throws <see cref="FormatException"/> with the
/// validation message; use <see cref="Create"/> when invalid input is expected.
/// </para>
/// </remarks>
public interface IValidatedValue<TSelf> : IParsable<TSelf>, IEquatable<TSelf>
    where TSelf : struct, IValidatedValue<TSelf>
{
    /// <summary>
    /// Gets the normalized value: upper case, with spaces and separators removed. Empty for the
    /// <see langword="default"/> instance.
    /// </summary>
    string Value { get; }

    /// <summary>
    /// Validates and normalizes <paramref name="value"/>. Never throws: every failure is an
    /// <c>Error.Validation</c> with a code from <see cref="ValidationErrorCodes"/>, or
    /// <c>validation.required</c> for null, empty or whitespace input.
    /// </summary>
    /// <param name="value">The raw input.</param>
    /// <returns>The value, or the reason it is invalid.</returns>
    static abstract Result<TSelf> Create(string? value);
}
