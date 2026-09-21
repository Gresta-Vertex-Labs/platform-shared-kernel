using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>Not ready until every startup migration and seeder has completed (<see cref="IPersistenceStartup"/>).</summary>
internal sealed class PersistenceStartupHealthCheck(IPersistenceStartup startup) : IHealthCheck
{
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(startup.IsCompleted
            ? HealthCheckResult.Healthy("Startup migrations and seeders completed.")
            : HealthCheckResult.Unhealthy("Startup migrations and seeders have not completed."));
}

/// <summary>Wraps the field-encryption key-ring probe (<c>FieldEncryptionServiceKeys.KeyRingProbe</c>).</summary>
internal sealed class FieldEncryptionHealthCheck(IEncryptionKeyProviderProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return health.IsHealthy
            ? HealthCheckResult.Healthy(health.Description ?? "Field-encryption keys available.")
            : HealthCheckResult.Unhealthy(health.Description ?? "Field-encryption keys unavailable.");
    }
}

/// <summary>
/// Wraps <see cref="IAuditSealingProbe"/>: Degraded (still ready) when the oldest unsealed audit record is older than
/// the allowed lag — sealing is background work shared by every instance, so a lag never takes one instance out of
/// rotation — and Unhealthy only when the ledger cannot be read.
/// </summary>
internal sealed class AuditSealingHealthCheck(IAuditSealingProbe probe, TimeSpan maxLag) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, object>
        {
            ["UnsealedRecords"] = health.UnsealedRecords,
            ["Lag"] = health.Lag,
        };

        return health.Lag > maxLag
            ? HealthCheckResult.Degraded($"The oldest unsealed audit record is {health.Lag} old (allowed {maxLag}).", data: data)
            : HealthCheckResult.Healthy("Audit ledger sealing is keeping up.", data);
    }
}
