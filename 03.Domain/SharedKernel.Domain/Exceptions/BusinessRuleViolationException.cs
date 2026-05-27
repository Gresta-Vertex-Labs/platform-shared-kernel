using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>
/// Thrown when a domain business rule is violated.
/// Carries the <see cref="IBusinessRule"/> instance that was broken so callers can inspect
/// the rule's message or type.
/// </summary>
/// <remarks>
/// <para>
/// This exception is raised by <c>AggregateRoot&lt;TId&gt;.CheckRule(IBusinessRule)</c> when
/// <c>IBusinessRule.IsBroken()</c> returns <see langword="true"/>. Map it to an HTTP 422
/// Unprocessable Entity at the presentation layer — never HTTP 500.
/// </para>
/// <para>
/// Hierarchy: <c>Exception</c> → <c>SharedKernelException</c> → <c>DomainException</c>
/// → <c>BusinessRuleViolationException</c>.
/// Catching <c>DomainException</c> catches this; catching <c>SharedKernelException</c>
/// also catches this. <see cref="Error"/> carries <c>ErrorType.BusinessRule</c>.
/// </para>
/// </remarks>
public sealed class BusinessRuleViolationException : DomainException
{
    /// <summary>
    /// Initialises a new <see cref="BusinessRuleViolationException"/> for the specified
    /// <paramref name="rule"/>.
    /// </summary>
    /// <param name="rule">The business rule that was violated. Must not be <see langword="null"/>.</param>
    public BusinessRuleViolationException(IBusinessRule rule)
        : base(Error.BusinessRule(ErrorCodes.Domain.RuleViolated, rule.Message))
    {
        Rule = rule;
    }

    /// <summary>Gets the business rule that was violated.</summary>
    public IBusinessRule Rule { get; }
}
