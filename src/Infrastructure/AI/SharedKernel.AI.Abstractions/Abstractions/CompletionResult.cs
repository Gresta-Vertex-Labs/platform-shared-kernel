namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The result of a non-streaming completion call.</summary>
public sealed record CompletionResult
{
    /// <summary>Gets the assistant's response message.</summary>
    public required ChatMessage Message { get; init; }

    /// <summary>Gets the identifier of the model that produced <see cref="Message"/>.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the token usage this call billed.</summary>
    public required TokenUsage TokenUsage { get; init; }

    /// <summary>Gets why generation stopped.</summary>
    public required CompletionFinishReason FinishReason { get; init; }

    /// <summary>
    /// Gets the tool calls the model requested, populated when <see cref="FinishReason"/> is
    /// <see cref="CompletionFinishReason.ToolCallsRequested"/>.
    /// </summary>
    public IReadOnlyList<ToolCallRequest> ToolCalls { get; init; } = [];
}
