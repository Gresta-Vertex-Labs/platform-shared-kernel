using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.AI.SemanticKernel.Logging;
using SharedKernel.AI.SemanticKernel.Resilience;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.SemanticKernel.Orchestration;

/// <summary>The Semantic Kernel implementation of <see cref="ISemanticKernel"/>.</summary>
/// <remarks>
/// <para>
/// <b>Tool execution is never performed here</b> — when the model requests tool calls, they surface as
/// <see cref="CompletionResult.ToolCalls"/> with <see cref="CompletionFinishReason.ToolCallsRequested"/>;
/// this type never invokes them (<c>ToolCallBehavior.EnableFunctions(..., autoInvoke: false)</c>).
/// </para>
/// <para>
/// <b>No retry by default:</b> <see cref="CompleteAsync"/> dispatches exactly once unless the
/// composition root explicitly supplied a <see cref="BoundedRetryOptions"/> via <c>.WithBoundedRetry(...)</c>,
/// and even then only for genuinely transient HTTP outcomes (429 rate-limit, 5xx server fault) — never
/// for a 4xx client-shaped failure (bad request, auth, not-found), and never for
/// <see cref="CompleteStreamingAsync"/> at all (retrying mid-stream would duplicate already-yielded
/// content).
/// </para>
/// <para><b>No caching anywhere in this adapter</b> — every call dispatches fresh, per Domain Invariant #4.</para>
/// </remarks>
internal sealed class SemanticKernelOrchestrator : ISemanticKernel
{
    private const string ProviderName = IntelligenceWellKnown.SemanticKernelProviderName;

    private readonly IChatCompletionService _chatService;
    private readonly string _defaultModelId;
    private readonly BoundedRetryOptions? _retryOptions;
    private readonly ILogger<SemanticKernelOrchestrator> _logger;

    public SemanticKernelOrchestrator(
        IChatCompletionService chatService,
        string defaultModelId,
        BoundedRetryOptions? retryOptions,
        ILogger<SemanticKernelOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelId);
        ArgumentNullException.ThrowIfNull(logger);

