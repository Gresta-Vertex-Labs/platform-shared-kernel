using SharedKernel.Primitives.Errors;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>Builds the canonical <see cref="Error"/> for each classified failure of this package.</summary>
internal static class FeatureManagementErrors
{
    public static Error InvalidFlagConfiguration() =>
        Error.Unexpected(
            FeatureManagementErrorCodes.InvalidFlagConfiguration,
            "Feature flag configuration is invalid.");
}
