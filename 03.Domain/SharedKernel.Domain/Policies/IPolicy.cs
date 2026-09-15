namespace SharedKernel.Domain.Policies;

/// <summary>
/// A reusable domain decision about a subject, answering whether it complies and, when it does not, why.
/// </summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
/// <remarks>
/// <para>
/// <b>Policy, rule or specification?</b>
/// </para>
/// <list type="table">
/// <listheader><term>Type</term><description>Use it for</description></listheader>
/// <item><term><see cref="IPolicy{T}"/></term><description>A decision applied to any subject passed in, which the caller may act on: "is this customer eligible for the discount?"</description></item>
/// <item><term><see cref="BusinessRules.IBusinessRule"/></term><description>An invariant bound to its data, enforced with <c>CheckRule</c>, which throws when broken.</description></item>
/// <item><term><see cref="Specifications.Specification{T}"/></term><description>A query filter that the persistence layer translates to SQL.</description></item>
/// </list>
/// <para>
/// When a policy decision must be enforced rather than merely reported, convert it with
/// <see cref="PolicyExtensions.ToRule{T}(IPolicy{T}, T, string)"/> and pass the result to <c>CheckRule</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class DiscountEligibilityPolicy : IPolicy&lt;Customer&gt;
/// {
///     public bool IsCompliant(Customer customer) =&gt; customer.OrderCount &gt;= 5;
///
///     public string Explain(Customer customer) =&gt;
///         IsCompliant(customer) ? string.Empty : "A discount needs at least five previous orders.";
/// }
/// </code>
/// </example>
public interface IPolicy<in T>
{
    /// <summary>Returns <see langword="true"/> when <paramref name="subject"/> complies with the policy.</summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns><see langword="true"/> when compliant; otherwise <see langword="false"/>.</returns>
    bool IsCompliant(T subject);

    /// <summary>
    /// Explains why <paramref name="subject"/> does not comply, or returns an empty string when it does.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>The explanation, or <see cref="string.Empty"/> when the subject complies.</returns>
    /// <remarks>
    /// The default names the policy type. Override it with an explanation a person can act on.
    /// </remarks>
    string Explain(T subject) =>
        IsCompliant(subject) ? string.Empty : $"Policy '{GetType().Name}' is not satisfied.";
}
