using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// Readiness probe for field encryption's keys, named <see cref="FieldEncryptionReadiness.ProbeName"/>.
/// </summary>
/// <remarks>
/// Unhealthy while the key ring of an asynchronous-only provider has not been refreshed within
/// <see cref="EncryptionOptions.MaxKeyStaleness"/> (a process that stopped refreshing cannot read values
/// encrypted under a key made current since). Otherwise it reports the key provider's own readiness probe when the
/// provider has one, and healthy when it does not.
/// </remarks>
internal sealed class FieldEncryptionKeyRingProbe(
    FieldKeyRing keyRing,
    IOptions<EncryptionOptions> options,
    IServiceProvider services) : IReadinessProbe
{
    public string Name => FieldEncryptionReadiness.ProbeName;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (keyRing.IsBridged)
        {
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            if (keyRing.LastRefreshedAt is not { } refreshedAt)
                return ReadinessReport.Unhealthy("Field encryption keys have not been loaded yet.");

            var age = timeProvider.GetUtcNow() - refreshedAt;
            if (age > options.Value.MaxKeyStaleness)
            {
                return ReadinessReport.Unhealthy(
                    $"Field encryption keys were last refreshed {age.TotalMinutes:F0} minutes ago.");
            }
        }

        if (keyRing.Provider is not IReadinessProbe inner)
            return ReadinessReport.Healthy();

        return await inner.ProbeAsync(cancellationToken).ConfigureAwait(false);
    }
}
