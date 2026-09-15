namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A business rule: an invariant bound to its data that must hold for the domain to stay valid, identified by
/// a stable error code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Enforce a rule with <c>CheckRule</c> from an aggregate, value object or domain service. A
/// broken rule throws <see cref="Exceptions.BusinessRuleViolationException"/>, whose error carries this rule's
/// <see cref="Code"/> and <see cref="Message"/> and maps to HTTP 422. Compose rules with
/// <see cref="BusinessRuleExtensions"/>; to enforce a decision about an arbitrary subject, write an
/// <see cref="Policies.IPolicy{T}"/> and convert it with <c>ToRule</c>.
/// </para>
/// <para>
/// <b>Error code.</b> Clients branch on <see cref="Code"/> and localization looks messages up by it, so give
/// every rule its own code and never change a published one. Use lowercase, dot-separated segments such as
/// <c>order.not_paid</c>, with no variable data in the code; put identifiers in <see cref="Message"/>.
/// </para>
/// <para>
/// <b>Pitfall.</b> <see cref="IsBroken"/> can be called more than once for a single check, by
/// <c>CheckRule</c> and by composite rules. Keep it free of side effects and deterministic for the same state.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderMustBePaidRule(Order order) : IBusinessRule
/// {
///     public string Code =&gt; "order.not_paid";
///     public string Message =&gt; "The order must be paid before it ships.";
///     public bool IsBroken() =&gt; !order.IsPaid;
/// }
///
/// // Inside the Order aggregate:
/// CheckRule(new OrderMustBePaidRule(this));
/// </code>
/// </example>
public interface IBusinessRule
{
    /// <summary>Gets the stable, machine-readable error code of this rule, such as <c>order.not_paid</c>.</summary>
    string Code { get; }

    /// <summary>Gets the human-readable explanation reported when the rule is broken.</summary>
    string Message { get; }

    /// <summary>Returns whether the rule is currently violated by the data it is bound to.</summary>
    /// <returns><see langword="true"/> when the rule is broken; otherwise <see langword="false"/>.</returns>
    bool IsBroken();
}
