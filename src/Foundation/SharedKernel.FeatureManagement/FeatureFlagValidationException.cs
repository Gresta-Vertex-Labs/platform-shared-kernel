namespace SharedKernel.FeatureManagement;

/// <summary>
/// Thrown at startup when a flag passed to <see cref="FeatureFlagOptions.ValidateOnStart"/> is missing from
/// configuration or has a variant whose value does not fit the flag's type.
/// </summary>
public sealed class FeatureFlagValidationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="failures">One line per problem.</param>
    public FeatureFlagValidationException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    /// <summary>One line per problem, in the order the flags were declared.</summary>
    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return "Feature flag configuration is invalid:" + Environment.NewLine + "- "
            + string.Join(Environment.NewLine + "- ", failures);
    }
}
