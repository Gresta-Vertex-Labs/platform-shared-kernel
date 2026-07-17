using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Storage.S3.Logging;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Storage.S3</c>,
/// shared by <see cref="FileStorage.S3FileStorage"/> and <see cref="BlobUri.S3BlobUriGenerator"/>.
/// </summary>
/// <remarks>
/// Occupies EventId sub-block <c>LoggingEventIdRanges.Storage + 100</c>..<c>+199</c> (8100-8199) —
/// the second 100-wide sub-block of this domain's <c>8000-8999</c> block, per <c>SharedKernel.Storage.S3</c>'s
/// declaration order in <c>08.Storage/CLAUDE.md</c>.
/// </remarks>
internal static partial class S3StorageLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 100,
        Level = LogLevel.Debug,
        Message = "S3 object '{Key}' not found in bucket '{Bucket}' during {Operation}.")]
    internal static partial void ObjectNotFound(ILogger logger, Exception exception, string bucket, string key, string operation);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 101,
        Level = LogLevel.Warning,
        Message = "Access denied for object '{Key}' in bucket '{Bucket}' during {Operation}.")]
    internal static partial void AccessDenied(ILogger logger, Exception exception, string bucket, string key, string operation);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 102,
        Level = LogLevel.Error,
        Message = "Failed to upload object '{Key}' to bucket '{Bucket}'.")]
    internal static partial void UploadFailed(ILogger logger, Exception exception, string bucket, string key);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 103,
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
        EventId = LoggingEventIdRanges.Storage + 104,
        Level = LogLevel.Error,
        Message = "Batch delete failed for bucket '{Bucket}'.")]
    internal static partial void BatchDeleteFailed(ILogger logger, Exception exception, string bucket);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 105,
        Level = LogLevel.Warning,
        Message = "Connectivity check failed for bucket '{Bucket}'.")]
    internal static partial void ConnectivityCheckFailed(ILogger logger, Exception exception, string bucket);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 106,
        Level = LogLevel.Warning,
        Message = "Requested presign expiry '{RequestedExpiry}' for object '{Key}' in bucket '{Bucket}' exceeds the provider maximum of '{MaxExpiry}'.")]
    internal static partial void ExpiryTooLong(ILogger logger, string bucket, string key, TimeSpan requestedExpiry, TimeSpan maxExpiry);
}
