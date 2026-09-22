using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Storage;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in object-storage connectivity health check, wrapping <c>08.Storage</c>'s
/// <see cref="IFileStorageHealthProbe"/> for one named store.
/// </summary>
public static class StorageReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies the bucket behind the store named
    /// <paramref name="storeName"/> is reachable, via the <see cref="IFileStorageHealthProbe"/> that
    /// <c>AddSharedKernelStorage()</c> registers.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="storeName">
    /// The name of the registered store to probe (e.g. <c>"invoices"</c>) — the same name passed to the
    /// provider's <c>AddStore</c>/<c>AddTenantStore</c>. A required, explicit parameter: a service with
    /// several stores on different buckets adds one check per store, each with its own
    /// <paramref name="name"/>.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Storage"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="storeName"/> is not a valid store name.</exception>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Storage"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves <see cref="IFileStorageHealthProbe"/> from DI — works
    /// uniformly against whichever provider (<c>SharedKernel.Storage.S3</c>,
    /// <c>SharedKernel.Storage.Obs</c>) serves the store, with zero provider-specific branching in this
    /// package. The probe reads bucket metadata only; it never needs an object to exist. Opt-in only —
    /// never registered by <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddStorageReadinessCheck(
        this IHealthChecksBuilder builder,
        string storeName,
        string name = HealthCheckNames.Storage)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!FileStoreRegistration.IsValidStoreName(storeName))
        {
            throw new ArgumentException(
                $"Store name '{storeName}' is invalid: use 1 to 64 characters from A-Z, a-z, 0-9, '.', '_' and '-', starting with a letter or digit.",
                nameof(storeName));
        }

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Storage];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.StorageReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new StorageReadinessHealthCheck(sp.GetRequiredService<IFileStorageHealthProbe>(), storeName),
            failureStatus: null,
            tags: tags));
    }
}
