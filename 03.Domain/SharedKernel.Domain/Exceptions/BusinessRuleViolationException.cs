using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>Thrown when a business rule is broken.</summary>
/// <remarks>
/// <see cref="SharedKernelException.Error"/> is an <see cref="ErrorType.BusinessRule"/> error (HTTP 422)
/// carrying the rule's own <see cref="IBusinessRule.Code"/> and <see cref="IBusinessRule.Message"/>.
/// The hierarchy is <c>SharedKernelException</c>, then <c>DomainException</c>, then this type, so catching
/// <c>DomainException</c> catches it.
/// </remarks>
public sealed class BusinessRuleViolationException : DomainException
{
    /// <summary>Creates the exception for the broken <paramref name="rule"/>.</summary>
    /// <param name="rule">The rule that was broken.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    public BusinessRuleViolationException(IBusinessRule rule)
        : base(CreateError(rule))
    {
        Rule = rule;
    }

    /// <summary>Gets the rule that was broken.</summary>
    public IBusinessRule Rule { get; }

    private static Error CreateError(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Error.BusinessRule(rule.Code, rule.Message);
    }
}
