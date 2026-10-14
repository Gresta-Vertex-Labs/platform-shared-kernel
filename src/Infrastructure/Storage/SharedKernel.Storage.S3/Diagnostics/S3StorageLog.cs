using System.Net;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Storage.S3.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Storage.S3</c>, EventIds 8100-8199 (see <see cref="S3StorageLoggingEventIds"/>).
/// Object keys, endpoints and tenant ids are never logged; the bucket and the provider's request id are, so a failure
/// can be traced with the provider's support.
/// </summary>
internal static partial class S3StorageLog
{
    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.AccessDenied,
        Level = LogLevel.Warning,
        Message = "Store '{Store}' on bucket '{Bucket}' denied operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.")]
    public static partial void AccessDenied(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode, string? requestId);

    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.Unavailable,
        Level = LogLevel.Warning,
        Message = "Store '{Store}' on bucket '{Bucket}' was unavailable for operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.")]
    public static partial void Unavailable(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode? statusCode, string? errorCode, string? requestId, Exception exception);

    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.ProviderError,
        Level = LogLevel.Error,
        Message = "Store '{Store}' on bucket '{Bucket}' rejected operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.")]
    public static partial void ProviderError(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode, string? requestId, Exception exception);

    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.ExpectedFailure,
        Level = LogLevel.Debug,
        Message = "Store '{Store}' on bucket '{Bucket}' answered operation '{Operation}' with status {StatusCode} and error code '{ErrorCode}'.")]
    public static partial void ExpectedFailure(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode);

    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.ProbeFailed,
        Level = LogLevel.Warning,
        Message = "Store '{Store}' on bucket '{Bucket}' failed its reachability probe.")]
    public static partial void ProbeFailed(ILogger logger, string store, string bucket, Exception exception);

    [LoggerMessage(
        EventId = S3StorageLoggingEventIds.WrongRegion,
        Level = LogLevel.Error,
        Message = "Store '{Store}' on bucket '{Bucket}' is not served by the configured endpoint or region with error code '{ErrorCode}' and request '{RequestId}', so set the connection Region to the region of the bucket.")]
    public static partial void WrongRegion(
        ILogger logger, string store, string bucket, string? errorCode, string? requestId, Exception exception);
}
