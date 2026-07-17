using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Storage.Obs.Logging;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Storage.Obs</c>,
/// shared by <see cref="FileStorage.ObsFileStorage"/> and <see cref="BlobUri.ObsBlobUriGenerator"/>.
/// </summary>
/// <remarks>
/// Occupies EventId sub-block <c>LoggingEventIdRanges.Storage + 200</c>..<c>+299</c> (8200-8299) —
/// the third 100-wide sub-block of this domain's <c>8000-8999</c> block, per <c>SharedKernel.Storage.Obs</c>'s
/// declaration order in <c>08.Storage/CLAUDE.md</c>.
/// </remarks>
internal static partial class ObsStorageLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 200,
        Level = LogLevel.Debug,
        Message = "OBS object '{Key}' not found in bucket '{Bucket}' during {Operation}.")]
    internal static partial void ObjectNotFound(ILogger logger, Exception exception, string bucket, string key, string operation);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 201,
        Level = LogLevel.Warning,
        Message = "Access denied for object '{Key}' in bucket '{Bucket}' during {Operation}.")]
    internal static partial void AccessDenied(ILogger logger, Exception exception, string bucket, string key, string operation);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 202,
        Level = LogLevel.Error,
        Message = "Failed to upload object '{Key}' to bucket '{Bucket}'.")]
    internal static partial void UploadFailed(ILogger logger, Exception exception, string bucket, string key);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 203,
        Level = LogLevel.Error,
        Message = "Failed to copy object '{SourceKey}' from bucket '{SourceBucket}' to '{DestinationKey}' in bucket '{DestinationBucket}'.")]
    internal static partial void CopyFailed(
        ILogger logger,
        Exception exception,
        string sourceBucket,
        string sourceKey,
        string destinationBucket,
        string destinationKey);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 204,
        Level = LogLevel.Error,
        Message = "Batch delete failed for bucket '{Bucket}'.")]
    internal static partial void BatchDeleteFailed(ILogger logger, Exception exception, string bucket);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 205,
        Level = LogLevel.Warning,
        Message = "Connectivity check failed for bucket '{Bucket}'.")]
    internal static partial void ConnectivityCheckFailed(ILogger logger, Exception exception, string bucket);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 206,
        Level = LogLevel.Warning,
        Message = "Requested presign expiry '{RequestedExpiry}' for object '{Key}' in bucket '{Bucket}' exceeds the provider maximum of '{MaxExpiry}'.")]
    internal static partial void ExpiryTooLong(ILogger logger, string bucket, string key, TimeSpan requestedExpiry, TimeSpan maxExpiry);
}
