using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;
// Elastic.Clients.Elasticsearch declares its own non-generic Result enum; alias ours explicitly so
// the bare identifier in this file resolves to the neutral domain contract.
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.ElasticSearch.Provisioning;

/// <summary>The ElasticSearch implementation of <see cref="ISearchIndexProvisioner"/> — singleton.</summary>
internal sealed class ElasticSearchIndexProvisioner : ISearchIndexProvisioner
{
    private const string FingerprintMetaKey = "sk_schema_fingerprint";

    private readonly ElasticsearchClient _client;
    private readonly ElasticSearchOptions _options;
    private readonly ILogger<ElasticSearchIndexProvisioner> _logger;
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<string, (SearchIndexHealth Health, DateTimeOffset ObservedAt)> _probeCache = [];

    /// <summary>Initializes a new <see cref="ElasticSearchIndexProvisioner"/>.</summary>
    public ElasticSearchIndexProvisioner(
        ElasticsearchClient client, ElasticSearchOptions options, ILogger<ElasticSearchIndexProvisioner> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default)
    {
        var existsResponse = await _client.Indices.ExistsAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        if (!existsResponse.IsValidResponse)
        {
            return Result.Failure(SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, "EnsureIndexAsync", existsResponse.DebugInformation));
        }

        var properties = BuildProperties(definition);
        var meta = new Dictionary<string, object> { [FingerprintMetaKey] = definition.Fingerprint };

        if (!existsResponse.Exists)
        {
            var createRequest = new CreateIndexRequest(definition.Name)
            {
                Mappings = new TypeMapping { Properties = properties, Meta = meta },
                Settings = new IndexSettings
                {
                    NumberOfShards = _options.NumberOfShards,
                    NumberOfReplicas = _options.NumberOfReplicas,
                    MaxResultWindow = definition.MaxTotalHits,
                    RefreshInterval = _options.RefreshIntervalSeconds < 0
                        ? new Duration("-1")
                        : TimeSpan.FromSeconds(_options.RefreshIntervalSeconds),
                },
            };

            var createResponse = await _client.Indices.CreateAsync(createRequest, cancellationToken).ConfigureAwait(false);
            if (!createResponse.IsValidResponse)
            {
                return Result.Failure(SearchErrors.EngineFault(
                    SearchWellKnown.ElasticSearchProviderName, "EnsureIndexAsync", createResponse.DebugInformation));
            }

            _logger.ElasticSearchIndexEnsured(definition.Name, definition.Fields.Count, definition.MaxTotalHits);
            return Result.Success();
        }

        // Additive-only: PUT the union mapping. ElasticSearch itself rejects an incompatible in-place
        // field-type change with a non-success response, which is translated into IndexDefinitionConflict
        // rather than predicted client-side — it never silently rewrites an incompatible mapping.
        var putMappingResponse = await _client.Indices
            .PutMappingAsync(new PutMappingRequest(definition.Name) { Properties = properties, Meta = meta }, cancellationToken)
            .ConfigureAwait(false);
        if (!putMappingResponse.IsValidResponse)
        {
            return Result.Failure(SearchErrors.IndexDefinitionConflict(definition.Name, putMappingResponse.DebugInformation));
        }

