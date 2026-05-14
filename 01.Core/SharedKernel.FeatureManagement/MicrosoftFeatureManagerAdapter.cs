using SharedKernel.FeatureManagement.Abstractions;
using MsftFeatureManager = Microsoft.FeatureManagement.IFeatureManager;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Adapts <see cref="Microsoft.FeatureManagement.IFeatureManager"/> to the
/// SharedKernel <see cref="IFeatureManager"/> abstraction, isolating consuming services
/// from the concrete Microsoft.FeatureManagement package.
/// </summary>
internal sealed class MicrosoftFeatureManagerAdapter : IFeatureManager
{
    private readonly MsftFeatureManager _inner;

    public MicrosoftFeatureManagerAdapter(MsftFeatureManager inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsEnabledAsync(string feature, CancellationToken ct = default)
    {
        // Microsoft.FeatureManagement does not accept CancellationToken here —
        // the token is held for future API evolution.
        return await _inner.IsEnabledAsync(feature).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsEnabledAsync<TContext>(
        string feature,
        TContext context,
        CancellationToken ct = default)
    {
        return await _inner.IsEnabledAsync(feature, context).ConfigureAwait(false);
    }
}
