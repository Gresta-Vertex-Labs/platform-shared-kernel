namespace SharedKernel.Storage;

/// <summary>
/// The names of the tracing and metrics a storage provider emits, shared by every provider so a dashboard or a
/// subscription written against them works for S3, MinIO, OBS and a future provider alike.
/// </summary>
/// <remarks>
/// The activity source and the meter are both <see cref="SourceName"/>, which a host's <c>SharedKernel.*</c>
/// wildcard subscription picks up without referencing a storage package. Spans are named <c>storage {operation}</c>,
/// kind Client. Object keys are never a tag or a measurement dimension.
/// </remarks>
public static class StorageTelemetryNames
{
    /// <summary>The name of the activity source: <c>SharedKernel.Storage</c>.</summary>
    public const string SourceName = "SharedKernel.Storage";

    /// <summary>The name of the meter: <c>SharedKernel.Storage</c>, the same as <see cref="SourceName"/>.</summary>
    public const string MeterName = SourceName;

    /// <summary>
    /// The duration histogram of one provider operation, unit <c>s</c>: <c>sharedkernel.storage.operation.duration</c>.
    /// Tags: <see cref="StorageTagKeys.Store"/>, <see cref="StorageTagKeys.Operation"/>,
    /// <see cref="StorageTagKeys.Provider"/> and <c>outcome</c>, plus <c>error.type</c> and <c>error.code</c> on a failure.
    /// </summary>
    public const string OperationDurationInstrumentName = "sharedkernel.storage.operation.duration";

    /// <summary>
    /// The byte counter, unit <c>By</c>: <c>sharedkernel.storage.bytes</c>. Tags: <see cref="StorageTagKeys.Store"/>,
    /// <see cref="StorageTagKeys.Provider"/> and <see cref="StorageTagKeys.Direction"/>. Downloads count the bytes
    /// the provider announced for the response.
    /// </summary>
    public const string BytesInstrumentName = "sharedkernel.storage.bytes";
}
