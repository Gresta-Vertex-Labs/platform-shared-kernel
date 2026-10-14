using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.SemanticKernel.Diagnostics;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.AI.SemanticKernel.Resilience;
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
/// and even then only for a real HTTP 429 or 5xx status — never for a 4xx client-shaped failure
/// (bad request, auth, not-found), never for a transport fault, and never for
/// <see cref="CompleteStreamingAsync"/> at all (retrying mid-stream would duplicate already-yielded
/// content). The SDK's own implicit retry is disabled at registration, so there is no hidden second dispatch.
/// </para>
/// <para><b>No caching anywhere in this adapter</b> — every call dispatches fresh, per Domain Invariant #4.</para>
/// <para>
/// <b>Observability.</b> Each call is one Client span (<c>ai complete</c> / <c>ai complete_stream</c>) and one duration
/// observation; a retry stays inside its call's span. Only a recognised provider fault becomes a failed
/// <see cref="Result{T}"/> (or an <see cref="IntelligenceStreamException"/> mid-stream); the caller's cancellation is
/// always an <see cref="OperationCanceledException"/>; anything else propagates untouched.
/// </para>
/// </remarks>
internal sealed class SemanticKernelOrchestrator : ISemanticKernel
{
    private const string ProviderName = IntelligenceWellKnown.SemanticKernelProviderName;

    private readonly IChatCompletionService _chatService;
    private readonly string _defaultModelId;
    private readonly BoundedRetryOptions? _retryOptions;
    private readonly SemanticKernelTelemetry _telemetry;
    private readonly ILogger<SemanticKernelOrchestrator> _logger;

    public SemanticKernelOrchestrator(
        IChatCompletionService chatService,
        string defaultModelId,
        BoundedRetryOptions? retryOptions,
        SemanticKernelTelemetry telemetry,
        ILogger<SemanticKernelOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelId);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(logger);

        _chatService = chatService;
        _defaultModelId = defaultModelId;
        _retryOptions = retryOptions;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<Result<CompletionResult>> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelId = request.ModelId ?? _defaultModelId;
        var boundedModel = SemanticKernelTelemetry.BoundedModel(request.ModelId, _defaultModelId);

        var result = await SemanticKernelOperationRunner.RunAsync(
            _telemetry, _logger, IntelligenceOperations.Complete, boundedModel, modelId, cancellationToken, async () =>
            {
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

                        var completion = MapResult(responses[0], modelId);
                        SemanticKernelLog.SemanticKernelCompletionCompleted(_logger, completion.ModelId, completion.FinishReason.ToString(), completion.TokenUsage.TotalTokens);
                        return Result<CompletionResult>.Success(completion);
                    }
                    catch (Exception ex) when (_retryOptions is not null
                        && attempt < maxAttempts
                        && SemanticKernelFaults.IsTransientHttpFailure(ex, cancellationToken))
                    {
                        SemanticKernelFaults.TryClassify(ex, cancellationToken, ProviderName, IntelligenceOperations.Complete, modelId, out var error);
                        SemanticKernelLog.SemanticKernelCompletionRetrying(_logger, modelId, attempt, maxAttempts, error?.Code ?? string.Empty, ex);
                        await Task.Delay(_retryOptions.ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
                    }
                }

                // Unreachable — the loop above always returns or throws before falling out.
                throw new UnreachableException("The retry loop ended without a result.");
            }).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _telemetry.RecordTokens(IntelligenceOperations.Complete, boundedModel, result.Value.TokenUsage);
        }

        return result;
    }

    public async IAsyncEnumerable<CompletionChunk> CompleteStreamingAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelId = request.ModelId ?? _defaultModelId;
        var boundedModel = SemanticKernelTelemetry.BoundedModel(request.ModelId, _defaultModelId);

        // The span ends when the enumerator completes, faults or is disposed (an abandoned stream included). The
        // consumer's Activity.Current is captured before the span starts and put back before every yield, so the span
        // never replaces the consumer's ambient activity; it is current only around the provider call it parents.
        var consumerActivity = Activity.Current;
        using var scope = _telemetry.Begin(IntelligenceOperations.CompleteStream, boundedModel);
        Activity.Current = consumerActivity;

        ChatHistory history;
        OpenAIPromptExecutionSettings settings;
        try
        {
            history = BuildHistory(request);
            settings = BuildExecutionSettings(request);
        }
        catch (Exception ex) when (scope.FailAndDecline(ex))
        {
            throw;
        }

        SemanticKernelLog.SemanticKernelStreamingStarted(_logger, modelId);

        var enumerator = _chatService
            .GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        TokenUsage? finalUsage = null;
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                consumerActivity = Activity.Current;

                CompletionChunk? chunk = null;
                try
                {
                    if (scope.Activity is not null)
                    {
                        Activity.Current = scope.Activity;
                    }

                    if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        chunk = MapChunk(enumerator.Current);
                    }
                }
                catch (Exception ex) when (SemanticKernelFaults.TryClassify(
                    ex, cancellationToken, ProviderName, IntelligenceOperations.CompleteStream, modelId, out var error))
                {
                    if (error is null)
                    {
                        var canceled = SemanticKernelFaults.CanceledBy(ex, cancellationToken);
                        scope.Fail(canceled);
                        throw canceled;
                    }

                    SemanticKernelFaults.Report(ex, error, _logger, IntelligenceOperations.CompleteStream, modelId);
                    scope.Fail(error);
                    throw new IntelligenceStreamException(error, ex);
                }
                catch (Exception ex) when (scope.FailAndDecline(ex))
                {
                    throw;
                }
                finally
                {
                    if (scope.Activity is not null)
                    {
                        Activity.Current = consumerActivity;
                    }
                }

                if (chunk is null)
                {
                    // The usage the final chunk carried is the call's usage; nothing is summed, so nothing is double counted.
                    if (finalUsage is not null)
                    {
                        _telemetry.RecordTokens(IntelligenceOperations.CompleteStream, boundedModel, finalUsage);
                    }

                    scope.Succeed();
                    yield break;
                }

                if (chunk.TokenUsage is { } usage)
                {
                    finalUsage = usage;
                }

                yield return chunk;
            }
        }
    }

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
        _ => throw new UnreachableException($"Unknown ChatRole '{role}'."),
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
