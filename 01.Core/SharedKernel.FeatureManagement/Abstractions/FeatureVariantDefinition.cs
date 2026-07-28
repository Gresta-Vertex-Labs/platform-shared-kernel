namespace SharedKernel.FeatureManagement.Abstractions;

/// <summary>
/// Describes one weighted allocation branch of a gradual-rollout/A-B-experiment feature — the
/// variant-allocation sibling of <see cref="FeatureDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// Added in P-298/WO-049. Like <see cref="FeatureDefinition"/>, <see cref="FeatureVariantDefinition"/>
/// records are used to declare a feature's variants in a discoverable, typed manner. They are not
/// directly used by the runtime evaluation path; use <see cref="IFeatureManager.GetVariantAsync(string, CancellationToken)"/>
/// / <see cref="IFeatureManager.GetVariantAsync{TContext}(string, TContext, CancellationToken)"/> for
/// evaluation. This record models the *declaration* of a variant and its relative allocation weight
/// — it is not a replacement for <see cref="FeatureDefinition"/>'s boolean on/off shape, which
/// remains the correct declaration type for plain feature flags.
/// </para>
/// </remarks>
/// <param name="Name">
/// The variant identifier (e.g. <c>"VariantB"</c>), matching the name that
/// <see cref="FeatureVariant.Name"/> surfaces on assignment.
/// </param>
/// <param name="Weight">
/// The allocation weight for this variant, relative to its sibling variants for the same feature
/// (e.g. percentage points in a percentile-based allocation). This value is documentation/modeling
/// only — the authoritative allocation is whatever is configured in the underlying feature
/// management configuration source; this record does not itself drive evaluation.
/// </param>
/// <param name="Configuration">
/// The configuration payload surfaced to callers via <see cref="FeatureVariant.Configuration"/> when
/// this variant is assigned.
/// </param>
public sealed record FeatureVariantDefinition(
    string Name,
    int Weight,
    string? Configuration = null);
