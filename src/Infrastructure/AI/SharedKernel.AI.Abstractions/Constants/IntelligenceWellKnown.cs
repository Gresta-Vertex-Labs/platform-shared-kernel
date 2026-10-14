using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.AI.Abstractions.Constants;

/// <summary>
/// Domain-local well-known constants for <c>10.Intelligence</c> — default query limits, OpenTelemetry
/// source/meter/tag names, and provider name literals.
/// </summary>
/// <remarks>
/// This is the <c>SK0022</c> named-constant holder for the intelligence domain, mirroring
/// <c>SearchWellKnown</c> (<c>09.Search.Abstractions</c>). It lives in <c>.Abstractions</c>
/// specifically so every sibling provider package reads byte-identical
/// <see cref="ActivitySourceName"/> / <see cref="MeterName"/> values — that identity is the whole
/// reason <c>13.ServiceDefaults</c> can wire one string name and cover all three providers with no
/// <c>ProjectReference</c> to <c>10.Intelligence</c>. The <c>ActivitySource</c>/<c>Meter</c>
/// <em>instances</em> are created per-provider; only the names live here.
/// </remarks>
public static class IntelligenceWellKnown
{
    /// <summary>The default result limit for a <see cref="VectorQuery"/> when unspecified.</summary>
    public const int DefaultQueryLimit = 10;

    /// <summary>The maximum result limit a <see cref="VectorQuery"/> may request.</summary>
    public const int MaxQueryLimit = 1000;

    /// <summary>
    /// The shared OpenTelemetry <c>ActivitySource</c> name every provider package uses for its own,
    /// separately-instantiated <c>ActivitySource</c>.
    /// </summary>
    public const string ActivitySourceName = "SharedKernel.AI";

    /// <summary>
    /// The shared OpenTelemetry <c>Meter</c> name every provider package uses for its own,
    /// separately-instantiated <c>Meter</c>.
    /// </summary>
    public const string MeterName = "SharedKernel.AI";

    /// <summary>The provider-name value for Qdrant.</summary>
    public const string QdrantProviderName = "qdrant";

    /// <summary>The provider-name value for the Semantic Kernel orchestration provider.</summary>
    public const string SemanticKernelProviderName = "semantickernel";

    /// <summary>The <see cref="IntelligenceTagKeys.TokenType"/> value for tokens the provider billed for the request (<c>input</c>).</summary>
    public const string TokenTypeInput = "input";

    /// <summary>The <see cref="IntelligenceTagKeys.TokenType"/> value for tokens the provider billed for the response (<c>output</c>).</summary>
    public const string TokenTypeOutput = "output";

    /// <summary>The instrument name of the operation duration histogram (<c>sharedkernel.ai.operation.duration</c>, unit <c>s</c>).</summary>
    public const string OperationDurationInstrumentName = "sharedkernel.ai.operation.duration";

    /// <summary>The instrument name of the token counter (<c>sharedkernel.ai.tokens</c>, unit <c>{token}</c>).</summary>
    public const string TokensInstrumentName = "sharedkernel.ai.tokens";

    /// <summary>The instrument name of the vector record counter (<c>sharedkernel.ai.vector.records</c>, unit <c>{record}</c>).</summary>
    public const string VectorRecordsInstrumentName = "sharedkernel.ai.vector.records";

    /// <summary>
    /// The <c>ai.model</c> value used when a caller-supplied model id is not one the provider is configured with, so
    /// a caller cannot mint unbounded metric dimensions.
    /// </summary>
    public const string OtherModel = "_OTHER";
}
