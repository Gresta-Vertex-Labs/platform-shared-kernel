using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in object-storage connectivity health check, wrapping <c>08.Storage</c>'s
/// <see cref="IFileStorage.CheckHealthAsync"/> probe.
/// </summary>
public static class StorageReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies object-storage connectivity for
    /// <paramref name="bucket"/> via the <see cref="IFileStorage"/> resolved from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="bucket">
    /// The bucket to probe. A required, explicit parameter — deliberately never defaulted from
    /// either provider's <c>DefaultBucket</c> option, since doing so would require referencing a
    /// concrete provider options type (<c>S3StorageOptions</c>/<c>ObsStorageOptions</c>) and
    /// reintroduce exactly the provider-specific coupling this method exists to avoid.
    /// <see cref="IFileStorage"/> itself carries no "default bucket" concept.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Storage"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Storage"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves <see cref="IFileStorage"/> from DI — works
    /// uniformly against whichever provider (<c>SharedKernel.Storage.S3</c> or
    /// <c>SharedKernel.Storage.Obs</c>) a service has registered, with zero provider-specific
    /// branching in this package. Opt-in only — never registered by <c>AddServiceDefaults()</c> or
    /// <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddStorageReadinessCheck(
        this IHealthChecksBuilder builder,
        string bucket,
        string name = HealthCheckNames.Storage)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Storage];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.StorageReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new StorageReadinessHealthCheck(sp.GetRequiredService<IFileStorage>(), bucket),
            failureStatus: null,
            tags: tags));
    }
}
