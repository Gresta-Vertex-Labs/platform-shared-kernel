namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// Fluent extension methods for composing <see cref="IBusinessRule"/> instances.
/// </summary>
public static class BusinessRuleExtensions
{
    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="AndBusinessRule"/> that is broken when either sub-rule is broken.
    /// </summary>
    public static AndBusinessRule And(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="OrBusinessRule"/> that is broken only when both sub-rules are broken.
    /// </summary>
    public static OrBusinessRule Or(this IBusinessRule left, IBusinessRule right) => new(left, right);

    /// <summary>
    /// Wraps <paramref name="rule"/> in a <see cref="NotBusinessRule"/> that inverts its broken state.
    /// </summary>
    public static NotBusinessRule Not(this IBusinessRule rule) => new(rule);
}
