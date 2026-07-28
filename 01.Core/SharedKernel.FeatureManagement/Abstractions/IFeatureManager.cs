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

    /// <summary>
    /// Gets the variant assigned to the given <paramref name="feature"/>, without any targeting
    /// context. Use this overload for features whose allocation does not depend on per-caller
    /// targeting (e.g. a feature with only a <c>DefaultWhenEnabled</c>/<c>DefaultWhenDisabled</c>
    /// allocation branch, no percentage/user/group targeting).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in P-298/WO-049 — bridges <c>Microsoft.FeatureManagement</c>'s variant/allocation
    /// support the same way <see cref="IsEnabledAsync(string, CancellationToken)"/> already bridges
    /// plain boolean evaluation. No <c>Microsoft.FeatureManagement</c> type is ever exposed through
    /// this member's signature.
    /// </para>
    /// <para>
    /// A feature with no configured variants, an unresolvable allocation, or an unknown feature
    /// name never throws — it returns the deterministic fallback <see cref="FeatureVariant.Unassigned"/>.
    /// </para>
    /// <para>
    /// Purely additive: this member does not change the behavior of the existing
    /// <see cref="IsEnabledAsync(string, CancellationToken)"/>/<see cref="IsEnabledAsync{TContext}(string, TContext, CancellationToken)"/>
    /// boolean evaluation path.
    /// </para>
    /// </remarks>
    /// <param name="feature">The stable name of the feature flag.</param>
    /// <param name="ct">A token that may request cancellation of the evaluation.</param>
    /// <returns>
    /// The assigned <see cref="FeatureVariant"/>, or <see cref="FeatureVariant.Unassigned"/> if no
    /// variant could be resolved.
    /// </returns>
    ValueTask<FeatureVariant> GetVariantAsync(string feature, CancellationToken ct = default);

    /// <summary>
    /// Gets the variant assigned to the given <paramref name="feature"/> for the given
    /// <paramref name="context"/>. Use this overload for percentage-based gradual rollouts or
    /// A/B experiments, where the assigned variant depends on a stable per-caller targeting
    /// identity (e.g. a tenant id or user id).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in P-298/WO-049. Unlike <see cref="IsEnabledAsync{TContext}(string, TContext, CancellationToken)"/>,
    /// which bridges <c>Microsoft.FeatureManagement</c>'s generic contextual-filter mechanism,
    /// <c>Microsoft.FeatureManagement</c>'s variant/allocation API is evaluated exclusively against
    /// a targeting identity (a stable user id plus optional groups) — it has no generic
    /// per-<typeparamref name="TContext"/> contextual-filter equivalent. The adapter implementing
    /// this interface derives that targeting identity deterministically from
    /// <c><paramref name="context"/>?.ToString()</c>: repeated calls with an equal
    /// <paramref name="context"/> value always resolve to the same variant. Callers wanting precise
    /// targeting control should pass a <see cref="string"/> context representing the stable identity
    /// to target (e.g. a tenant id or user id) — a <typeparamref name="TContext"/> without a
    /// meaningful <see cref="object.ToString"/> override will still be deterministic, but every
    /// instance of that type collapses to the same targeting bucket.
    /// </para>
    /// <para>
    /// A feature with no configured variants, an unresolvable allocation, or an unknown feature
    /// name never throws — it returns the deterministic fallback <see cref="FeatureVariant.Unassigned"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">
    /// The context type providing the targeting identity used to evaluate which variant is assigned.
    /// </typeparam>
    /// <param name="feature">The stable name of the feature flag.</param>
    /// <param name="context">The evaluation context used to derive the targeting identity.</param>
    /// <param name="ct">A token that may request cancellation of the evaluation.</param>
    /// <returns>
    /// The assigned <see cref="FeatureVariant"/>, or <see cref="FeatureVariant.Unassigned"/> if no
    /// variant could be resolved.
    /// </returns>
    ValueTask<FeatureVariant> GetVariantAsync<TContext>(string feature, TContext context, CancellationToken ct = default);
}
