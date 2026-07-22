namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>Why a completion call stopped generating.</summary>
public enum CompletionFinishReason
{
    /// <summary>The model reached a natural stopping point.</summary>
    Stop = 0,

    /// <summary>The model was truncated by <see cref="CompletionRequest.MaxOutputTokens"/> or a provider ceiling.</summary>
    MaxTokensReached = 1,

    /// <summary>The model requested one or more tool calls — see <see cref="CompletionResult.ToolCalls"/>.</summary>
    ToolCallsRequested = 2,

    /// <summary>The provider's content filter interrupted generation.</summary>
    ContentFiltered = 3,
}
