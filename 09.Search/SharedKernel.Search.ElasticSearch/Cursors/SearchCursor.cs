namespace SharedKernel.Search.ElasticSearch.Cursors;

/// <summary>An opaque handle over an open ElasticSearch point-in-time deep-pagination cursor.</summary>
/// <remarks><see cref="Token"/> is opaque (a point-in-time id plus the last sort tuple) and must never be parsed.</remarks>
public readonly record struct SearchCursor
{
    /// <summary>Initializes a new <see cref="SearchCursor"/>.</summary>
    public SearchCursor(string token, DateTimeOffset expiresAt)
    {
        Token = token;
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the opaque cursor token. Never parse this value.</summary>
    public string Token { get; }

    /// <summary>Gets the time the underlying point-in-time's keep-alive lapses.</summary>
    public DateTimeOffset ExpiresAt { get; }
}