        _chatService = chatService;
        _defaultModelId = defaultModelId;
        _retryOptions = retryOptions;
        _logger = logger;
    }

    public async Task<Result<CompletionResult>> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelId = request.ModelId ?? _defaultModelId;
        var history = BuildHistory(request);
        var settings = BuildExecutionSettings(request);
        var maxAttempts = _retryOptions?.MaxAttempts ?? 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var responses = await _chatService
                    .GetChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var result = MapResult(responses[0], modelId);
                _logger.SemanticKernelCompletionCompleted(result.ModelId, result.FinishReason.ToString(), result.TokenUsage.TotalTokens);
                return Result<CompletionResult>.Success(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = SemanticKernelErrors.FromException(ex, ProviderName, nameof(CompleteAsync), modelId);
                var canRetry = _retryOptions is not null && attempt < maxAttempts && IsTransientFailure(ex);
                if (canRetry)
                {
                    _logger.SemanticKernelCompletionRetrying(modelId, attempt, maxAttempts, error.Code);
                    await Task.Delay(_retryOptions!.ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.SemanticKernelEngineFault(nameof(CompleteAsync), modelId);
                return Result<CompletionResult>.Failure(error);
            }
        }

        // Unreachable — the loop above always returns or throws before falling out.
        throw new InvalidOperationException("CompleteAsync's retry loop terminated without a result.");
    }

    public async IAsyncEnumerable<CompletionChunk> CompleteStreamingAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelId = request.ModelId ?? _defaultModelId;
        var history = BuildHistory(request);
        var settings = BuildExecutionSettings(request);

        _logger.SemanticKernelStreamingStarted(modelId);

        var enumerator = _chatService
            .GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                StreamingChatMessageContent current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }

                    current = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new IntelligenceStreamException(
                        SemanticKernelErrors.FromException(ex, ProviderName, nameof(CompleteStreamingAsync), modelId), ex);
                }

                yield return MapChunk(current);
            }
        }
    }

    /// <summary>
    /// Determines whether <paramref name="exception"/> represents a genuinely transient failure worth
    /// retrying — HTTP 429 (rate limit) or 5xx (server fault) only. A 4xx client-shaped failure (bad
    /// request, auth, not-found) is never retried: retrying an inherently malformed request only
    /// re-bills for the same certain failure.
    /// </summary>
    private static bool IsTransientFailure(Exception exception) =>
        exception is System.ClientModel.ClientResultException { Status: 429 or >= 500 };

    private static ChatHistory BuildHistory(CompletionRequest request)
    {
        var history = new ChatHistory();
        foreach (var message in request.Messages)
        {
            history.AddMessage(MapRole(message.Role), message.Content);
        }

        return history;
    }

    private static AuthorRole MapRole(ChatRole role) => role switch
    {
        ChatRole.System => AuthorRole.System,
        ChatRole.User => AuthorRole.User,
        ChatRole.Assistant => AuthorRole.Assistant,
        ChatRole.Tool => AuthorRole.Tool,
        _ => throw new InvalidOperationException($"Unknown ChatRole '{role}'."),
    };

    private static OpenAIPromptExecutionSettings BuildExecutionSettings(CompletionRequest request)
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            ModelId = request.ModelId,
            Temperature = request.Temperature,
            MaxTokens = request.MaxOutputTokens,
        };

        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = request.StopSequences.ToList();
        }

        if (request.Tools.Count > 0)
        {
            var functions = request.Tools.Select(ToolDefinitionMapper.ToOpenAIFunction).ToList();

            // autoInvoke: false — this contract never executes tools itself; the caller does. See the
            // type-level remarks.
            settings.ToolCallBehavior = ToolCallBehavior.EnableFunctions(functions, autoInvoke: false);
        }

        return settings;
    }

    private static CompletionResult MapResult(ChatMessageContent content, string modelId)
    {
        var toolCalls = content.Items
            .OfType<FunctionCallContent>()
            .Select(call => new ToolCallRequest
            {
                CallId = call.Id ?? string.Empty,
                Name = call.FunctionName,
                ArgumentsJson = SerializeArguments(call.Arguments),
            })
            .ToList();

        var finishReason = toolCalls.Count > 0
            ? CompletionFinishReason.ToolCallsRequested
            : MapFinishReasonFromMetadata(content.Metadata);

        return new CompletionResult
        {
            Message = new ChatMessage { Role = ChatRole.Assistant, Content = content.Content ?? string.Empty },
            ModelId = content.ModelId ?? modelId,
            TokenUsage = ExtractUsageFromMetadata(content.Metadata),
            FinishReason = finishReason,
            ToolCalls = toolCalls,
        };
    }

    private static CompletionChunk MapChunk(StreamingChatMessageContent chunk)
    {
        CompletionFinishReason? finishReason = chunk is OpenAIStreamingChatMessageContent { FinishReason: { } reason }
            ? MapOpenAiFinishReason(reason)
            : null;

        TokenUsage? tokenUsage = null;
        if (chunk.Metadata is not null &&
            chunk.Metadata.TryGetValue("Usage", out var usageObj) &&
            usageObj is OpenAI.Chat.ChatTokenUsage usage)
        {
            tokenUsage = new TokenUsage
            {
                PromptTokens = usage.InputTokenCount,
                CompletionTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
            };
        }

        return new CompletionChunk
        {
            DeltaContent = chunk.Content ?? string.Empty,
            FinishReason = finishReason,
            TokenUsage = tokenUsage,
        };
    }

    private static string SerializeArguments(KernelArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "{}";
        }

        var plain = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            plain[key] = value;
        }

        return JsonSerializer.Serialize(plain);
    }

    private static TokenUsage ExtractUsageFromMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("Usage", out var usageObj) && usageObj is OpenAI.Chat.ChatTokenUsage usage)
        {
            return new TokenUsage
            {
                PromptTokens = usage.InputTokenCount,
                CompletionTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
            };
        }

        return new TokenUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 };
    }

    private static CompletionFinishReason MapFinishReasonFromMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("FinishReason", out var reasonObj))
        {
            if (reasonObj is OpenAI.Chat.ChatFinishReason reason)
            {
                return MapOpenAiFinishReason(reason);
            }

            if (reasonObj is string reasonText)
            {
                return reasonText.ToLowerInvariant() switch
                {
                    "length" => CompletionFinishReason.MaxTokensReached,
                    "tool_calls" or "function_call" => CompletionFinishReason.ToolCallsRequested,
                    "content_filter" => CompletionFinishReason.ContentFiltered,
                    _ => CompletionFinishReason.Stop,
                };
            }
        }

        return CompletionFinishReason.Stop;
    }

    private static CompletionFinishReason MapOpenAiFinishReason(OpenAI.Chat.ChatFinishReason reason) => reason switch
    {
        OpenAI.Chat.ChatFinishReason.Stop => CompletionFinishReason.Stop,
        OpenAI.Chat.ChatFinishReason.Length => CompletionFinishReason.MaxTokensReached,
        OpenAI.Chat.ChatFinishReason.ToolCalls => CompletionFinishReason.ToolCallsRequested,
        OpenAI.Chat.ChatFinishReason.FunctionCall => CompletionFinishReason.ToolCallsRequested,
        OpenAI.Chat.ChatFinishReason.ContentFilter => CompletionFinishReason.ContentFiltered,
        _ => CompletionFinishReason.Stop,
    };
}
