using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Internal;

/// <summary>
/// The single implementation of rule enforcement and exception-to-result creation shared by
/// aggregates, value objects and domain services, so the three cannot drift apart.
/// </summary>
internal static class DomainInvariants
{
    /// <summary>
    /// Throws the same error <c>Guard.Throw.Null</c> produces, for a type parameter that is not constrained to
    /// a class or struct and so cannot call it.
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

    internal static void CheckRule(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }

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
