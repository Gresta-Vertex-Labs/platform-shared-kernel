using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Internal;

/// <summary>
/// The single implementation of null checking, business rule enforcement and exception-to-result creation
/// shared by aggregates, value objects, strongly-typed identifiers and domain services.
/// </summary>
internal static class DomainInvariants
{
    /// <summary>
    /// Returns <paramref name="value"/>, or throws <see cref="DomainException"/> with the same
    /// <c>Required</c> validation error <c>Guard.Throw.Null</c> produces when it is <see langword="null"/>.
    /// Exists for unconstrained type parameters, which cannot call that guard.
    /// </summary>
    internal static T NotNull<T>(T value, string paramName)
    {
        if (value is null)
        {
            throw new DomainException(
                Error.Validation(ErrorCodes.Validation.Required, $"'{paramName}' must not be null."));
        }

        return value;
    }

    /// <summary>
    /// Throws <see cref="BusinessRuleViolationException"/> when <paramref name="rule"/> is broken, or
    /// <see cref="ArgumentNullException"/> when it is <see langword="null"/>.
    /// </summary>
    internal static void CheckRule(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }

    /// <summary>
    /// Runs <paramref name="factory"/>, returning every error of a <see cref="ValidationException"/> or the
    /// single error of any other <see cref="DomainException"/> as a failed result; other exceptions propagate.
    /// </summary>
    internal static ValidationResult<T> TryCreate<T>(Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        try
        {
            return ValidationResult<T>.Success(factory());
        }
        catch (ValidationException ex)
        {
            IReadOnlyList<Error> errors = ex.Errors.Count > 0 ? ex.Errors : [ex.Error];
            return ValidationResult<T>.Failure(errors);
        }
        catch (DomainException ex)
        {
            // BusinessRuleViolationException and every Guard.Throw violation derive from DomainException.
            return ValidationResult<T>.Failure([ex.Error]);
        }
    }
}
