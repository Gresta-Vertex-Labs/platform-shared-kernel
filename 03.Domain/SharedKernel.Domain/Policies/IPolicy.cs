namespace SharedKernel.Domain.Policies;

/// <summary>
/// Defines a domain policy that evaluates whether a subject of type <typeparamref name="T"/>
/// is compliant with the policy's rules.
/// </summary>
/// <typeparam name="T">The type of domain object evaluated by this policy.</typeparam>
/// <remarks>
/// <para>
/// <b>Policy vs Business Rule distinction:</b><br/>
/// An <see cref="IPolicy{T}"/> evaluates whether a domain object (of type <typeparamref name="T"/>) satisfies
/// a compliance criterion — it operates on rich domain objects (e.g., "is this order eligible for a discount?").<br/>
/// An <see cref="SharedKernel.Domain.BusinessRules.IBusinessRule"/> validates raw primitive invariants (strings, numbers, enums)
/// and returns a broken/not-broken signal with a message. Use <c>IBusinessRule</c> inside value objects and aggregate
/// constructors; use <see cref="IPolicy{T}"/> when the evaluation target is a full domain object.
/// </para>
/// </remarks>
public interface IPolicy<T>
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="subject"/> is compliant with this policy.
    /// </summary>
    /// <param name="subject">The domain object to evaluate.</param>
    bool IsCompliant(T subject);
}
