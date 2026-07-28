using System.Collections.Concurrent;
using SharedKernel.FeatureManagement.Abstractions;

namespace SharedKernel.Testing.FeatureManagement;

/// <summary>
/// In-memory test double for <see cref="IFeatureManager"/>, including variant/allocation support.
/// </summary>
/// <remarks>
/// <para>
/// A deterministic override map, not a rules engine — explicitly NOT a weighted-random allocator. A
/// test asserting "60% of calls get VariantB" is testing <c>Microsoft.FeatureManagement</c>'s own
/// allocation engine, not this platform's neutral abstraction. This fake exists so application code
/// consuming <see cref="IFeatureManager"/> can be tested deterministically given a fixed flag/variant
/// state, never to reimplement percentage-based rollout logic.
/// </para>
/// <para>
/// An unconfigured feature defaults CLOSED for the boolean path (<see langword="false"/>, the safer
/// choice) and falls back to <see cref="FeatureVariant.Unassigned"/> for the variant path — neither
/// path ever throws, mirroring the real contract's documented fallback requirement.
/// </para>
/// </remarks>
public sealed class FakeFeatureManager : IFeatureManager
{
    private readonly ConcurrentDictionary<string, bool> _enabledOverrides = new();
    private readonly ConcurrentDictionary<string, FeatureVariant> _variantOverrides = new();

    /// <summary>Configures whether <paramref name="feature"/> is enabled for every subsequent boolean evaluation.</summary>
    /// <param name="feature">The stable feature name.</param>
    /// <param name="enabled">Whether the feature should evaluate as enabled.</param>
    public void SetEnabled(string feature, bool enabled) => _enabledOverrides[feature] = enabled;

    /// <summary>Configures the variant assigned to <paramref name="feature"/> for every subsequent variant evaluation.</summary>
    /// <param name="feature">The stable feature name.</param>
    /// <param name="variant">The variant to assign.</param>
    public void SetVariant(string feature, FeatureVariant variant) => _variantOverrides[feature] = variant;

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(string feature, CancellationToken ct = default) =>
        ValueTask.FromResult(_enabledOverrides.GetValueOrDefault(feature));

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync<TContext>(string feature, TContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(_enabledOverrides.GetValueOrDefault(feature));

    /// <inheritdoc />
    public ValueTask<FeatureVariant> GetVariantAsync(string feature, CancellationToken ct = default) =>
        ValueTask.FromResult(_variantOverrides.GetValueOrDefault(feature) ?? FeatureVariant.Unassigned);

    /// <inheritdoc />
    public ValueTask<FeatureVariant> GetVariantAsync<TContext>(string feature, TContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(_variantOverrides.GetValueOrDefault(feature) ?? FeatureVariant.Unassigned);

    /// <summary>Clears both the enabled-flag and variant override maps.</summary>
    public void Reset()
    {
        _enabledOverrides.Clear();
        _variantOverrides.Clear();
    }
}
