namespace SharedKernel.Domain.Policies;

/// <summary>
/// A policy: a reusable domain decision about any subject passed to it, answering whether the subject
/// complies and, when it does not, why.
/// </summary>
/// <typeparam name="T">The type of subject the policy evaluates. Contravariant.</typeparam>
/// <remarks>
/// <para>
/// <b>Policy, business rule or specification?</b>
/// </para>
/// <list type="table">
/// <listheader><term>Type</term><description>Use it for</description></listheader>
/// <item>
/// <term><see cref="IPolicy{T}"/></term>
/// <description>
/// A decision the caller may act on or report, applied to any subject: "is this customer eligible for the
/// discount?"
/// </description>
/// </item>
/// <item>
/// <term><see cref="BusinessRules.IBusinessRule"/></term>
/// <description>An invariant bound to its data, enforced with <c>CheckRule</c>, which throws when broken.</description>
/// </item>
/// <item>
/// <term><see cref="Specifications.Specification{T}"/></term>
/// <description>A query that the persistence layer translates to SQL.</description>
/// </item>
/// </list>
/// <para>
/// <b>Enforcement.</b> To enforce a decision rather than report it, convert it with
/// <see cref="PolicyExtensions.ToRule{T}(IPolicy{T}, T, string)"/> and pass the result to <c>CheckRule</c>.
/// Compose policies with <see cref="PolicyExtensions"/>.
/// </para>
/// <para>
/// <b>Pitfall.</b> <see cref="Explain"/> has a default implementation, which C# exposes only through the
/// interface. A class that does not declare its own <c>Explain</c> must be called through an
/// <see cref="IPolicy{T}"/>-typed variable, and its explanation names only the policy type. Implement
/// <see cref="Explain"/> with a message a person can act on.
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
    /// <summary>Returns whether <paramref name="subject"/> complies with the policy.</summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns><see langword="true"/> when the subject complies; otherwise <see langword="false"/>.</returns>
    bool IsCompliant(T subject);

    /// <summary>
    /// Returns why <paramref name="subject"/> does not comply, or an empty string when it does.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// <see cref="string.Empty"/> when the subject complies; otherwise a non-empty explanation. The default
    /// implementation returns <c>Policy '{TypeName}' is not satisfied.</c>
    /// </returns>
    /// <remarks>Implementations must return <see cref="string.Empty"/> exactly when the subject complies.</remarks>
    string Explain(T subject) =>
        IsCompliant(subject) ? string.Empty : $"Policy '{GetType().Name}' is not satisfied.";
}
