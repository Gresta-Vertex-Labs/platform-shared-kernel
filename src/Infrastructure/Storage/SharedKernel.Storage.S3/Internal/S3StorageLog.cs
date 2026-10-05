using System.Net;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Storage.S3.Internal;

/// <summary>
/// Log statements of <c>SharedKernel.Storage.S3</c>, EventIds 8100-8199 (the second sub-block of
/// <c>08.Storage</c>'s 8000-8999). Object keys are never logged; the provider's request id is, so a failure
/// can be traced with the provider's support.
/// </summary>
internal static partial class S3StorageLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 100,
        Level = LogLevel.Warning,
        Message = "Storage store {Store} (bucket {Bucket}) denied {Operation}: {StatusCode} {ErrorCode}, request {RequestId}.")]
    public static partial void AccessDenied(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode, string? requestId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 101,
        Level = LogLevel.Warning,
        Message = "Storage store {Store} (bucket {Bucket}) was unavailable for {Operation}: {StatusCode} {ErrorCode}, request {RequestId}.")]
    public static partial void Unavailable(
        ILogger logger, Exception exception, string store, string bucket, string operation, HttpStatusCode? statusCode, string? errorCode, string? requestId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 102,
        Level = LogLevel.Error,
        Message = "Storage store {Store} (bucket {Bucket}) rejected {Operation}: {StatusCode} {ErrorCode}, request {RequestId}.")]
    public static partial void ProviderError(
        ILogger logger, Exception exception, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode, string? requestId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 103,
        Level = LogLevel.Debug,
        Message = "Storage store {Store} (bucket {Bucket}) answered {Operation} with {StatusCode} {ErrorCode}.")]
    public static partial void ExpectedFailure(
        ILogger logger, string store, string bucket, string operation, HttpStatusCode statusCode, string? errorCode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 104,
        Level = LogLevel.Warning,
        Message = "Storage store {Store} (bucket {Bucket}) failed its reachability probe.")]
    public static partial void ProbeFailed(ILogger logger, Exception exception, string store, string bucket);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Storage + 105,
        Level = LogLevel.Error,
        Message = "Storage store {Store}: bucket {Bucket} is not served by the configured endpoint or region ({ErrorCode}, request {RequestId}). Set the connection's Region to the bucket's region.")]
    public static partial void WrongRegion(ILogger logger, string store, string bucket, string? errorCode, string? requestId);
}
