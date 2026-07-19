using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Search.Meilisearch.Options;

/// <summary>Options-pattern configuration for the Meilisearch provider, validated at startup.</summary>
public sealed class MeilisearchOptions
{
    /// <summary>
    /// The configuration section path this type binds to — passed to <c>GetSection</c>, never a bare
    /// literal at the call site (SK0022).
    /// </summary>
    public const string SectionName = "Search:Meilisearch";

    /// <summary>Gets or sets the Meilisearch instance URL.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the API key used to authenticate against Meilisearch.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UID of the API key identified by <see cref="ApiKey"/> — required only when
    /// <c>.WithTenantTokens()</c> is used, since <c>GenerateTenantToken</c> needs it.
    /// </summary>
    public string? ApiKeyUid { get; set; }

    /// <summary>Gets or sets the HTTP client timeout, in seconds.</summary>
    [Range(1, 300)]
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the maximum time <c>WaitUntilSearchableAsync</c> polls for, in seconds.</summary>
    [Range(1, 3600)]
    public int TaskWaitTimeoutSeconds { get; set; } = 120;

    /// <summary>Gets or sets the polling interval used while waiting for a Meilisearch task, in milliseconds.</summary>
    [Range(25, 5000)]
    public int TaskPollIntervalMilliseconds { get; set; } = 250;

    /// <summary>Gets or sets the pagination ceiling applied to every registered index.</summary>
    [Range(1, 100_000)]
    public int MaxTotalHits { get; set; } = 1000;

    /// <summary>Gets or sets the per-facet value-count cap applied to every registered index.</summary>
    [Range(1, 10_000)]
    public int MaxFacetValues { get; set; } = 100;

    /// <summary>Gets or sets the default batch size used by bulk write operations.</summary>
    [Range(1, 100_000)]
    public int DefaultBatchSize { get; set; } = 1000;

    /// <summary>Gets or sets the maximum TTL, in minutes, a tenant search token may be issued for.</summary>
    [Range(1, 60)]
    public int TenantTokenMaxTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether index settings are verified against the registered
    /// index definition at startup.
    /// </summary>
    public bool ValidateIndexSettingsOnStart { get; set; } = true;
}
