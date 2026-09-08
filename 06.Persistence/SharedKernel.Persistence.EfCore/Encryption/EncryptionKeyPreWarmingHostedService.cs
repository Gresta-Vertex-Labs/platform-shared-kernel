using Microsoft.Extensions.Hosting;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Startup readiness gate that blocks the host from accepting traffic until
/// <see cref="PreWarmedEncryptionKeyProvider"/>'s current encryption key version has been warmed
/// (D-133/P-498/WO-081).
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically by
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> —
/// mirrors <c>MigrationAndSeedHostedService&lt;TContext&gt;</c>'s existing "block readiness until
/// done" shape (<c>WithMigrationsOnStartup()</c>). This is a ONE-TIME, expected, boot-time blocking
/// wait — never a per-request one — and directly satisfies the "does not block a thread-pool thread
/// on a cache-cold KMS call under normal operation" requirement for the write path:
/// <see cref="PreWarmedEncryptionKeyProvider.GetCurrentKeyAsync"/>'s synchronous-throw-if-unwarmed
/// path becomes reachable only under a genuine startup-ordering bug once this hosted service has
/// completed.
/// </para>
/// <para><see cref="StopAsync"/> is a no-op.</para>
/// </remarks>
internal sealed class EncryptionKeyPreWarmingHostedService : IHostedService
{
    private readonly PreWarmedEncryptionKeyProvider _provider;

    /// <summary>Initialises a new <see cref="EncryptionKeyPreWarmingHostedService"/>.</summary>
    /// <param name="provider">The provider to warm on startup.</param>
    public EncryptionKeyPreWarmingHostedService(PreWarmedEncryptionKeyProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _provider.WarmCurrentAsync(cancellationToken).AsTask();

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
