namespace SharedKernel.FeatureManagement.Abstractions;

/// <summary>
/// The variant assigned to a feature for a particular (or absent) evaluation context — the
/// weighted-allocation counterpart to a plain boolean <see cref="IFeatureManager.IsEnabledAsync(string, CancellationToken)"/>
/// result.
/// </summary>
/// <remarks>
/// <para>
/// Added in P-298/WO-049. Returned by <see cref="IFeatureManager.GetVariantAsync(string, CancellationToken)"/>
/// and <see cref="IFeatureManager.GetVariantAsync{TContext}(string, TContext, CancellationToken)"/>.
/// Deliberately carries no <c>Microsoft.FeatureManagement</c> type — <see cref="Configuration"/> is a
/// raw <see cref="string"/>, never a <c>Microsoft.Extensions.Configuration.IConfigurationSection</c>.
/// </para>
/// <para>
/// <see cref="Configuration"/> reflects the assigned variant's configuration value as a scalar string
/// (<c>IConfigurationSection.Value</c> internally). A variant configured with a nested/structured
/// configuration payload (a JSON object rather than a single scalar value) surfaces as <c>null</c>
/// here — this contract does not attempt to flatten or re-serialize structured configuration.
/// </para>
/// </remarks>
/// <param name="Name">
/// The caller-defined variant identifier (e.g. <c>"ControlGroup"</c> / <c>"VariantB"</c>), matching
/// the variant name declared in the feature's configured allocation.
/// </param>
/// <param name="Configuration">
/// The raw configuration payload for this variant, if any. The caller deserializes this to its own
/// strongly-typed shape as needed; <see cref="FeatureVariant"/> itself performs no interpretation.
/// </param>
public sealed record FeatureVariant(string Name, string? Configuration = null)
{
    /// <summary>
    /// The deterministic fallback variant returned when a feature has no configured variants, is
    /// not enabled with a matching default allocation, or its allocation cannot otherwise be
    /// resolved for the given evaluation context. <see cref="IFeatureManager"/>'s variant members
    /// never throw for this situation — they return this sentinel instead, mirroring
    /// <c>Error.None</c>'s "never use <see langword="null"/> for the empty case" convention
    /// elsewhere in this domain.
    /// </summary>
    /// <remarks>
    /// Named after <c>Microsoft.FeatureManagement.Telemetry.VariantAssignmentReason.None</c>'s own
    /// documented semantics ("variant allocation did not happen; no variant is assigned") — the
    /// exact condition this sentinel represents.
    /// </remarks>
    public static FeatureVariant Unassigned { get; } = new("Unassigned");
}
