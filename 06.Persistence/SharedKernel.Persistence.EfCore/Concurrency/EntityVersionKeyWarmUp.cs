using Microsoft.Extensions.Hosting;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>
/// Loads the key that seals entity versions (ETags) before the host takes traffic, when it comes from an
/// asynchronous-only key provider (a KMS such as Azure Key Vault), so no request blocks a thread on the provider.
/// </summary>
/// <remarks>
/// <para>
/// Registered once by <c>AddSharedKernelPostgres</c>. Does nothing for an <c>ISynchronousEncryptionKeyProvider</c>
/// (keys already in memory) or when no key provider is registered. It waits at most
/// <see cref="EntityVersionKeyRing.WarmUpTimeout"/>; a slower provider keeps loading in the background. A failure or a
/// timeout is logged (6025, 6026) and never fails startup: the first request that issues or checks a version then
/// loads the key itself, blocking that request once. That fallback is also the only path for a context built by hand,
/// which has no host.
/// </para>
/// <para>
/// A <c>WebApplication</c> starts hosted services before its server, so no request arrives before the warm-up ends.
/// With <c>HostOptions.ServicesStartConcurrently</c>, an early request may take the fallback.
/// </para>
/// <para>
/// <strong>Readiness does not wait for the key.</strong> Persistence works without it — only issuing and checking a
/// version needs it — so a key service outage must not take every endpoint out of rotation. A service whose readiness
/// should follow its key service registers a readiness check on it (13.ServiceDefaults'
/// <c>AddKeyVaultKeyProviderReadinessCheck()</c>).
/// </para>
/// </remarks>
internal sealed class EntityVersionKeyWarmUp(EntityVersionCodec codec) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => codec.Keys.WarmUpAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
