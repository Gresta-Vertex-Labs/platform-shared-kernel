using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in encryption-key-provider (KMS) reachability health check, wrapping <c>01.Core</c>'s
/// <see cref="IEncryptionKeyProviderProbe.ProbeAsync"/> probe.
/// </summary>
public static class KeyVaultKeyProviderReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies the configured <see cref="IEncryptionKeyProvider"/>'s
    /// backing KMS/HSM is reachable, via the <see cref="IEncryptionKeyProviderProbe"/> resolved
    /// from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.EncryptionKeyProvider"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.EncryptionKeyProvider"/>,
    /// never <see cref="HealthCheckTags.Live"/>. Resolves only <see cref="IEncryptionKeyProviderProbe"/>
    /// from DI — never <see cref="IEncryptionKeyProvider"/>, <see cref="IEnvelopeEncryptionProvider"/>,
    /// or any concrete provider type. Requires <c>Cryptography.KeyVaultKeyProviderExtensions
    /// .AddSharedKernelKeyVaultKeyProvider()</c> (or an equivalent registration of
    /// <see cref="IEncryptionKeyProviderProbe"/>) to have been called first — omitting it throws at
    /// the first probe invocation, not at startup, mirroring every other dependency-specific check
    /// in this domain. Like <see cref="WorkflowReadinessHealthCheckExtensions.AddWorkflowReadinessCheck"/>/
    /// <see cref="SchedulerReadinessHealthCheckExtensions.AddSchedulerReadinessCheck"/> (and unlike
    /// the bucket/indexName/collectionName family), this method takes no caller-supplied identifier
    /// parameter: <see cref="IEncryptionKeyProviderProbe"/> is a per-host singleton with nothing
    /// analogous to a bucket/index/collection name to disambiguate.
    /// </para>
    /// <para>
    /// <b>No layering grant needed — unlike <c>AddWorkflowReadinessCheck</c>/
    /// <c>AddSchedulerReadinessCheck</c>.</b> <c>01.Core</c> is already inside this domain's
    /// granted <c>01</c>–<c>12</c> composition-root range; <see cref="IEncryptionKeyProviderProbe"/>
    /// is reached through the <c>ProjectReference</c> to <c>SharedKernel.Cryptography.KeyVault.Azure</c>
    /// this package already carries for <c>AddSharedKernelKeyVaultKeyProvider</c> — there is no
    /// narrow, individually-named <c>13→NN</c> grant to document here, and none should ever be
    /// added for this method.
    /// </para>
    /// <para>
    /// Reports <see cref="HealthStatus.Unhealthy"/> — never <see cref="HealthStatus.Degraded"/> —
    /// when the probe reports the dependency unreachable. No fail-safe/graceful-degradation layer
    /// sits in front of raw KMS connectivity: an encrypt/decrypt-key-resolution call either
    /// succeeds or it does not, the same calibration family as <c>AddDatabaseReadinessCheck</c>/
    /// <c>AddStorageReadinessCheck</c>/<c>AddSearchReadinessCheck</c>/
    /// <see cref="SchedulerReadinessHealthCheckExtensions.AddSchedulerReadinessCheck"/> — never
    /// <c>AddCacheReadinessCheck</c>'s fail-safe-aware <see cref="HealthStatus.Degraded"/>. Opt-in
    /// only — never registered by <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddKeyVaultKeyProviderReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.EncryptionKeyProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.EncryptionKeyProvider];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.KeyVaultKeyProviderReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new KeyVaultKeyProviderReadinessHealthCheck(sp.GetRequiredService<IEncryptionKeyProviderProbe>()),
            failureStatus: null,
            tags: tags));
    }
}
