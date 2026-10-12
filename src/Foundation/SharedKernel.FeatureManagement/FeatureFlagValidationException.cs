using SharedKernel.Core.Exceptions;
using SharedKernel.FeatureManagement.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Thrown at startup when a flag passed to <see cref="FeatureFlagOptions.ValidateOnStart"/> is missing from
/// configuration or has a variant whose value does not fit the flag's type.
/// </summary>
/// <remarks>
/// Its <see cref="SharedKernelException.Error"/> has the code
/// <see cref="FeatureManagementErrorCodes.InvalidFlagConfiguration"/> and <see cref="ErrorType.Unexpected"/>
/// (a configuration fault, never an outage). <see cref="Exception.Message"/> lists every problem; each line
/// names only a flag key, a variant name and a fixed reason, never a configuration value.
/// </remarks>
public sealed class FeatureFlagValidationException : SharedKernelException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="failures">One line per problem.</param>
    public FeatureFlagValidationException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures), FeatureManagementErrors.InvalidFlagConfiguration())
    {
        Failures = failures;
    }

    /// <summary>Creates the exception from an explicit error. <see cref="Failures"/> is empty.</summary>
    /// <param name="error">The error describing the failure.</param>
    public FeatureFlagValidationException(Error error)
        : base(error)
    {
        Failures = [];
    }

    /// <summary>Creates the exception from an explicit error and its cause. <see cref="Failures"/> is empty.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public FeatureFlagValidationException(Error error, Exception innerException)
        : base(error, innerException)
    {
        Failures = [];
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
