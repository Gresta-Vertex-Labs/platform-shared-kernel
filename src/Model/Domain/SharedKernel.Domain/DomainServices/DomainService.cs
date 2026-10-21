using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.DomainServices;

/// <summary>
/// Base class for a domain service: stateless domain logic that spans several aggregates or value objects
/// and belongs to none of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Extend this class rather than implementing <see cref="IDomainService"/> directly; an
/// architecture rule checks it.
/// </para>
/// <para>
/// <b>Scope.</b> A domain service receives the domain objects it works on. It does not load or save them
/// (the application layer does), hold a clock, or record events; when something must be recorded as
/// having happened, it tells an aggregate to raise the event.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class FundsTransferService : DomainService
/// {
///     public void Transfer(Account source, Account target, Money amount)
///     {
///         CheckRule(new SufficientFundsRule(source, amount));
///         source.Withdraw(amount);
///         target.Deposit(amount);
///     }
/// }
/// </code>
/// </example>
public abstract class DomainService : IDomainService
{
    /// <summary>
    /// Throws <see cref="BusinessRuleViolationException"/> when <paramref name="rule"/> is broken; otherwise
    /// does nothing.
    /// </summary>
    /// <param name="rule">The business rule to enforce. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// <paramref name="rule"/> is broken; the exception's error carries the rule's code and message.
    /// </exception>
    protected static void CheckRule(IBusinessRule rule) => DomainInvariants.CheckRule(rule);

    /// <summary>
    /// Returns a failed <see cref="Result"/> when <paramref name="rule"/> is broken; otherwise a successful one.
    /// The non-throwing counterpart of <see cref="CheckRule"/>.
    /// </summary>
    /// <param name="rule">The business rule to enforce. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A successful result when the rule holds; otherwise a failure whose <see cref="Error"/> equals the one a
    /// <see cref="BusinessRuleViolationException"/> for the rule carries
    /// (<see cref="ErrorType.BusinessRule"/>, the rule's code and message).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Constructors and factories use <see cref="CheckRule"/>; behavior methods that return a
    /// <see cref="Result"/> use this method and return its failure instead of throwing.
    /// </remarks>
    protected static Result TryCheckRule(IBusinessRule rule) => DomainInvariants.TryCheckRule(rule);
}
