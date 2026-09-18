using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation;

namespace SharedKernel.Guards;

/// <summary>
/// Guard clauses for the identifier types, in the same shape as <c>SharedKernel.Core</c>'s guards:
/// <c>Guard.Against.Invalid&lt;Iban&gt;(iban)</c> returns <see langword="null"/> for a valid value
/// and the <see cref="Error"/> otherwise.
/// </summary>
/// <remarks>
/// Null, empty or whitespace-only input returns Core's own <c>Guard.Against.NullOrWhiteSpace</c>
/// error, which names the parameter. Any other failure returns the identifier's error unchanged,
/// with its code from <see cref="ValidationErrorCodes"/> and its values in
/// <see cref="Error.MessageArguments"/>.
/// </remarks>
public static class ValidationGuardExtensions
{
    /// <summary>Checks that <paramref name="value"/> is a valid <typeparamref name="T"/>, such as an <see cref="Iban"/> or <see cref="VatNumber"/>.</summary>
    /// <typeparam name="T">The identifier type.</typeparam>
    /// <param name="guard">The guard entry point, <c>Guard.Against</c>.</param>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name used when the value is missing. Supplied by the compiler.</param>
    /// <returns><see langword="null"/> when the value is valid; otherwise the error.</returns>
    public static Error? Invalid<T>(
        this IGuardClause guard,
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, IValidatedValue<T>
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return guard.NullOrWhiteSpace(value, paramName);
        }

        Result<T> result = T.Create(value);
        return result.IsSuccess ? null : result.Error;
    }

    /// <summary>Checks that <paramref name="value"/> is a valid national ID of <paramref name="country"/>.</summary>
    /// <param name="guard">The guard entry point, <c>Guard.Against</c>.</param>
    /// <param name="value">The number to check.</param>
    /// <param name="country">The issuing country.</param>
    /// <param name="registry">The validators to use; <see cref="NationalIdValidatorRegistry.Default"/> when omitted.</param>
    /// <param name="paramName">The name used when the value is missing. Supplied by the compiler.</param>
    /// <returns><see langword="null"/> when the number is valid; otherwise the error.</returns>
    public static Error? InvalidNationalId(
        this IGuardClause guard,
        string? value,
        CountryCode country,
        NationalIdValidatorRegistry? registry = null,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return guard.NullOrWhiteSpace(value, paramName);
        }

        Result<NationalId> result = NationalId.Create(country, value, registry);
        return result.IsSuccess ? null : result.Error;
    }
}
