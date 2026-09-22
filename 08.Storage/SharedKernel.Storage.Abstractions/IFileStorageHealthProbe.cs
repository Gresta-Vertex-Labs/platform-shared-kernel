using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage;

/// <summary>
/// Checks that a store's bucket is reachable with the configured credentials, for readiness probes. Registered
/// by <see cref="StorageServiceCollectionExtensions.AddSharedKernelStorage"/> as a singleton.
/// </summary>
/// <remarks>
/// Wire it into health checks with <c>services.AddHealthChecks().AddStorageReadinessCheck(storeName)</c> from
/// <c>SharedKernel.ServiceDefaults.Storage</c>. The probe reads bucket metadata only (S3 <c>HeadBucket</c>, which
/// needs <c>s3:ListBucket</c>); it never needs an object to exist and never transfers object bytes.
/// </remarks>
public interface IFileStorageHealthProbe
{
    /// <summary>Probes the bucket behind <paramref name="storeName"/>.</summary>
    /// <param name="storeName">The store name, compared ignoring case; shared or tenant-scoped.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>
    /// Success, or a failure. S3-family providers return <see cref="StorageErrorCodes.AccessDenied"/> when the
    /// credentials are refused (403) and <see cref="StorageErrorCodes.Unavailable"/> for every other failure,
    /// including a missing bucket, a bucket in another region, or an unreachable endpoint; details are logged.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="storeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No store has that name.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result> ProbeAsync(string storeName, CancellationToken cancellationToken = default);
}
