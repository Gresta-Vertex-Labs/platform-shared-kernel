using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Domain.Policies;

/// <summary>Composes <see cref="IPolicy{T}"/> instances and converts a policy decision into a business rule.</summary>
public static class PolicyExtensions
{
    /// <summary>Returns a policy satisfied only when both <paramref name="left"/> and <paramref name="right"/> are.</summary>
    /// <typeparam name="T">The type of subject the policies evaluate.</typeparam>
    /// <param name="left">The first policy.</param>
    /// <param name="right">The second policy.</param>
    /// <returns>The combined policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public static AndPolicy<T> And<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>Returns a policy satisfied when <paramref name="left"/> or <paramref name="right"/> is.</summary>
    /// <typeparam name="T">The type of subject the policies evaluate.</typeparam>
    /// <param name="left">The first alternative.</param>
    /// <param name="right">The second alternative.</param>
    /// <returns>The combined policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public static OrPolicy<T> Or<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>Returns a policy satisfied when <paramref name="policy"/> is not.</summary>
    /// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
    /// <param name="policy">The policy to negate.</param>
    /// <param name="explanation">The explanation returned when a subject complies with <paramref name="policy"/>.</param>
    /// <returns>The negated policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="explanation"/> is null, empty or whitespace.</exception>
    public static NotPolicy<T> Not<T>(this IPolicy<T> policy, string explanation) => new(policy, explanation);

    /// <summary>
    /// Binds <paramref name="policy"/> to <paramref name="subject"/> as a business rule that is broken when the
    /// subject does not comply, so the decision can be enforced with <c>CheckRule</c>.
    /// </summary>
    /// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
    /// <param name="policy">The policy to enforce.</param>
    /// <param name="subject">The subject the rule evaluates.</param>
    /// <param name="code">The rule code reported when the subject does not comply, e.g. <c>order.discount_not_allowed</c>.</param>
    /// <returns>A rule whose message is the policy's explanation for <paramref name="subject"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty or whitespace.</exception>
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
