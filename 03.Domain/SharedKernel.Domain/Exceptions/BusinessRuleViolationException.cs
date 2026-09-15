using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>The exception thrown when a business rule is broken, carrying the rule and its error code.</summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> <c>CheckRule</c> on an aggregate, value object or domain service throws it; construct it
/// directly only when enforcing a rule outside those bases.
/// </para>
/// <para>
/// <b>Error.</b> <see cref="SharedKernelException.Error"/> is an <see cref="ErrorType.BusinessRule"/> error,
/// mapped to HTTP 422, whose code and message are the rule's <see cref="IBusinessRule.Code"/> and
/// <see cref="IBusinessRule.Message"/>, read once when the exception is constructed.
/// </para>
/// <para>
/// <b>Hierarchy.</b> Derives from <see cref="DomainException"/>, so a handler for <see cref="DomainException"/>
/// also catches it, and <c>TryCreate</c> turns it into a failed result.
/// </para>
/// </remarks>
public sealed class BusinessRuleViolationException : DomainException
{
    /// <summary>Initializes a new exception for the broken <paramref name="rule"/>.</summary>
    /// <param name="rule">The rule that was broken. Must not be null.</param>
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
