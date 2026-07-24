using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.AI.Abstractions.Constants;

/// <summary>
/// Domain-local well-known constants for <c>10.Intelligence</c> — default query limits, OpenTelemetry
/// source/meter/tag names, and provider name literals.
/// </summary>
/// <remarks>
/// This is the <c>SK0022</c> named-constant holder for the intelligence domain, mirroring
/// <c>SearchWellKnown</c> (<c>09.Search.Abstractions</c>). It lives in <c>.Abstractions</c>
/// specifically so all three sibling provider packages read byte-identical
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

    /// <summary>The provider-name value for Milvus.</summary>
    public const string MilvusProviderName = "milvus";

    /// <summary>The provider-name value for the Semantic Kernel orchestration provider.</summary>
    public const string SemanticKernelProviderName = "semantickernel";

    /// <summary>The OpenTelemetry tag key carrying the active provider name (<c>ai.provider</c>).</summary>
    public const string ProviderTagName = "ai.provider";

    /// <summary>The OpenTelemetry tag key carrying the active collection name (<c>ai.collection</c>).</summary>
    public const string CollectionTagName = "ai.collection";

    /// <summary>The OpenTelemetry tag key carrying the active model identifier (<c>ai.model</c>).</summary>
    public const string ModelTagName = "ai.model";
}
