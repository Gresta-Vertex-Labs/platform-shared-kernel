using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Framework-agnostic assertion helpers over <see cref="IBusinessRule"/>.
/// </summary>
/// <remarks>
/// Both assertions throw <see cref="InvalidOperationException"/> carrying <see cref="IBusinessRule.Message"/>
/// on failure. Zero dependency on xUnit, NUnit, or FluentAssertions.
/// </remarks>
public static class BusinessRuleAssertions
{
    /// <summary>Asserts that <paramref name="rule"/> is currently broken.</summary>
    /// <param name="rule">The business rule to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="rule"/>.<see cref="IBusinessRule.IsBroken"/> returns <see langword="false"/>.</exception>
    public static void ShouldBeBroken(this IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (!rule.IsBroken())
        {
            throw new InvalidOperationException(
                $"Expected business rule to be broken, but it was not. Rule message: \"{rule.Message}\".");
        }
    }

    /// <summary>Asserts that <paramref name="rule"/> is not currently broken.</summary>
    /// <param name="rule">The business rule to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="rule"/>.<see cref="IBusinessRule.IsBroken"/> returns <see langword="true"/>.</exception>
    public static void ShouldNotBeBroken(this IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
        {
            throw new InvalidOperationException(
                $"Expected business rule not to be broken, but it was. Rule message: \"{rule.Message}\".");
        }
    }
}
