namespace SharedKernel.Caching.FusionCache.Diagnostics;

/// <summary>The closed vocabularies of the cache's tag values.</summary>
public static class CachingTagValues
{
    /// <summary>The in-process memory layer (<c>l1</c>).</summary>
    public const string LevelMemory = "l1";

    /// <summary>The distributed layer (<c>l2</c>).</summary>
    public const string LevelDistributed = "l2";

    /// <summary>A read the cache answered (<c>hit</c>).</summary>
    public const string ResultHit = "hit";

    /// <summary>A read the cache could not answer (<c>miss</c>).</summary>
    public const string ResultMiss = "miss";

    /// <summary>A read without computing (<c>get</c>).</summary>
    public const string OperationGet = "get";

    /// <summary>A read that computes on a miss (<c>get_or_set</c>).</summary>
    public const string OperationGetOrSet = "get_or_set";

    /// <summary>A write (<c>set</c>).</summary>
    public const string OperationSet = "set";

    /// <summary>A factory run (<c>factory</c>).</summary>
    public const string OperationFactory = "factory";
}
