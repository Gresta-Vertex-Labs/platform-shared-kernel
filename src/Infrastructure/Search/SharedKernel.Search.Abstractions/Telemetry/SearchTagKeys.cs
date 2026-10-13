namespace SharedKernel.Search.Abstractions.Telemetry;

/// <summary>
/// The span and metric tag keys of <c>09.Search</c>, <c>search.{snake_name}</c>, identical on both providers. Only these
/// keys (plus <c>outcome</c> and <c>error.type</c> from <c>WellKnownTagKeys</c>) are ever recorded: query text, filter
/// values, document ids and cursor tokens are user data and never a tag.
/// </summary>
public static class SearchTagKeys
{
    /// <summary>The index the operation targets (<c>search.index</c>).</summary>
    public const string Index = "search.index";

    /// <summary>The contract operation being executed (<c>search.operation</c>), one of <see cref="SearchOperations"/>.</summary>
    public const string Operation = "search.operation";

    /// <summary>The provider that served the operation (<c>search.provider</c>), a <c>SearchWellKnown.*ProviderName</c> value.</summary>
    public const string Provider = "search.provider";
}
