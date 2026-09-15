namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A business invariant: a condition that must hold for the domain to stay valid.
/// </summary>
/// <remarks>
/// <para>
/// Enforce a rule with <c>CheckRule</c> from an aggregate, value object or domain service. A broken rule
/// throws <see cref="Exceptions.BusinessRuleViolationException"/>, whose error carries this rule's
/// <see cref="Code"/> and <see cref="Message"/> and maps to HTTP 422.
/// </para>
/// <para>
/// <b>The code is the contract; the message is for people.</b> Clients branch on <see cref="Code"/>, and
/// localization looks messages up by it, so give every rule its own stable code and never change a
/// published one. Use lowercase, dot-separated segments: <c>order.not_paid</c>,
/// <c>account.insufficient_funds</c>.
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
/// </code>
/// </example>
public interface IBusinessRule
{
    /// <summary>Gets the stable, machine-readable code that identifies this rule, e.g. <c>order.not_paid</c>.</summary>
    string Code { get; }

    /// <summary>Gets a human-readable explanation of the rule, shown when it is broken.</summary>
    string Message { get; }

    /// <summary>Returns <see langword="true"/> when the rule is currently violated.</summary>
    /// <returns><see langword="true"/> when broken; otherwise <see langword="false"/>.</returns>
    bool IsBroken();
}
