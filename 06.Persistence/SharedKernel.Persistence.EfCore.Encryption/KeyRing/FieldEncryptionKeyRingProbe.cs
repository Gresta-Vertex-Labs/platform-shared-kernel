using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// Readiness probe for field encryption's keys, registered as a keyed <see cref="IEncryptionKeyProviderProbe"/>
/// under <see cref="FieldEncryptionServiceKeys.KeyRingProbe"/>.
/// </summary>
/// <remarks>
/// Unhealthy while the key ring of an asynchronous-only provider has not been refreshed within
/// <see cref="EncryptionOptions.MaxKeyStaleness"/> (a process that stopped refreshing cannot read values
/// encrypted under a key made current since). Otherwise it reports the key provider's own probe when the provider
/// has one, and healthy when it does not. Wire it into a readiness check with
/// <c>sp.GetRequiredKeyedService&lt;IEncryptionKeyProviderProbe&gt;(FieldEncryptionServiceKeys.KeyRingProbe)</c>.
/// </remarks>
internal sealed class FieldEncryptionKeyRingProbe(
    FieldKeyRing keyRing,
    IOptions<EncryptionOptions> options,
    IServiceProvider services) : IEncryptionKeyProviderProbe
{
    public async Task<EncryptionKeyProviderHealth> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (keyRing.IsBridged)
        {
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            if (keyRing.LastRefreshedAt is not { } refreshedAt)
                return new EncryptionKeyProviderHealth(false, "Field encryption keys have not been loaded yet.");

            var age = timeProvider.GetUtcNow() - refreshedAt;
            if (age > options.Value.MaxKeyStaleness)
            {
                return new EncryptionKeyProviderHealth(
                    false, $"Field encryption keys were last refreshed {age.TotalMinutes:F0} minutes ago.");
            }
        }

        return keyRing.Provider is IEncryptionKeyProviderProbe inner
            ? await inner.ProbeAsync(cancellationToken).ConfigureAwait(false)
            : new EncryptionKeyProviderHealth(true, null);
    }
}