        var putSettingsResponse = await _client.Indices
            .PutSettingsAsync(
                new PutIndicesSettingsRequest(definition.Name, new IndexSettings { MaxResultWindow = definition.MaxTotalHits }),
                cancellationToken)
            .ConfigureAwait(false);
        if (!putSettingsResponse.IsValidResponse)
        {
            return Result.Failure(SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, "EnsureIndexAsync", putSettingsResponse.DebugInformation));
        }

        _logger.ElasticSearchIndexEnsured(definition.Name, definition.Fields.Count, definition.MaxTotalHits);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var response = await _client.Indices.ExistsAsync(indexName, cancellationToken).ConfigureAwait(false);
        return response.IsValidResponse
            ? Result<bool>.Success(response.Exists)
            : Result<bool>.Failure(SearchErrors.EngineFault(SearchWellKnown.ElasticSearchProviderName, "IndexExistsAsync", response.DebugInformation));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var response = await _client.Indices.DeleteAsync(indexName, cancellationToken).ConfigureAwait(false);
        return response.IsValidResponse
            ? Result.Success()
            : Result.Failure(SearchErrors.EngineFault(SearchWellKnown.ElasticSearchProviderName, "DeleteIndexAsync", response.DebugInformation));
    }

    /// <inheritdoc />
    public async Task<Result> CutoverAsync(IndexCutoverRequest request, CancellationToken cancellationToken = default)
    {
        // A SINGLE request containing both the remove and the add — ElasticSearch applies the whole
        // actions array atomically, so the read alias is never left undefined.
        var updateRequest = new UpdateAliasesRequest
        {
            Actions =
            [
                new IndexUpdateAliasesAction
                {
                    Remove = new RemoveAction { Alias = request.LiveIndexName, Indices = Indices.All },
                },
                new IndexUpdateAliasesAction
                {
                    Add = new AddAction { Alias = request.LiveIndexName, Index = request.StagingIndexName },
                },
            ],
        };

        var response = await _client.Indices.UpdateAliasesAsync(updateRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            return Result.Failure(SearchErrors.CutoverFailed(request.StagingIndexName, request.LiveIndexName, response.DebugInformation));
        }

        _logger.ElasticSearchAliasCutoverCompleted(request.StagingIndexName, request.LiveIndexName);

        if (!request.DeleteStagingAfterCutover)
        {
            return Result.Success();
        }

        var deleteResponse = await _client.Indices.DeleteAsync(request.StagingIndexName, cancellationToken).ConfigureAwait(false);
        if (!deleteResponse.IsValidResponse)
        {
            return Result.Failure(SearchErrors.CutoverFailed(request.StagingIndexName, request.LiveIndexName, deleteResponse.DebugInformation));
        }

        _logger.ElasticSearchStagingIndexDeleted(request.StagingIndexName);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<SearchIndexHealth>> ProbeAsync(string indexName, CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (_options.ProbeCacheSeconds > 0
                && _probeCache.TryGetValue(indexName, out var cached)
                && DateTimeOffset.UtcNow - cached.ObservedAt < TimeSpan.FromSeconds(_options.ProbeCacheSeconds))
            {
                return Result<SearchIndexHealth>.Success(cached.Health);
            }
        }

        var stopwatch = Stopwatch.StartNew();

        var healthResponse = await _client.Cluster
            .HealthAsync(new Elastic.Clients.Elasticsearch.Cluster.HealthRequest(indexName)
            {
                WaitForStatus = HealthStatus.Yellow,
                Timeout = TimeSpan.FromSeconds(1),
            }, cancellationToken)
            .ConfigureAwait(false);

        // Yellow is healthy — a single-node cluster is permanently yellow, and gating readiness on
        // green is a documented defect class in Elastic's own Helm charts. Only red or a timeout fails.
        var reachable = healthResponse.IsValidResponse
            && healthResponse.Status is HealthStatus.Yellow or HealthStatus.Green;

        if (reachable && healthResponse.Status == HealthStatus.Yellow)
        {
            _logger.ElasticSearchClusterYellowAccepted(indexName);
        }

        var indexAddressable = false;
        var searchable = false;
        long documentCount = 0;
        string? schemaFingerprint = null;

        if (reachable)
        {
            try
            {
                var existsResponse = await _client.Indices.ExistsAsync(indexName, cancellationToken).ConfigureAwait(false);
                indexAddressable = existsResponse.IsValidResponse && existsResponse.Exists;
            }
            catch
            {
                indexAddressable = false;
            }

            if (indexAddressable)
            {
                try
                {
                    var countResponse = await _client
                        .CountAsync(new CountRequest(indexName) { Query = new Query { MatchAll = new MatchAllQuery() } }, cancellationToken)
                        .ConfigureAwait(false);
                    searchable = countResponse.IsValidResponse;
                    documentCount = countResponse.IsValidResponse ? countResponse.Count : 0;
                }
                catch
                {
                    searchable = false;
                }

                try
                {
                    var mappingResponse = await _client.Indices.GetMappingAsync(indexName, cancellationToken).ConfigureAwait(false);
                    if (mappingResponse.IsValidResponse)
                    {
                        var indexMappingRecord = mappingResponse.Mappings.Values.FirstOrDefault();
                        if (indexMappingRecord?.Mappings.Meta is { } meta && meta.TryGetValue(FingerprintMetaKey, out var fingerprintValue))
                        {
                            schemaFingerprint = fingerprintValue?.ToString();
                        }
                    }
                }
                catch
                {
                    schemaFingerprint = null;
                }
            }
        }

        var engineVersion = string.Empty;
        try
        {
            var infoResponse = await _client.InfoAsync(cancellationToken).ConfigureAwait(false);
            engineVersion = infoResponse.IsValidResponse ? infoResponse.Version.Number : string.Empty;
        }
        catch
        {
            engineVersion = string.Empty;
        }

        stopwatch.Stop();

        if (!reachable || !indexAddressable || !searchable)
        {
            _logger.ElasticSearchProbeDegraded(indexName, healthResponse.Status.ToString(), indexAddressable, searchable);
        }

        var health = new SearchIndexHealth
        {
            Reachable = reachable,
            IndexAddressable = indexAddressable,
            Searchable = searchable,
            DocumentCount = documentCount,
            // ElasticSearch has no equivalent scalar to Meilisearch's global task queue depth —
            // permanently null, never a TODO. 13.ServiceDefaults must never treat null as unhealthy.
            PendingWriteCount = null,
            EngineVersion = engineVersion,
            SchemaFingerprint = schemaFingerprint,
            Latency = stopwatch.Elapsed,
        };

        lock (_cacheLock)
        {
            _probeCache[indexName] = (health, DateTimeOffset.UtcNow);
        }

        return Result<SearchIndexHealth>.Success(health);
    }

    private static Properties BuildProperties(SearchIndexDefinition definition)
    {
        var propertiesDictionary = new Dictionary<PropertyName, IProperty>();

        foreach (var field in definition.Fields)
        {
            propertiesDictionary[field.Name] = field.Kind switch
            {
                SearchFieldKind.Text => new TextProperty(),
                SearchFieldKind.Keyword => new KeywordProperty(),
                SearchFieldKind.Integer => new IntegerNumberProperty(),
                SearchFieldKind.Decimal => new DoubleNumberProperty(),
                SearchFieldKind.Boolean => new BooleanProperty(),
                SearchFieldKind.DateTimeOffset => new DateProperty(),
                _ => throw new NotSupportedException($"Unrecognized SearchFieldKind '{field.Kind}'."),
            };
        }

        if (definition.TenantField is { } tenantField && !propertiesDictionary.ContainsKey(tenantField))
        {
            propertiesDictionary[tenantField] = new KeywordProperty();
        }

        return new Properties(propertiesDictionary);
    }
}
