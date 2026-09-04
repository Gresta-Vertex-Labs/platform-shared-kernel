using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IEncryptionKeyProviderProbe.ProbeAsync"/> (<c>01.Core/SharedKernel.Cryptography</c>)
/// in an <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="HealthStatus.Healthy"/> when <see cref="EncryptionKeyProviderHealth.IsHealthy"/>
/// is <see langword="true"/>; <see cref="HealthStatus.Unhealthy"/> otherwise — never
/// <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of raw KMS connectivity: an encrypt/decrypt-key-resolution call either works or it does
/// not (same calibration family as <see cref="DatabaseReadinessHealthCheck"/>/
/// <see cref="StorageReadinessHealthCheck"/>/<see cref="SearchReadinessHealthCheck"/>, never
/// <see cref="CacheReadinessHealthCheck"/>'s FusionCache-L1-absorption rationale).
/// </para>
/// <para>
/// Like <c>19.Scheduling</c>'s <c>ISchedulerServiceProbe</c>, <see cref="IEncryptionKeyProviderProbe.ProbeAsync"/>
/// returns <see cref="EncryptionKeyProviderHealth"/> directly — never a <c>Result&lt;T&gt;</c>
/// wrapper. The probe implementation itself never lets an exception propagate for an ordinary
/// reachability failure — see <see cref="IEncryptionKeyProviderProbe"/>'s own remarks — so this
/// adapter does not need a defensive try/catch around the call; it reads
/// <see cref="EncryptionKeyProviderHealth.IsHealthy"/>/<c>.Description</c> directly.
/// </para>
/// <para>
/// <c>01.Core</c> ships only the probe primitive — this adapter is the
/// <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the platform's
/// "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule.
/// <see cref="IEncryptionKeyProviderProbe"/> is reached via the already-existing
/// <c>ProjectReference</c> to <c>SharedKernel.Cryptography.KeyVault.Azure</c> (added for
/// <c>Cryptography.KeyVaultKeyProviderExtensions.AddSharedKernelKeyVaultKeyProvider</c>) —
/// <c>01.Core</c> is already inside this domain's granted <c>01</c>–<c>12</c> composition-root
/// range, so unlike <see cref="WorkflowReadinessHealthCheck"/>/<see cref="SchedulerReadinessHealthCheck"/>
/// this capability required no new, individually-named layering grant.
/// </para>
/// </remarks>
internal sealed class KeyVaultKeyProviderReadinessHealthCheck(IEncryptionKeyProviderProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);

        return health.IsHealthy
            ? HealthCheckResult.Healthy("Encryption key provider is reachable.")
            : HealthCheckResult.Unhealthy(health.Description ?? "Encryption key provider is unreachable.");
    }
}
