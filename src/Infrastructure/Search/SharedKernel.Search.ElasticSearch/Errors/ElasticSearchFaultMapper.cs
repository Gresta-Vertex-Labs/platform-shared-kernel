using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Error = SharedKernel.Primitives.Errors.Error;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.ElasticSearch.Errors;

/// <summary>
/// Classifies a failed ElasticSearch response onto the canonical <see cref="SearchErrors"/>
/// vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why classification, not just failure.</b> The ElasticSearch client does not throw — it returns a
/// response whose <c>IsValidResponse</c> is <see langword="false"/>. Before the pre-publish pass every
/// such response became either <see cref="SearchErrors.WriteRejected"/> or
/// <see cref="SearchErrors.EngineFault"/> regardless of cause, so a consumer could not distinguish a
/// cluster that was down (retry in a moment, the data is fine) from a request the cluster refused
/// (retrying will never help). Callers cannot write correct retry or circuit-breaking logic against a
/// single undifferentiated failure, and the Meilisearch sibling — whose SDK throws — had the same
/// problem in mirror image. Both providers now classify into the same error vocabulary, so a consumer's
/// handling is identical whichever engine is configured.
/// </para>
/// <para>
/// A transport-level failure (no HTTP status at all, because the request never got an answer) is the
/// clearest unreachability signal there is and is checked first; only then are server-reported status
/// codes consulted.
/// </para>
/// </remarks>
internal static class ElasticSearchFaultMapper
{
    /// <summary>
    /// Maps the failed <paramref name="response"/> for <paramref name="operation"/> against
    /// <paramref name="indexName"/> onto a canonical <see cref="Error"/>.
    /// </summary>
    public static Error Map(ElasticsearchResponse response, string indexName, string operation, string endpoint)
    {
        var apiCall = response.ApiCallDetails;

        // No HTTP status means the request never reached a node — connection refused, DNS failure,
        // TLS handshake failure, or every node in the pool already marked dead.
        if (apiCall is null || apiCall.HttpStatusCode is null)
        {
            return apiCall?.OriginalException is TimeoutException or TaskCanceledException
                ? SearchErrors.Timeout(operation, TimeSpan.Zero)
                : SearchErrors.Unreachable(SearchWellKnown.ElasticSearchProviderName, endpoint);
        }

        return apiCall.HttpStatusCode.Value switch
        {
            401 or 403 => SearchErrors.Unauthorized(indexName, operation),
            404 => SearchErrors.IndexNotFound(indexName),
            408 => SearchErrors.Timeout(operation, TimeSpan.Zero),

            // 429 is ElasticSearch's rejected-execution / circuit-breaker signal. It is a capacity
            // condition, not a malformed request: the identical call succeeds once load drops, which is
            // the same operational shape as unreachability and must be told apart from a 400.
            429 or 502 or 503 or 504 => SearchErrors.Unreachable(
                SearchWellKnown.ElasticSearchProviderName, endpoint),

            409 => SearchErrors.IndexAlreadyExists(indexName),
            400 => SearchErrors.InvalidSearchRequest(DescribeServerError(response)),
            _ => SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, operation, DescribeServerError(response)),
        };
    }

    /// <summary>
    /// Maps a failed write response, preserving the write-rejected classification for the
    /// server-refused-this-document case while still reporting a down or overloaded cluster as such.
    /// </summary>
    public static Error MapWrite(ElasticsearchResponse response, string indexName, string operation, string endpoint)
    {
        var error = Map(response, indexName, operation, endpoint);

        // A 400 on a write is the engine rejecting this specific document — a mapping conflict, a
        // malformed value — which is a write rejection rather than a malformed search request.
        return error.Code == SearchErrors.InvalidSearchRequest(string.Empty).Code
            ? SearchErrors.WriteRejected(indexName, DescribeServerError(response))
            : error;
    }

    /// <summary>
    /// Returns the cluster's own reason for the failure when it supplied one, falling back to the
    /// client's debug information.
    /// </summary>
    /// <remarks>
    /// The server error's <c>reason</c> is preferred over <c>DebugInformation</c> because the latter
    /// embeds the full request body — which for a search carries the caller's query text and filter
    /// values, and therefore potentially personal data, into an <see cref="Error"/> that will be logged.
    /// </remarks>
    private static string DescribeServerError(ElasticsearchResponse response)
    {
        if (response.ElasticsearchServerError is { } serverError)
        {
            var reason = serverError.Error?.Reason;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }
        }

        return response.ApiCallDetails?.HttpStatusCode is { } status
            ? $"ElasticSearch returned HTTP {status}."
            : "ElasticSearch returned an unsuccessful response.";
    }
}
