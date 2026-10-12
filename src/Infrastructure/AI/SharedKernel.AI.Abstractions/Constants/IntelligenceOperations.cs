namespace SharedKernel.AI.Abstractions.Constants;

/// <summary>
/// The closed set of operation names carried by the <c>ai.operation</c> span and metric tag, and by the span name
/// <c>ai {operation}</c>. Every provider uses these constants, so a provider swap changes no dashboard except the
/// <c>ai.provider</c> value.
/// </summary>
public static class IntelligenceOperations
{
    /// <summary><c>IVectorCollection.UpsertAsync</c>.</summary>
    public const string Upsert = "upsert";

    /// <summary><c>IVectorCollection.UpsertManyAsync</c>.</summary>
    public const string UpsertMany = "upsert_many";

    /// <summary><c>IVectorCollection.DeleteAsync</c>.</summary>
    public const string Delete = "delete";

    /// <summary><c>IVectorCollection.DeleteManyAsync</c>.</summary>
    public const string DeleteMany = "delete_many";

    /// <summary><c>IVectorCollection.DeleteByFilterAsync</c>.</summary>
    public const string DeleteByFilter = "delete_by_filter";

    /// <summary><c>IVectorCollection.QueryAsync</c>.</summary>
    public const string Query = "query";

    /// <summary>A provider-exclusive hybrid (dense plus sparse) query.</summary>
    public const string QueryHybrid = "query_hybrid";

    /// <summary><c>IVectorCollection.GetAsync</c>.</summary>
    public const string Get = "get";

    /// <summary><c>IVectorCollection.CountAsync</c>.</summary>
    public const string Count = "count";

    /// <summary><c>IVectorCollection.ScrollAsync</c>.</summary>
    public const string Scroll = "scroll";

    /// <summary><c>IEmbeddingGenerator.EmbedAsync</c>.</summary>
    public const string Embed = "embed";

    /// <summary><c>IEmbeddingGenerator.EmbedManyAsync</c>.</summary>
    public const string EmbedMany = "embed_many";

    /// <summary><c>ISemanticKernel.CompleteAsync</c>.</summary>
    public const string Complete = "complete";

    /// <summary><c>ISemanticKernel.CompleteStreamingAsync</c>.</summary>
    public const string CompleteStream = "complete_stream";
}
