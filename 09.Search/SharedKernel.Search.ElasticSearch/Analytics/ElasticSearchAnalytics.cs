using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Querying;

namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The ElasticSearch implementation of <see cref="IAnalyticsSearch{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// Every SDK aggregation type is referenced fully qualified (<c>global::Elastic.Clients.Elasticsearch.Aggregations.*</c>)
/// throughout this file — this domain's own <see cref="Analytics"/> namespace deliberately reuses the
/// same node names (<see cref="TermsAggregation"/>, <see cref="RangeBucket"/>, etc.) as the ElasticSearch
/// client's own aggregation model, so an unqualified reference would be ambiguous at best and silently
/// wrong at worst.
/// </remarks>
internal sealed class ElasticSearchAnalytics<TDocument> : IAnalyticsSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ILogger<ElasticSearchAnalytics<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="ElasticSearchAnalytics{TDocument}"/>.</summary>
    public ElasticSearchAnalytics(
        ElasticsearchClient client, SearchIndexDefinition definition, ILogger<ElasticSearchAnalytics<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AggregationResultSet>> AggregateAsync(
        SearchFilter? filter,
        IReadOnlyCollection<AggregationRequest> aggregations,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            return Result<AggregationResultSet>.Failure(filterResult.Error);
        }

        var aggregationDictionary = new Dictionary<string, global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation>();
        foreach (var request in aggregations)
        {
            aggregationDictionary[GetName(request)] = BuildAggregation(request);
        }

        var searchRequest = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>(_definition.Name)
        {
            Size = 0,
            Query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() },
            Aggregations = aggregationDictionary,
        };

        var response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            var error = ElasticSearchErrors.AggregationFailed(_definition.Name, response.DebugInformation);
            return Result<AggregationResultSet>.Failure(error);
        }

        // response.IsValidResponse == true guarantees Aggregations is populated.
        var resultSet = MapResultSet(response.Aggregations!, aggregations);
        _logger.ElasticSearchAggregationExecuted(_definition.Name, aggregations.Count, response.Took);
        return Result<AggregationResultSet>.Success(resultSet);
    }

    private static string GetName(AggregationRequest request) => request switch
    {
        TermsAggregation t => t.Name,
        CardinalityAggregation c => c.Name,
        StatsAggregation s => s.Name,
        DateHistogramAggregation d => d.Name,
        RangeAggregation r => r.Name,
    };

    private static global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation BuildAggregation(AggregationRequest request)
    {
        switch (request)
        {
            case TermsAggregation terms:
                var termsAggregation = new global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation
                {
                    Terms = new global::Elastic.Clients.Elasticsearch.Aggregations.TermsAggregation
                    {
                        Field = terms.Field,
                        Size = terms.Size,
                    },
                };
                if (terms.SubAggregations.Count > 0)
                {
                    termsAggregation.Aggregations = terms.SubAggregations.ToDictionary(GetName, BuildAggregation);
                }

                return termsAggregation;

            case CardinalityAggregation cardinality:
                return new global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation
                {
                    Cardinality = new global::Elastic.Clients.Elasticsearch.Aggregations.CardinalityAggregation
                    {
                        Field = cardinality.Field,
                    },
                };

            case StatsAggregation stats:
                return new global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation
                {
                    Stats = new global::Elastic.Clients.Elasticsearch.Aggregations.StatsAggregation { Field = stats.Field },
                };

            case DateHistogramAggregation dateHistogram:
                var dateHistogramAggregation = new global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation
                {
                    DateHistogram = new global::Elastic.Clients.Elasticsearch.Aggregations.DateHistogramAggregation
                    {
                        Field = dateHistogram.Field,
                        CalendarInterval = ToCalendarInterval(dateHistogram.Interval),
                    },
                };
                if (dateHistogram.SubAggregations.Count > 0)
                {
                    dateHistogramAggregation.Aggregations = dateHistogram.SubAggregations.ToDictionary(GetName, BuildAggregation);
                }

                return dateHistogramAggregation;

            case RangeAggregation range:
                return new global::Elastic.Clients.Elasticsearch.Aggregations.Aggregation
                {
                    Range = new global::Elastic.Clients.Elasticsearch.Aggregations.RangeAggregation
                    {
                        Field = range.Field,
                        Ranges = range.Ranges
                            .Select(r => new global::Elastic.Clients.Elasticsearch.Aggregations.AggregationRange
                            {
                                Key = r.Key,
                                From = r.From,
                                To = r.To,
                            })
                            .ToArray(),
                    },
                };

            default:
                throw new NotSupportedException(
                    $"Unrecognized AggregationRequest node type '{request.GetType()}' — the closed hierarchy has grown a new node.");
        }
    }

    private static global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval ToCalendarInterval(DateHistogramInterval interval)
        => interval switch
        {
            DateHistogramInterval.Minute => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Minute,
            DateHistogramInterval.Hour => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Hour,
            DateHistogramInterval.Day => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Day,
            DateHistogramInterval.Week => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Week,
            DateHistogramInterval.Month => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Month,
            DateHistogramInterval.Quarter => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Quarter,
            DateHistogramInterval.Year => global::Elastic.Clients.Elasticsearch.Aggregations.CalendarInterval.Year,
            _ => throw new NotSupportedException($"Unrecognized DateHistogramInterval '{interval}'."),
        };

    private static AggregationResultSet MapResultSet(
        global::Elastic.Clients.Elasticsearch.Aggregations.AggregateDictionary aggregations,
        IReadOnlyCollection<AggregationRequest> requests)
    {
        var results = new Dictionary<string, AggregationResult>();
        foreach (var request in requests)
        {
            var mapped = MapSingle(aggregations, request);
            if (mapped is not null)
            {
                results[GetName(request)] = mapped;
            }
        }

        return new AggregationResultSet { Results = results };
    }

    /// <summary>
    /// Maps a bucket's own sub-aggregations, which are <see langword="null"/> when no sub-aggregation
    /// was requested for that bucket — an empty result set in that case, never a null-reference risk
    /// for the caller.
    /// </summary>
    private static AggregationResultSet MapSubAggregations(
        global::Elastic.Clients.Elasticsearch.Aggregations.AggregateDictionary? aggregations,
        IReadOnlyCollection<AggregationRequest> requests)
        => aggregations is null
            ? new AggregationResultSet { Results = new Dictionary<string, AggregationResult>() }
            : MapResultSet(aggregations, requests);

    private static AggregationResult? MapSingle(
        global::Elastic.Clients.Elasticsearch.Aggregations.AggregateDictionary aggregations, AggregationRequest request)
    {
        var name = GetName(request);
        switch (request)
        {
            case TermsAggregation terms:
                if (!aggregations.TryGetAggregate<global::Elastic.Clients.Elasticsearch.Aggregations.StringTermsAggregate>(name, out var termsAggregate))
                {
                    return null;
                }

                var termsBuckets = termsAggregate.Buckets
                    .Select(bucket => new TermsBucket
                    {
                        Key = bucket.Key.TryGetString(out var keyString) ? keyString! : bucket.Key.ToString(),
                        DocCount = bucket.DocCount,
                        SubAggregations = MapSubAggregations(bucket.Aggregations, terms.SubAggregations),
                    })
                    .ToArray();
                return new TermsResult(name, termsBuckets, termsAggregate.SumOtherDocCount ?? 0);

            case CardinalityAggregation:
                return aggregations.TryGetAggregate<global::Elastic.Clients.Elasticsearch.Aggregations.CardinalityAggregate>(name, out var cardinalityAggregate)
                    ? new CardinalityResult(name, cardinalityAggregate.Value)
                    : null;

            case StatsAggregation:
                if (!aggregations.TryGetAggregate<global::Elastic.Clients.Elasticsearch.Aggregations.StatsAggregate>(name, out var statsAggregate))
                {
                    return null;
                }

                return new StatsResult(
                    name, statsAggregate.Count, statsAggregate.Min ?? 0, statsAggregate.Max ?? 0, statsAggregate.Avg ?? 0, statsAggregate.Sum);

            case DateHistogramAggregation dateHistogram:
                if (!aggregations.TryGetAggregate<global::Elastic.Clients.Elasticsearch.Aggregations.DateHistogramAggregate>(name, out var dateHistogramAggregate))
                {
                    return null;
                }

                var dateBuckets = dateHistogramAggregate.Buckets
                    .Select(bucket => new DateHistogramBucket
                    {
                        Key = bucket.Key,
                        DocCount = bucket.DocCount,
                        SubAggregations = MapSubAggregations(bucket.Aggregations, dateHistogram.SubAggregations),
                    })
                    .ToArray();
                return new DateHistogramResult(name, dateBuckets);

            case RangeAggregation:
                if (!aggregations.TryGetAggregate<global::Elastic.Clients.Elasticsearch.Aggregations.RangeAggregate>(name, out var rangeAggregate))
                {
                    return null;
                }

                var rangeBuckets = rangeAggregate.Buckets
                    .Select(bucket => new RangeBucket(bucket.Key ?? string.Empty, bucket.DocCount))
                    .ToArray();
                return new RangeResult(name, rangeBuckets);

            default:
                throw new NotSupportedException(
                    $"Unrecognized AggregationRequest node type '{request.GetType()}' — the closed hierarchy has grown a new node.");
        }
    }
}
