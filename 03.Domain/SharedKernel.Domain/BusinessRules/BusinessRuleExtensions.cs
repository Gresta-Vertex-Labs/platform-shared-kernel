namespace SharedKernel.Domain.BusinessRules;

/// <summary>Composes <see cref="IBusinessRule"/> instances.</summary>
public static class BusinessRuleExtensions
{
    /// <summary>Returns a rule that is broken when either <paramref name="left"/> or <paramref name="right"/> is.</summary>
    /// <param name="left">The first rule.</param>
    /// <param name="right">The second rule.</param>
    /// <returns>The combined rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public static AndBusinessRule And(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>Returns a rule that is broken only when both <paramref name="left"/> and <paramref name="right"/> are.</summary>
    /// <param name="left">The first alternative.</param>
    /// <param name="right">The second alternative.</param>
    /// <returns>The combined rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public static OrBusinessRule Or(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>Returns a rule that is broken when <paramref name="rule"/> holds.</summary>
    /// <param name="rule">The rule to negate.</param>
    /// <param name="code">The code reported when the negated rule is broken.</param>
    /// <param name="message">The message reported when the negated rule is broken.</param>
    /// <returns>The negated rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="message"/> is null, empty or whitespace.</exception>
    public static NotBusinessRule Not(this IBusinessRule rule, string code, string message) => new(rule, code, message);
}
