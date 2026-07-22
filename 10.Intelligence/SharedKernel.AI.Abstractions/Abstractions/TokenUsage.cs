namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// Token accounting shared between embedding and completion results — a first-class, unconditional
/// output of every model call, never hidden.
/// </summary>
/// <remarks>
/// <see cref="CompletionTokens"/> is always <c>0</c> on an embedding result — embedding calls have no
/// completion half. <see cref="TotalTokens"/> is not re-derived by a consumer; the provider reports it
/// directly, since some providers bill on values that are not a pure sum (e.g. cached-prefix pricing).
/// </remarks>
public sealed record TokenUsage
{
    /// <summary>Gets the number of prompt/input tokens billed.</summary>
    public required int PromptTokens { get; init; }

    /// <summary>Gets the number of completion/output tokens billed. Always <c>0</c> on an embedding result.</summary>
    public required int CompletionTokens { get; init; }

    /// <summary>Gets the total number of tokens billed, as reported by the provider.</summary>
    public required int TotalTokens { get; init; }
}
