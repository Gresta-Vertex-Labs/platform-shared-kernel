using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;

namespace SharedKernel.Search.ElasticSearch.Suggest;

/// <summary>The ElasticSearch implementation of <see cref="ISuggestSearch{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
internal sealed class ElasticSearchSuggestSearch<TDocument> : ISuggestSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    private const string OperationSuggest = "suggest";
    private const string SuggestionName = "sk_suggest";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly IReadOnlyCollection<string> _completionFields;
    private readonly ElasticSearchOptions _options;
    private readonly ILogger<ElasticSearchSuggestSearch<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="ElasticSearchSuggestSearch{TDocument}"/>.</summary>
    public ElasticSearchSuggestSearch(
        ElasticsearchClient client,
        SearchIndexDefinition definition,
        IReadOnlyCollection<string> completionFields,
        ElasticSearchOptions options,
        ILogger<ElasticSearchSuggestSearch<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _completionFields = completionFields;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SearchSuggestion>>> SuggestAsync(
        string suggestField,
        string prefix,
        TenantScope tenantScope,
        int size = 10,
        bool fuzzy = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suggestField))
        {
            return Result<IReadOnlyList<SearchSuggestion>>.Failure(
                SearchErrors.InvalidSearchRequest("A suggest field is required."));
        }

        // Fail at the call, not at the engine: a field that was never declared as a completion field
        // produces an opaque ElasticSearch mapping error, and the caller's real mistake — forgetting
        // WithCompletionField at the composition root — is not recoverable from it.
        if (!_completionFields.Contains(suggestField, StringComparer.Ordinal))
        {
            return Result<IReadOnlyList<SearchSuggestion>>.Failure(SearchErrors.FieldNotSearchable(
                _definition.Name,
                $"{suggestField} (not declared as a completion field; call WithCompletionField on the ElasticSearch builder)"));
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Result<IReadOnlyList<SearchSuggestion>>.Failure(
                SearchErrors.InvalidSearchRequest("A suggestion prefix must not be empty."));
        }

        if (size < 1)
        {
            return Result<IReadOnlyList<SearchSuggestion>>.Failure(
                SearchErrors.InvalidSearchRequest($"Size must be at least 1; received {size}."));
        }

        // Same fail-closed rule as every read on the neutral contract: a tenanted index must never be
        // suggested from without a tenant, or one tenant's content completes another tenant's typing.
        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            _logger.ElasticSearchTenantScopeMissing(_definition.Name);
            return Result<IReadOnlyList<SearchSuggestion>>.Failure(
                SearchErrors.TenantScopeMissing(_definition.Name));
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        using var activity = SearchDiagnostics.StartActivity(OperationSuggest, _definition.Name);

        var suggester = new FieldSuggester
        {
            Completion = new CompletionSuggester
            {
                Field = suggestField!,
                Size = size,
                SkipDuplicates = true,
                Fuzzy = fuzzy ? new SuggestFuzziness { Fuzziness = new Fuzziness("AUTO") } : null,

                // The completion suggester ignores a query-level filter entirely, so tenant isolation
                // has to travel as a context filter on the suggester itself. This is why the tenant
                // field is registered as a category context on the completion mapping at provisioning
                // time — without that, there would be no way to scope a suggestion at all, and this
                // capability could not have shipped for a tenanted index.
                Contexts = _definition.TenantField is { } tenantField
                    ? new Dictionary<Field, ICollection<CompletionContext>>()
                    {
                        [tenantField] = [new CompletionContext { Context = new Context(tenantScope.Tenant!.Value.ToString()) }],
                    }
                    : null,
            },
            Prefix = prefix,
        };

        var request = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>(_definition.Name)
        {
            Size = 0,
            Suggest = new Suggester
            {
                Suggesters = new Dictionary<string, FieldSuggester>(StringComparer.Ordinal)
                {
                    [SuggestionName] = suggester,
                },
            },
        };

        Result<IReadOnlyList<SearchSuggestion>> result;
        try
        {
            var response = await _client.SearchAsync<TDocument>(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsValidResponse)
            {
                result = Result<IReadOnlyList<SearchSuggestion>>.Failure(
                    ElasticSearchFaultMapper.Map(
                        response, _definition.Name, OperationSuggest, string.Join(",", _options.Nodes)));
            }
            else
            {
                var suggestions = MapSuggestions(response);
                _logger.ElasticSearchSuggestExecuted(_definition.Name, suggestions.Count);
                result = Result<IReadOnlyList<SearchSuggestion>>.Success(suggestions);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, OperationSuggest, ex.Message);
            _logger.ElasticSearchOperationFaulted(OperationSuggest, _definition.Name, error.Code);
            result = Result<IReadOnlyList<SearchSuggestion>>.Failure(error);
        }

        SearchDiagnostics.Complete(
            activity, OperationSuggest, _definition.Name, startTimestamp, result.IsFailure ? result.Error.Code : null);
        return result;
    }

    private static IReadOnlyList<SearchSuggestion> MapSuggestions(
        SearchResponse<TDocument> response)
    {
        if (response.Suggest is not { } suggest || !suggest.TryGetValue(SuggestionName, out var entries))
        {
            return [];
        }

        var suggestions = new List<SearchSuggestion>();
        foreach (var entry in entries)
        {
            if (entry is not CompletionSuggest<TDocument> completion)
            {
                continue;
            }

            foreach (var option in completion.Options)
            {
                suggestions.Add(new SearchSuggestion
                {
                    Text = option.Text,
                    DocumentId = option.Id ?? string.Empty,

                    // The client models the response's two score fields as separate properties: Score0
                    // carries `_score`, which is the one a completion suggester actually populates, and
                    // Score carries a plain `score` that this response shape does not emit. Reading only
                    // Score returned 0 for every suggestion — which silently discards the ranking that
                    // is the main reason to use a completion suggester over a prefix query at all.
                    Score = option.Score0 ?? option.Score ?? 0d,
                });
            }
        }

        return suggestions;
    }
}
