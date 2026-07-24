using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.SemanticKernel.Logging;

/// <summary>
/// The <c>[LoggerMessage]</c> source-generated log statements for
/// <c>SharedKernel.AI.SemanticKernel</c>, reserved <c>EventId</c> sub-block 10300–10399
/// (<see cref="LoggingEventIdRanges.Intelligence"/> + 300..399).
/// </summary>
internal static partial class SemanticKernelLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 300,
        Level = LogLevel.Information,
        Message = "Semantic Kernel orchestration provider configured: chat model '{ChatModelId}', embedding model '{EmbeddingModelId}'.")]
    public static partial void SemanticKernelClientConfigured(this ILogger logger, string chatModelId, string embeddingModelId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 301,
        Level = LogLevel.Debug,
        Message = "Embedded {TextCount} text(s) with model '{ModelId}', billing {TotalTokens} token(s).")]
    public static partial void SemanticKernelEmbeddingCompleted(this ILogger logger, int textCount, string modelId, int totalTokens);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 302,
        Level = LogLevel.Warning,
        Message = "Embedding batch size {Requested} exceeds the provider's ceiling of {Ceiling}.")]
    public static partial void SemanticKernelBatchSizeExceeded(this ILogger logger, int requested, int ceiling);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 303,
        Level = LogLevel.Debug,
        Message = "Completion call with model '{ModelId}' finished with reason {FinishReason}, billing {TotalTokens} token(s).")]
    public static partial void SemanticKernelCompletionCompleted(this ILogger logger, string modelId, string finishReason, int totalTokens);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 304,
        Level = LogLevel.Debug,
        Message = "Streaming completion call with model '{ModelId}' started.")]
    public static partial void SemanticKernelStreamingStarted(this ILogger logger, string modelId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 305,
        Level = LogLevel.Warning,
        Message = "Request to model '{ModelId}' rejected before any I/O: {Reason}")]
    public static partial void SemanticKernelRequestRejected(this ILogger logger, string modelId, string reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 306,
        Level = LogLevel.Warning,
        Message = "Estimated prompt size {Estimated} exceeds context window {ContextWindow} for model '{ModelId}'.")]
    public static partial void SemanticKernelContextWindowExceeded(this ILogger logger, string modelId, int estimated, int contextWindow);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 307,
        Level = LogLevel.Error,
        Message = "Semantic Kernel operation '{Operation}' faulted for model '{ModelId}'.")]
    public static partial void SemanticKernelEngineFault(this ILogger logger, string operation, string modelId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 308,
        Level = LogLevel.Warning,
        Message = "Semantic Kernel raw client access is enabled. The raw client bypasses tenant scoping.")]
    public static partial void SemanticKernelRawClientAccessEnabled(this ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 309,
        Level = LogLevel.Warning,
        Message = "Retrying completion call for model '{ModelId}': attempt {Attempt} of {MaxAttempts} after error code '{ErrorCode}'.")]
    public static partial void SemanticKernelCompletionRetrying(this ILogger logger, string modelId, int attempt, int maxAttempts, string errorCode);
}
