using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Options;

namespace SharedKernel.Search.Meilisearch.Tenancy;

/// <summary>The Meilisearch implementation of <see cref="ITenantSearchTokenIssuer"/> — scoped.</summary>
internal sealed class MeilisearchTenantTokenIssuer : ITenantSearchTokenIssuer
{
    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly MeilisearchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<MeilisearchTenantTokenIssuer> _logger;

    /// <summary>Initializes a new <see cref="MeilisearchTenantTokenIssuer"/>.</summary>
    public MeilisearchTenantTokenIssuer(
        global::Meilisearch.MeilisearchClient client,
        MeilisearchOptions options,
        IClock clock,
        ILogger<MeilisearchTenantTokenIssuer> logger)
    {
        _client = client;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<TenantSearchToken>> IssueAsync(
        TenantScope tenantScope,
        string tenantField,
        IReadOnlyCollection<string> indexNames,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl > TimeSpan.FromMinutes(_options.TenantTokenMaxTtlMinutes))
        {
            return Task.FromResult(Result<TenantSearchToken>.Failure(
                MeilisearchErrors.TenantTokenTtlOutOfRange(ttl, _options.TenantTokenMaxTtlMinutes)));
        }

        if (string.IsNullOrEmpty(_options.ApiKeyUid))
        {
            return Task.FromResult(Result<TenantSearchToken>.Failure(
                MeilisearchErrors.TenantTokenIssuanceFailed(
                    "MeilisearchOptions.ApiKeyUid must be configured to issue tenant search tokens.")));
        }

        // The rule dictionary is constructed here, closed, from TenantScope + tenantField — callers
        // can never hand-write the SDK's untyped IReadOnlyDictionary<string, object> rule shape.
        var filterExpression = $"{tenantField} = {QuoteAndEscape(tenantScope.Value)}";
        var rules = new Dictionary<string, object>();
        foreach (var indexName in indexNames)
        {
            rules[indexName] = new Dictionary<string, object> { ["filter"] = filterExpression };
        }

        var expiresAt = _clock.UtcNow.Add(ttl);

        string token;
        try
        {
            var tenantTokenRules = new global::Meilisearch.TenantTokenRules(rules);
            token = _client.GenerateTenantToken(_options.ApiKeyUid, tenantTokenRules, _options.ApiKey, expiresAt.UtcDateTime);
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<TenantSearchToken>.Failure(MeilisearchErrors.TenantTokenIssuanceFailed(ex.Message)));
        }

        _logger.MeilisearchTenantTokenIssued(indexNames.Count, (int)ttl.TotalMinutes);

        return Task.FromResult(Result<TenantSearchToken>.Success(new TenantSearchToken
        {
            Value = token,
            ExpiresAt = expiresAt,
            ScopedIndexes = indexNames.ToArray(),
        }));
    }

    private static string QuoteAndEscape(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
