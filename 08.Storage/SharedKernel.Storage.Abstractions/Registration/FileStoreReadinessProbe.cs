using System.Diagnostics;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage;

/// <summary>The readiness probe of one named store, over the provider's <see cref="FileStoreRegistration.Probe"/>.</summary>
internal sealed class FileStoreReadinessProbe(FileStoreRegistration registration, IServiceProvider services) : IReadinessProbe
{
    /// <summary>Data key: the store name.</summary>
    internal const string StoreDataKey = "Store";

    /// <summary>Data key: the <c>storage.*</c> error code of a failed probe.</summary>
    internal const string ErrorCodeDataKey = "ErrorCode";

    public string Name { get; } = StorageReadinessProbeNames.ForStore(registration.Name);

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.GetTimestamp();
        Result result = await registration.Probe(services, cancellationToken).ConfigureAwait(false);
        var latency = Stopwatch.GetElapsedTime(started);

        if (result.IsSuccess)
        {
            return ReadinessReport.Healthy(
                "Storage reachable.",
                new Dictionary<string, object>(StringComparer.Ordinal) { [StoreDataKey] = registration.Name },
                latency);
        }

        return ReadinessReport.Unhealthy(
            result.Error.Message,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [StoreDataKey] = registration.Name,
                [ErrorCodeDataKey] = result.Error.Code,
            },
            latency);
    }
}
