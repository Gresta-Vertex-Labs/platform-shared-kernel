namespace SharedKernel.Domain.BusinessRules;

/// <summary>Extension methods that compose <see cref="IBusinessRule"/> instances with and, or, and not.</summary>
/// <example>
/// <code>
/// CheckRule(new OrderMustBePaidRule(this).And(new OrderMustHaveLinesRule(this)));
/// </code>
/// </example>
public static class BusinessRuleExtensions
{
    /// <summary>
    /// Returns a rule that is broken when either <paramref name="left"/> or <paramref name="right"/> is.
    /// </summary>
    /// <param name="left">The first rule. Must not be null.</param>
    /// <param name="right">The second rule. Must not be null.</param>
    /// <returns>An <see cref="AndBusinessRule"/> reporting the code of the first broken operand.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static AndBusinessRule And(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>
    /// Returns a rule that is broken only when both <paramref name="left"/> and <paramref name="right"/> are.
    /// </summary>
    /// <param name="left">The first alternative, whose code the combined rule reports. Must not be null.</param>
    /// <param name="right">The second alternative. Must not be null.</param>
    /// <returns>An <see cref="OrBusinessRule"/> reporting the left operand's code.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static OrBusinessRule Or(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>Returns a rule that is broken when <paramref name="rule"/> holds.</summary>
    /// <param name="rule">The rule to negate. Must not be null.</param>
    /// <param name="code">
    /// The error code reported when the negated rule is broken. Must not be null or whitespace.
    /// </param>
    /// <param name="message">
    /// The message reported when the negated rule is broken. Must not be null or whitespace.
    /// </param>
    /// <returns>
    /// A <see cref="NotBusinessRule"/> reporting <paramref name="code"/> and <paramref name="message"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="rule"/>, <paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="message"/> is empty or whitespace.
    /// </exception>
    public static NotBusinessRule Not(this IBusinessRule rule, string code, string message) => new(rule, code, message);
}
