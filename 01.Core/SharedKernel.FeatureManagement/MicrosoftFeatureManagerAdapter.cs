using SharedKernel.FeatureManagement.Abstractions;
using MsftTargetingContext = Microsoft.FeatureManagement.FeatureFilters.TargetingContext;
using MsftVariant = Microsoft.FeatureManagement.Variant;
using MsftVariantFeatureManager = Microsoft.FeatureManagement.IVariantFeatureManager;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Adapts <c>Microsoft.FeatureManagement.IVariantFeatureManager</c> to the
/// SharedKernel <see cref="IFeatureManager"/> abstraction, isolating consuming services
/// from the concrete Microsoft.FeatureManagement package.
/// </summary>
/// <remarks>
/// As of P-298/WO-049 this adapter is built on <c>IVariantFeatureManager</c> rather than the
/// older, boolean-only <c>Microsoft.FeatureManagement.IFeatureManager</c> — <c>IVariantFeatureManager</c>
/// is a superset that carries both the boolean <c>IsEnabledAsync</c> members and the
/// variant/allocation <c>GetVariantAsync</c> members, so a single injected dependency serves this
/// entire adapter. <c>Microsoft.FeatureManagement</c>'s own <c>AddFeatureManagement(...)</c>
/// registers its concrete <c>FeatureManager</c> against both interfaces, so this retargeting
/// requires no additional DI registration.
/// </remarks>
internal sealed class MicrosoftFeatureManagerAdapter : IFeatureManager
{
    private readonly MsftVariantFeatureManager _inner;

    public MicrosoftFeatureManagerAdapter(MsftVariantFeatureManager inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsEnabledAsync(string feature, CancellationToken ct = default)
    {
        // Preserves this member's pre-existing behavior exactly (P-298/WO-049 hard rule: adding
        // variant support must never change the existing boolean IsEnabledAsync behavior).
        // IVariantFeatureManager.IsEnabledAsync now technically accepts a CancellationToken where
        // the previously-injected Microsoft.FeatureManagement.IFeatureManager did not — ct is
        // deliberately still not forwarded, to guarantee zero observable behavior change from this
        // phase. ct is held for a possible future, deliberate phase to wire it up.
        return await _inner.IsEnabledAsync(feature, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsEnabledAsync<TContext>(
        string feature,
        TContext context,
        CancellationToken ct = default)
    {
        // See the remark in the ct-less IsEnabledAsync overload above — ct is deliberately not
        // forwarded, to preserve this pre-existing member's behavior byte-for-byte.
        return await _inner.IsEnabledAsync(feature, context, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<FeatureVariant> GetVariantAsync(string feature, CancellationToken ct = default)
    {
        MsftVariant? variant = await _inner.GetVariantAsync(feature, ct).ConfigureAwait(false);
        return ToFeatureVariant(variant);
    }

    /// <inheritdoc/>
    public async ValueTask<FeatureVariant> GetVariantAsync<TContext>(
        string feature,
        TContext context,
        CancellationToken ct = default)
    {
        MsftTargetingContext targetingContext = ToTargetingContext(context);
        MsftVariant? variant = await _inner.GetVariantAsync(feature, targetingContext, ct).ConfigureAwait(false);
        return ToFeatureVariant(variant);
    }

    private static FeatureVariant ToFeatureVariant(MsftVariant? variant)
    {
        // Microsoft.FeatureManagement returns a null Variant for an unconfigured feature, a
        // disabled feature with no DefaultWhenDisabled, or an enabled feature with no matching
        // allocation branch and no DefaultWhenEnabled — confirmed empirically (P-298/WO-049).
        // IFeatureManager.GetVariantAsync never surfaces null; it returns the documented sentinel.
        if (variant is null)
        {
            return FeatureVariant.Unassigned;
        }

        return new FeatureVariant(variant.Name, variant.Configuration?.Value);
    }

    private static MsftTargetingContext ToTargetingContext<TContext>(TContext context)
    {
        // Microsoft.FeatureManagement's variant/allocation API has no generic per-TContext
        // contextual-filter equivalent to IsEnabledAsync<TContext> — GetVariantAsync's only
        // context-aware overload takes a fixed ITargetingContext (UserId + Groups). context's
        // ToString() is used as the stable targeting UserId so that repeated calls with an equal
        // context value deterministically resolve to the same variant (confirmed empirically:
        // Microsoft.FeatureManagement's percentile/user/group allocation is a stable hash of the
        // targeting UserId, not a source of nondeterminism itself).
        return new MsftTargetingContext
        {
            UserId = context?.ToString() ?? string.Empty,
            Groups = [],
        };
    }
}
