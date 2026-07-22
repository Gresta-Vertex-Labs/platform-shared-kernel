namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>A chat/completion invocation request.</summary>
public sealed record CompletionRequest
{
    /// <summary>Gets the conversation messages, in order.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Gets the model identifier to use, or <see langword="null"/> for the provider default.</summary>
    public string? ModelId { get; init; }

    /// <summary>Gets the sampling temperature, or <see langword="null"/> for the provider default.</summary>
    public float? Temperature { get; init; }

    /// <summary>Gets the maximum output tokens, or <see langword="null"/> for the provider default.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Gets the tools offered to the model for this call.</summary>
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];

    /// <summary>Gets the stop sequences that terminate generation.</summary>
    public IReadOnlyList<string> StopSequences { get; init; } = [];
}
