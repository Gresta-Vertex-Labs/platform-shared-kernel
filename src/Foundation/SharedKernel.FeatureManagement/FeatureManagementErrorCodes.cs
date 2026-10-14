namespace SharedKernel.FeatureManagement;

/// <summary>
/// The <see cref="SharedKernel.Primitives.Errors.Error.Code"/> values carried by the exceptions of
/// <c>SharedKernel.FeatureManagement</c>.
/// </summary>
/// <remarks>These are wire strings: they are never renamed.</remarks>
public static class FeatureManagementErrorCodes
{
    /// <summary>
    /// A flag declared for startup validation is missing from configuration or has a variant that does not fit
    /// its type. Carried by <see cref="FeatureFlagValidationException"/> as an unexpected error.
    /// </summary>
    public const string InvalidFlagConfiguration = "feature_management.invalid_flag_configuration";
}
