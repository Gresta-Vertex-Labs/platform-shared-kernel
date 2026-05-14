namespace SharedKernel.FeatureManagement.Abstractions;

/// <summary>
/// Determines whether named features are enabled at runtime. Inject this interface
/// in consuming services — never inject <c>Microsoft.FeatureManagement.IFeatureManager</c>
/// directly, which couples the caller to a specific implementation.
/// </summary>
/// <remarks>
/// <para>
/// Register the implementation via <c>services.AddSharedKernelFeatureManagement(configuration)</c>.
/// The default adapter wraps <c>Microsoft.FeatureManagement</c>.
/// </para>
/// <para>
/// Feature names should be stable string constants defined alongside the feature that uses them.
/// Consider defining them as <c>public const string FeatureName = "..."</c> fields adjacent to
/// the feature's entry point.
/// </para>
/// </remarks>
public interface IFeatureManager
{
    /// <summary>
    /// Returns <c>true</c> if the feature identified by <paramref name="feature"/> is currently enabled.
    /// </summary>
    /// <param name="feature">The stable name of the feature flag.</param>
    /// <param name="ct">A token that may request cancellation of the check.</param>
    ValueTask<bool> IsEnabledAsync(string feature, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the feature identified by <paramref name="feature"/> is currently
    /// enabled for the given <paramref name="context"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type used for context-aware feature evaluation
    /// (e.g., a tenant identifier, a user object).
    /// </typeparam>
    /// <param name="feature">The stable name of the feature flag.</param>
    /// <param name="context">The evaluation context passed to context-aware filters.</param>
    /// <param name="ct">A token that may request cancellation of the check.</param>
    ValueTask<bool> IsEnabledAsync<TContext>(string feature, TContext context, CancellationToken ct = default);
}
