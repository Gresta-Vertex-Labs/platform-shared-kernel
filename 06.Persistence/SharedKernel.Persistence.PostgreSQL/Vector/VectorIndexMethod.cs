namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>The pgvector approximate-nearest-neighbor index type.</summary>
public enum VectorIndexMethod
{
    /// <summary>
    /// Hierarchical Navigable Small World — higher build cost, faster/more accurate queries. The
    /// generally-recommended default for a query-heavy workload.
    /// </summary>
    Hnsw,

    /// <summary>
    /// Inverted File with Flat compression — faster to build, but must be built AFTER the table has
    /// data (an empty-table IVFFlat index has no useful list centroids) and its query accuracy
    /// depends on choosing an appropriate <c>lists</c> parameter for the table's row count.
    /// </summary>
    IvfFlat,
}
