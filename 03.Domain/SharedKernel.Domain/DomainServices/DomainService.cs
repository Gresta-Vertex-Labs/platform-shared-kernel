using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Internal;

namespace SharedKernel.Domain.DomainServices;

/// <summary>
/// Base class for domain services: stateless domain logic that spans several aggregates or value objects and
/// belongs to none of them.
/// </summary>
/// <remarks>
/// A domain service receives the domain objects it works on. It does not load or save them (the application
/// layer does), hold a clock, or accumulate events; when something must be recorded as having happened, it
/// tells an aggregate to raise the event. Extend this class rather than implementing
/// <see cref="IDomainService"/> directly; an architecture rule enforces it.
/// </remarks>
public abstract class DomainService : IDomainService
{
    /// <summary>Throws when <paramref name="rule"/> is broken.</summary>
    /// <param name="rule">The rule to enforce.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException"><paramref name="rule"/> is broken.</exception>
    protected static void CheckRule(IBusinessRule rule) => DomainInvariants.CheckRule(rule);
}
