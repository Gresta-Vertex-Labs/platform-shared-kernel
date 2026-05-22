namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// Represents a domain business rule — an invariant that must hold for the domain to remain
/// in a valid state.
/// </summary>
/// <remarks>
/// Implement this interface for each distinct business rule. Pass an instance to
/// <c>AggregateRoot&lt;TId&gt;.CheckRule(IBusinessRule)</c> to enforce it.
/// </remarks>
public interface IBusinessRule
{
    /// <summary>Gets a human-readable description of this rule and why it exists.</summary>
    string Message { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this rule is currently violated.
    /// </summary>
    bool IsBroken();
}
