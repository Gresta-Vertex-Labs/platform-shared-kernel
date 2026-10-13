using Microsoft.Extensions.Logging;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>
/// The package's log messages, source generated, in the <c>EventId</c> sub-block 10300-10399
/// (<see cref="SemanticKernelLoggingEventIds"/>). The category is the calling kernel type's <c>ILogger&lt;T&gt;</c>.
/// </summary>
internal static partial class SemanticKernelLog
{
    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.ClientConfigured,
        Level = LogLevel.Information,
        Message = "Semantic Kernel orchestration provider configured with chat model '{ChatModelId}' and embedding model '{EmbeddingModelId}'.")]
    public static partial void SemanticKernelClientConfigured(ILogger logger, string chatModelId, string embeddingModelId);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.EmbeddingCompleted,
        Level = LogLevel.Debug,
        Message = "Embedded {TextCount} text(s) with model '{ModelId}' billing {TotalTokens} token(s).")]
    public static partial void SemanticKernelEmbeddingCompleted(ILogger logger, int textCount, string modelId, int totalTokens);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.BatchSizeExceeded,
        Level = LogLevel.Warning,
        Message = "Embedding batch size {RequestedBatchSize} exceeds the ceiling of {MaxBatchSize}.")]
    public static partial void SemanticKernelBatchSizeExceeded(ILogger logger, int requestedBatchSize, int maxBatchSize);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.CompletionCompleted,
        Level = LogLevel.Debug,
        Message = "Completion call with model '{ModelId}' finished with reason '{FinishReason}' and billed {TotalTokens} token(s).")]
    public static partial void SemanticKernelCompletionCompleted(ILogger logger, string modelId, string finishReason, int totalTokens);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.StreamingStarted,
        Level = LogLevel.Debug,
        Message = "Streaming completion call with model '{ModelId}' started.")]
    public static partial void SemanticKernelStreamingStarted(ILogger logger, string modelId);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.RequestRejected,
        Level = LogLevel.Warning,
        Message = "Request to model '{ModelId}' was rejected before any I/O with error type '{ErrorType}' and code '{ErrorCode}'.")]
    public static partial void SemanticKernelRequestRejected(ILogger logger, string modelId, string errorType, string errorCode);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.ContextWindowExceeded,
        Level = LogLevel.Warning,
        Message = "Estimated prompt size of {EstimatedTokens} tokens exceeds the context window of {ContextWindowTokens} tokens for model '{ModelId}'.")]
    public static partial void SemanticKernelContextWindowExceeded(ILogger logger, string modelId, int estimatedTokens, int contextWindowTokens);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.EngineFault,
        Level = LogLevel.Warning,
        Message = "Semantic Kernel operation '{Operation}' faulted for model '{ModelId}' with error code '{ErrorCode}'.")]
    public static partial void SemanticKernelEngineFault(ILogger logger, string operation, string modelId, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.RawClientAccessEnabled,
        Level = LogLevel.Warning,
        Message = "Semantic Kernel raw client access is enabled and bypasses tenant scoping.")]
    public static partial void SemanticKernelRawClientAccessEnabled(ILogger logger);

    [LoggerMessage(
        EventId = SemanticKernelLoggingEventIds.CompletionRetrying,
        Level = LogLevel.Warning,
        Message = "Retrying completion call for model '{ModelId}' as attempt {Attempt} of {MaxAttempts} after error code '{ErrorCode}'.")]
    public static partial void SemanticKernelCompletionRetrying(ILogger logger, string modelId, int attempt, int maxAttempts, string errorCode, Exception exception);
}
