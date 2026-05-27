using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;

namespace SharedKernel.Domain.DomainServices;

/// <summary>
/// Abstract base class for domain services — domain logic that does not naturally belong to a
/// single aggregate root or value object.
/// </summary>
/// <remarks>
/// <para>
/// All domain service implementations must extend <see cref="DomainService"/> rather than
/// implementing <see cref="IDomainService"/> directly. Extending this class gives access to
/// <see cref="CheckRule"/> without any infrastructure coupling.
/// </para>
/// <para>
/// <strong>Explicit exclusions — domain services must NOT contain:</strong>
/// <list type="bullet">
///   <item><description>
///     <strong>Clock injection (<c>IClock</c>):</strong> Domain services are stateless logic
///     orchestrators. Time-sensitive domain decisions belong on aggregate roots or value objects
///     that hold the relevant state.
///   </description></item>
///   <item><description>
///     <strong>Domain event accumulation (<c>DomainEvents</c>):</strong> Only aggregate roots
///     raise and accumulate domain events. A domain service may instruct an aggregate to raise
///     an event, but the service itself never owns an event list.
///   </description></item>
///   <item><description>
///     <strong>Repository access:</strong> Domain services operate on domain objects passed to
///     them by the application layer. They must not load or persist aggregates — that is the
///     application layer's responsibility.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class DomainService : IDomainService
{
    /// <summary>
    /// Evaluates <paramref name="rule"/> and throws <see cref="BusinessRuleViolationException"/>
    /// if the rule is broken.
    /// </summary>
    /// <param name="rule">The business rule to enforce.</param>
    /// <exception cref="BusinessRuleViolationException">Thrown when <paramref name="rule"/> is broken.</exception>
    protected static void CheckRule(IBusinessRule rule)
    {
        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }
}
