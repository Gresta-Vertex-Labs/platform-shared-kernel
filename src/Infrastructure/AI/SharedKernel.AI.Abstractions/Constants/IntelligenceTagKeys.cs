namespace SharedKernel.AI.Abstractions.Constants;

/// <summary>
/// The tag keys carried by the <c>ai {operation}</c> spans and the <c>sharedkernel.ai.*</c> instruments. Every provider
/// uses these constants, so a provider swap changes no dashboard except the <see cref="Provider"/> value.
/// </summary>
/// <remarks>
/// Tags are bounded configuration only. A record id, vector, filter value, metadata value, key, host or prompt text is
/// never a tag.
/// </remarks>
public static class IntelligenceTagKeys
{
    /// <summary>The active provider name (<c>ai.provider</c>), a value such as <see cref="IntelligenceWellKnown.QdrantProviderName"/>.</summary>
    public const string Provider = "ai.provider";

    /// <summary>The active collection name (<c>ai.collection</c>).</summary>
    public const string Collection = "ai.collection";

    /// <summary>The configured model identifier (<c>ai.model</c>), or <see cref="IntelligenceWellKnown.OtherModel"/>.</summary>
    public const string Model = "ai.model";

    /// <summary>The operation name (<c>ai.operation</c>), a value of <see cref="IntelligenceOperations"/>.</summary>
    public const string Operation = "ai.operation";

    /// <summary>The token direction (<c>ai.token_type</c>), <see cref="IntelligenceWellKnown.TokenTypeInput"/> or <see cref="IntelligenceWellKnown.TokenTypeOutput"/>.</summary>
    public const string TokenType = "ai.token_type";
}
