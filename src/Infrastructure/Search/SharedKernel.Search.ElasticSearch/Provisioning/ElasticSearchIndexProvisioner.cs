using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Analysis;
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
    private const string TextAnalysisMetaKey = "sk_text_analysis_fingerprint";

    /// <summary>Elasticsearch's error type for creating an index that already exists.</summary>
    private const string IndexAlreadyExistsErrorType = "resource_already_exists_exception";

    /// <summary>
    /// The name of the custom analyzer this provider installs on an index that declares synonyms or
    /// stop words, and assigns to every <see cref="SearchFieldKind.Text"/> field.
    /// </summary>
    /// <remarks>
    /// A single named analyzer covering every searchable field is what makes the neutral declaration
    /// honest: Meilisearch applies its <c>synonyms</c>/<c>stopWords</c> settings index-wide, so
    /// per-field analyzers would not be portable even though ElasticSearch supports them.
    /// </remarks>
    private const string AnalyzerName = "sharedkernel_text";

    private const string SynonymFilterName = "sharedkernel_synonyms";
    private const string StopFilterName = "sharedkernel_stop";

    private readonly ElasticsearchClient _client;
    private readonly ElasticSearchOptions _options;
    private readonly IReadOnlyDictionary<string, SearchIndexDefinition> _registeredDefinitions;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _completionFields;
    private readonly IReadOnlyDictionary<string, string> _writeAliases;
    private readonly ILogger<ElasticSearchIndexProvisioner> _logger;
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<string, (SearchIndexHealth Health, DateTimeOffset ObservedAt)> _probeCache = [];

    /// <summary>Initializes a new <see cref="ElasticSearchIndexProvisioner"/>.</summary>
    /// <param name="client">The shared ElasticSearch client.</param>
    /// <param name="options">The validated provider options.</param>
    /// <param name="registeredDefinitions">
    /// Every index definition the composition root registered, keyed by index name — the input to
    /// <see cref="VerifyRegisteredIndexesAsync"/>.
    /// </param>
    /// <param name="completionFields">
    /// The ElasticSearch-exclusive completion (suggest) fields declared per index via
    /// <c>ElasticSearchBuilder.WithCompletionField</c>, keyed by index name.
    /// </param>
    /// <param name="writeAliases">
    /// The write alias each registered index was declared with, keyed by read alias. Checked by
    /// <see cref="VerifyRegisteredIndexesAsync"/>, because ElasticSearch auto-creates an index on write:
    /// a write alias that resolves to nothing lets every write succeed into a mapping-less index nobody
    /// reads, with no error anywhere.
    /// </param>
    /// <param name="logger">The logger.</param>
    public ElasticSearchIndexProvisioner(
        ElasticsearchClient client,
        ElasticSearchOptions options,
        IReadOnlyDictionary<string, SearchIndexDefinition> registeredDefinitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> completionFields,
        IReadOnlyDictionary<string, string> writeAliases,
        ILogger<ElasticSearchIndexProvisioner> logger)
    {
        _client = client;
        _options = options;
        _registeredDefinitions = registeredDefinitions;
        _completionFields = completionFields;
        _writeAliases = writeAliases;
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

        var hasTextAnalysis = definition.Synonyms.Count > 0 || definition.StopWords.Count > 0;
        var properties = BuildProperties(definition, hasTextAnalysis, GetCompletionFields(definition.Name));
        var meta = new Dictionary<string, object>
        {
            [FingerprintMetaKey] = definition.ComputeFingerprint(),
            [TextAnalysisMetaKey] = ComputeTextAnalysisFingerprint(definition),
        };

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
                    Analysis = hasTextAnalysis ? BuildAnalysis(definition) : null,
                },
            };

            var createResponse = await _client.Indices.CreateAsync(createRequest, cancellationToken).ConfigureAwait(false);
            if (createResponse.IsValidResponse)
            {
                _logger.ElasticSearchIndexEnsured(definition.Name, definition.Fields.Count, definition.MaxTotalHits);
                if (hasTextAnalysis)
                {
                    _logger.ElasticSearchTextAnalysisApplied(
                        definition.Name, definition.Synonyms.Count, definition.StopWords.Count);
                }

                return Result.Success();
            }

            // Replicas starting together all see "no index" and all create it; every one but the first is told
            // resource_already_exists_exception. The index (created whole: mappings and analysis in one request)
            // is there, so continue as on an existing one below.
            if (!string.Equals(
                    createResponse.ElasticsearchServerError?.Error?.Type,
                    IndexAlreadyExistsErrorType,
                    StringComparison.Ordinal))
            {
                return Result.Failure(SearchErrors.EngineFault(
                    SearchWellKnown.ElasticSearchProviderName, "EnsureIndexAsync", createResponse.DebugInformation));
            }
        }

        // ElasticSearch cannot change an open index's analysis settings at all, so a changed synonym or
        // stop-word list on a live index is a conflict, never an in-place update — and the Meilisearch
        // sibling refuses the same change for the same reason even though its own engine would allow it,
        // so the two providers stay observably identical. The remedy is the one an incompatible field
        // mapping already has: provision a staging index, bulk-load it, then CutoverAsync.
        if (hasTextAnalysis)
        {
            var analysisConflict = await DetectAnalysisConflictAsync(definition, cancellationToken)
                .ConfigureAwait(false);
            if (analysisConflict is not null)
            {
                return Result.Failure(analysisConflict);
            }
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

    /// <summary>Measures the index named <paramref name="indexName"/> for its readiness probe (<see cref="SearchIndexReadinessProbe"/>).</summary>
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

        // Deliberately UNSCOPED (the parameterless HealthRequest — overall cluster health), never
        // HealthRequest(indexName). The index-scoped overload 404s/fails when indexName does not exist
        // yet, which would collapse Reachable and IndexAddressable into the same signal — exactly the
        // anti-pattern SearchIndexHealth.IndexAddressable's own contract note warns against ("a green
        // cluster with a missing or misnamed read alias passes every cluster-health check"). Reachable
        // must answer "is the cluster itself up" independent of whether THIS index/alias exists;
        // IndexAddressable (computed separately below) answers the latter question. Confirmed via a
        // real container: HealthRequest(indexName) against a genuinely nonexistent index name returned
        // an invalid response, incorrectly reporting Reachable = false for a fully healthy cluster.
        var healthResponse = await _client.Cluster
            .HealthAsync(new Elastic.Clients.Elasticsearch.Cluster.HealthRequest
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

    private IReadOnlyList<string> GetCompletionFields(string indexName)
        => _completionFields.TryGetValue(indexName, out var fields) ? fields : [];

    private static Properties BuildProperties(
        SearchIndexDefinition definition, bool hasTextAnalysis, IReadOnlyList<string> completionFields)
    {
        var propertiesDictionary = new Dictionary<PropertyName, IProperty>();

        foreach (var field in definition.Fields)
        {
            propertiesDictionary[field.Name] = field.Kind switch
            {
                // The custom analyzer is attached only to text fields, and only when the definition
                // actually declares synonyms or stop words — an index that declares neither keeps
                // ElasticSearch's standard analyzer and its existing mappings unchanged.
                SearchFieldKind.Text => hasTextAnalysis
                    ? new TextProperty { Analyzer = AnalyzerName }
                    : new TextProperty(),
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

        foreach (var completionField in completionFields)
        {
            // The tenant field doubles as a category context on the completion mapping. The completion
            // suggester runs against its own FST and ignores the surrounding query entirely, so a
            // query-level tenant filter has no effect on it — a context is the only mechanism that can
            // scope a suggestion, and declaring it here at provisioning time is what lets
            // ISuggestSearch<TDocument> keep the same fail-closed tenant rule as every neutral read.
            propertiesDictionary[completionField] = new CompletionProperty
            {
                Contexts = definition.TenantField is { } contextField
                    ?
                    [
                        new SuggestContext
                        {
                            Name = contextField,
                            Type = "category",
                            Path = contextField,
                        },
                    ]
                    : null,
            };
        }

        return new Properties(propertiesDictionary);
    }

    /// <summary>
    /// Builds the index-level analysis chain implementing <paramref name="definition"/>'s neutral
    /// synonym and stop-word declarations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Synonyms are emitted in Solr explicit-mapping form (<c>term =&gt; term, replacement</c>), not
    /// equivalence form (<c>term, replacement</c>).</b> Equivalence is two-way and has no Meilisearch
    /// counterpart; explicit mapping is one-way, which is exactly what Meilisearch's own
    /// <c>synonyms</c> setting does. The original term is repeated on the right-hand side because an
    /// explicit mapping <em>replaces</em> the matched token — omitting it would stop the document
    /// matching the very word the caller typed.
    /// </para>
    /// <para>
    /// Filter order matters: lowercasing runs first so a capitalised query term still matches a
    /// lower-case stop word or synonym key, then stop-word removal, then synonym expansion — expanding
    /// before removal would reintroduce a stop word as a synonym replacement.
    /// </para>
    /// </remarks>
    private static IndexSettingsAnalysis BuildAnalysis(SearchIndexDefinition definition)
    {
        var filterNames = new List<string> { "lowercase" };
        var tokenFilters = new Dictionary<string, ITokenFilter>(StringComparer.Ordinal);

        if (definition.StopWords.Count > 0)
        {
            tokenFilters[StopFilterName] = new StopTokenFilter
            {
                Stopwords = definition.StopWords.ToArray(),
            };
            filterNames.Add(StopFilterName);
        }

        if (definition.Synonyms.Count > 0)
        {
            tokenFilters[SynonymFilterName] = new SynonymTokenFilter
            {
                Synonyms = definition.Synonyms
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => $"{entry.Key} => {entry.Key}, {string.Join(", ", entry.Value)}")
                    .ToArray(),
            };
            filterNames.Add(SynonymFilterName);
        }

        return new IndexSettingsAnalysis
        {
            TokenFilters = new TokenFilters(tokenFilters),
            Analyzers = new Analyzers(new Dictionary<string, IAnalyzer>(StringComparer.Ordinal)
            {
                [AnalyzerName] = new CustomAnalyzer
                {
                    Tokenizer = "standard",
                    Filter = filterNames,
                },
            }),
        };
    }

    /// <summary>
    /// Returns <see cref="SearchErrors.IndexDefinitionConflict"/> when the live index's text-analysis
    /// settings do not match what <paramref name="definition"/> declares, and <see langword="null"/>
    /// when they do.
    /// </summary>
    /// <remarks>
    /// The comparison is made against a dedicated text-analysis fingerprint written into mapping
    /// <c>_meta</c> at provisioning time, rather than by reading the live analyzer definition back and
    /// comparing it structurally. Reading it back would compare ElasticSearch's own normalised,
    /// defaults-filled rendering of the analysis chain against what this provider intended to send, and
    /// those differ in ways that have nothing to do with the caller's declaration. Hashing the neutral
    /// declaration is exact, and it is the same mechanism — and the same storage location — the whole
    /// schema fingerprint already uses.
    /// </remarks>
    private async Task<Primitives.Errors.Error?> DetectAnalysisConflictAsync(
        SearchIndexDefinition definition, CancellationToken cancellationToken)
    {
        var mappingResponse = await _client.Indices
            .GetMappingAsync(definition.Name, cancellationToken)
            .ConfigureAwait(false);

        if (!mappingResponse.IsValidResponse)
        {
            return SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName,
                "EnsureIndexAsync",
                mappingResponse.DebugInformation);
        }

        var liveFingerprint = mappingResponse.Mappings.Values.FirstOrDefault()?.Mappings.Meta is { } meta
            && meta.TryGetValue(TextAnalysisMetaKey, out var value)
                ? value?.ToString()
                : null;

        var expected = ComputeTextAnalysisFingerprint(definition);
        if (string.Equals(liveFingerprint, expected, StringComparison.Ordinal))
        {
            return null;
        }

        // A short, field-shaped token, not a sentence: SearchErrors.IndexDefinitionConflict renders its
        // second argument as "Field '{0}' on index '{1}' conflicts with the live mapping", so a prose
        // explanation here produces "Field 'the declared synonyms or stop words differ from…'" in an
        // error a consumer reads. The factory's own message already carries the remedy.
        return SearchErrors.IndexDefinitionConflict(
            definition.Name,
            liveFingerprint is null ? "analysis (never applied)" : "analysis (synonyms/stopWords)");
    }

    /// <summary>
    /// Returns a drift description when the index's declared write alias does not resolve to something
    /// this platform provisioned, and <see langword="null"/> when it does.
    /// </summary>
    /// <remarks>
    /// Resolving is not enough on its own — an auto-created index also "exists". What is checked is that
    /// the write target carries the <c>sk_schema_fingerprint</c> mapping metadata
    /// <see cref="EnsureIndexAsync"/> writes, which an index ElasticSearch created implicitly on first
    /// write never has. When the write alias is the read alias (the shape a service starts with), the
    /// read-path fingerprint check above has already proved it and this is a no-op.
    /// </remarks>
    private async Task<string?> DetectWriteAliasDriftAsync(string indexName, CancellationToken cancellationToken)
    {
        if (!_writeAliases.TryGetValue(indexName, out var writeAlias)
            || string.Equals(writeAlias, indexName, StringComparison.Ordinal))
        {
            return null;
        }

        var mappingResponse = await _client.Indices.GetMappingAsync(writeAlias, cancellationToken).ConfigureAwait(false);
        if (!mappingResponse.IsValidResponse)
        {
            return $"{indexName}: write alias '{writeAlias}' does not resolve — ElasticSearch auto-creates " +
                   "an index on write, so writes would silently land in a mapping-less index nothing reads";
        }

        var hasFingerprint = mappingResponse.Mappings.Values.Any(
            m => m.Mappings.Meta is { } meta && meta.ContainsKey(FingerprintMetaKey));

        return hasFingerprint
            ? null
            : $"{indexName}: write alias '{writeAlias}' resolves to an index this platform never " +
              "provisioned (no schema fingerprint) — most likely one ElasticSearch auto-created on a write";
    }

    /// <summary>
    /// Hashes only the neutral text-analysis declaration, so a changed synonym or stop-word list is
    /// detectable independently of an additive field change — which remains legal.
    /// </summary>
    private static string ComputeTextAnalysisFingerprint(SearchIndexDefinition definition)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var synonym in definition.Synonyms.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            builder.Append(synonym.Key).Append("=>");
            foreach (var replacement in synonym.Value.OrderBy(v => v, StringComparer.Ordinal))
            {
                builder.Append(replacement).Append(',');
            }

            builder.Append('\n');
        }

        builder.Append("--\n");
        foreach (var stopWord in definition.StopWords.OrderBy(w => w, StringComparer.Ordinal))
        {
            builder.Append(stopWord).Append('\n');
        }

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    /// <inheritdoc />
    public async Task<Result> VerifyRegisteredIndexesAsync(CancellationToken cancellationToken = default)
    {
        if (_registeredDefinitions.Count == 0)
        {
            return Result.Success();
        }

        var drifted = new List<string>();

        foreach (var (indexName, definition) in _registeredDefinitions)
        {
            var probeResult = await ProbeAsync(indexName, cancellationToken).ConfigureAwait(false);
            if (probeResult.IsFailure)
            {
                drifted.Add($"{indexName}: probe failed ({probeResult.Error.Code})");
                continue;
            }

            var health = probeResult.Value;
            if (!health.Reachable)
            {
                drifted.Add($"{indexName}: cluster unreachable");
                continue;
            }

            if (!health.IndexAddressable)
            {
                drifted.Add($"{indexName}: alias or index not addressable with this service's credentials");
                continue;
            }

            var expectedFingerprint = definition.ComputeFingerprint();
            if (health.SchemaFingerprint is null)
            {
                drifted.Add(
                    $"{indexName}: no schema fingerprint recorded — the index was never provisioned by EnsureIndexAsync");
                continue;
            }

            if (!string.Equals(health.SchemaFingerprint, expectedFingerprint, StringComparison.Ordinal))
            {
                drifted.Add(
                    $"{indexName}: schema fingerprint '{health.SchemaFingerprint}' does not match the registered definition's '{expectedFingerprint}'");
                continue;
            }

            // The write path is checked separately from the read path, because on ElasticSearch they can
            // legitimately address different concrete indexes — and because a broken write alias is
            // invisible until someone reads. ElasticSearch auto-creates an index on write, so a write
            // alias that resolves to nothing does not fail: every write lands in a brand-new,
            // mapping-less, analysis-less index that no read ever touches. The bulk call reports success,
            // the counts look plausible, and the data is simply not where the service is looking. Nothing
            // else in this domain notices, which is exactly why it is checked here.
            var writeAliasDrift = await DetectWriteAliasDriftAsync(indexName, cancellationToken)
                .ConfigureAwait(false);
            if (writeAliasDrift is not null)
            {
                drifted.Add(writeAliasDrift);
            }
        }

        if (drifted.Count == 0)
        {
            foreach (var indexName in _registeredDefinitions.Keys)
            {
                _logger.ElasticSearchIndexVerified(indexName);
            }

            return Result.Success();
        }

        var detail = string.Join("; ", drifted);
        _logger.ElasticSearchIndexDrifted(string.Join(", ", _registeredDefinitions.Keys), detail);
        return Result.Failure(SearchErrors.ProbeFailed(
            string.Join(", ", _registeredDefinitions.Keys),
            $"{drifted.Count} of {_registeredDefinitions.Count} registered index(es) did not match: {detail}"));
    }
}
