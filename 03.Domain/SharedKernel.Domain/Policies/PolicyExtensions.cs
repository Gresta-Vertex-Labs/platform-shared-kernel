using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Domain.Policies;

/// <summary>
/// Extension methods that compose <see cref="IPolicy{T}"/> instances and convert a policy decision into a
/// business rule.
/// </summary>
public static class PolicyExtensions
{
    /// <summary>
    /// Returns a policy satisfied only when both <paramref name="left"/> and <paramref name="right"/> are.
    /// </summary>
    /// <typeparam name="T">The type of subject the policies evaluate.</typeparam>
    /// <param name="left">The first policy. Must not be null.</param>
    /// <param name="right">The second policy. Must not be null.</param>
    /// <returns>An <see cref="AndPolicy{T}"/> combining both policies.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static AndPolicy<T> And<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>
    /// Returns a policy satisfied when <paramref name="left"/>, <paramref name="right"/>, or both are.
    /// </summary>
    /// <typeparam name="T">The type of subject the policies evaluate.</typeparam>
    /// <param name="left">The first alternative. Must not be null.</param>
    /// <param name="right">The second alternative. Must not be null.</param>
    /// <returns>An <see cref="OrPolicy{T}"/> combining both alternatives.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static OrPolicy<T> Or<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>Returns a policy satisfied when <paramref name="policy"/> is not.</summary>
    /// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
    /// <param name="policy">The policy to negate. Must not be null.</param>
    /// <param name="explanation">
    /// The explanation returned when a subject complies with <paramref name="policy"/>. Must not be null or
    /// whitespace.
    /// </param>
    /// <returns>A <see cref="NotPolicy{T}"/> reporting <paramref name="explanation"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="policy"/> or <paramref name="explanation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="explanation"/> is empty or whitespace.</exception>
    public static NotPolicy<T> Not<T>(this IPolicy<T> policy, string explanation) => new(policy, explanation);

    /// <summary>
    /// Binds <paramref name="policy"/> to <paramref name="subject"/> as a business rule that is broken when the
    /// subject does not comply, so the decision can be enforced with <c>CheckRule</c>.
    /// </summary>
    /// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
    /// <param name="policy">The policy to enforce. Must not be null.</param>
    /// <param name="subject">The subject the rule evaluates. Not checked for null.</param>
    /// <param name="code">
    /// The error code reported when the subject does not comply, such as <c>order.discount_not_allowed</c>.
    /// Must not be null or whitespace.
    /// </param>
    /// <returns>
    /// A business rule with <paramref name="code"/> as its code and the policy's explanation for
    /// <paramref name="subject"/> as its message.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="policy"/> or <paramref name="code"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty or whitespace.</exception>
    /// <remarks>
    /// <b>Evaluation.</b> The rule evaluates the policy each time it is checked, not when it is created, so it
    /// sees the subject's state at check time. Its message is built by calling <see cref="IPolicy{T}.Explain"/>
    /// through the interface.
    /// </remarks>
    /// <example>
    /// <code>
    /// CheckRule(new DiscountEligibilityPolicy().ToRule(customer, "order.discount_not_allowed"));
    /// </code>
    /// </example>
    public static IBusinessRule ToRule<T>(this IPolicy<T> policy, T subject, string code)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new PolicyRule<T>(policy, subject, code);
    }

    private sealed class PolicyRule<T>(IPolicy<T> policy, T subject, string code) : IBusinessRule
    {
        public string Code { get; } = code;

        public string Message => policy.Explain(subject);

        public bool IsBroken() => !policy.IsCompliant(subject);
    }
}
