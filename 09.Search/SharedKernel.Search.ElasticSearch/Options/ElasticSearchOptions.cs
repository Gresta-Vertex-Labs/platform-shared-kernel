using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Search.ElasticSearch.Options;

/// <summary>Options-pattern configuration for the ElasticSearch provider, validated at startup.</summary>
public sealed class ElasticSearchOptions
{
    /// <summary>
    /// The configuration section path this type binds to — passed to <c>GetSection</c>, never a bare
    /// literal at the call site (SK0022).
    /// </summary>
    public const string SectionName = "Search:ElasticSearch";

    /// <summary>Gets or sets the cluster node URIs.</summary>
    [Required]
    [MinLength(1)]
    public string[] Nodes { get; set; } = [];

    /// <summary>Gets or sets the API key used to authenticate against the cluster.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the basic-auth username, used only when <see cref="ApiKey"/> is not set.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the basic-auth password, used only when <see cref="ApiKey"/> is not set.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the expected server certificate fingerprint (for self-signed deployments).</summary>
    public string? CertificateFingerprint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TLS certificate validation is disabled. Logs a startup
    /// warning when <see langword="true"/> — never enable this in production.
    /// </summary>
    public bool AllowInvalidCertificates { get; set; }

    /// <summary>Gets or sets the request timeout, in seconds.</summary>
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the ping timeout, in seconds.</summary>
    [Range(1, 30)]
    public int PingTimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the pagination ceiling applied to every registered index. Defaults to the
    /// Meilisearch-parity value of 1000, not ElasticSearch's native 10 000 default — see the
    /// "Why MaxTotalHits defaults to 1000 on both providers" note in <c>09.Search/CLAUDE.md</c>.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxTotalHits { get; set; } = 1000;

    /// <summary>Gets or sets the per-facet value-count cap applied to every registered index.</summary>
    [Range(1, 10_000)]
    public int MaxFacetValues { get; set; } = 100;

    /// <summary>Gets or sets the target payload-byte ceiling per bulk batch.</summary>
    [Range(1_048_576, 52_428_800)]
    public int BulkMaxBytes { get; set; } = 10_485_760;

    /// <summary>Gets or sets the document-count ceiling per bulk batch.</summary>
    [Range(1, 50_000)]
    public int BulkMaxDocuments { get; set; } = 2000;

    /// <summary>Gets or sets the point-in-time keep-alive duration, in seconds.</summary>
    [Range(10, 3600)]
    public int PointInTimeKeepAliveSeconds { get; set; } = 300;

    /// <summary>Gets or sets how long a <c>ProbeAsync</c> result is cached, in seconds. 0 disables caching.</summary>
    [Range(0, 60)]
    public int ProbeCacheSeconds { get; set; } = 5;

    /// <summary>Gets or sets the number of primary shards applied when provisioning a new index.</summary>
    [Range(1, 100)]
    public int NumberOfShards { get; set; } = 1;

    /// <summary>Gets or sets the number of replicas applied when provisioning a new index.</summary>
    [Range(0, 10)]
    public int NumberOfReplicas { get; set; } = 1;

    /// <summary>Gets or sets the refresh interval, in seconds, applied when provisioning a new index.</summary>
    [Range(-1, 3600)]
    public int RefreshIntervalSeconds { get; set; } = 1;

}
