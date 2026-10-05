using System.Net;
using System.Net.Http;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Meilisearch.Errors;

/// <summary>
/// Maps an exception thrown by the Meilisearch SDK onto the canonical <see cref="SearchErrors"/>
/// vocabulary, so every member of <c>ISearchIndex&lt;TDocument&gt;</c> and
/// <c>ISearchIndexProvisioner</c> can honour its <c>Result</c>-returning signature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> The Meilisearch SDK signals every failure by throwing — an unreachable
/// instance, a rejected API key, a missing index, a malformed filter. ElasticSearch's client does the
/// opposite: it returns a response whose <c>IsValidResponse</c> is <see langword="false"/> and throws
/// nothing. Left alone, the identical operational condition — "the search engine is down" — surfaced
/// as a thrown exception out of a <c>Task&lt;Result&lt;T&gt;&gt;</c> on one provider and as a failed
/// <c>Result</c> on the other. That is a provider divergence a consumer cannot write correct code
/// against, and it is exactly what this domain's intersection-only rule exists to prevent; it simply
/// had never been applied to the failure path. <c>07.Messaging</c> and <c>08.Storage</c> each made the
/// same correction in their own pre-publish passes: an unreachable dependency is an operational
/// condition, not an exception.
/// </para>
/// <para>
/// <b>Classification is the point, not merely catching.</b> Collapsing everything to
/// <see cref="SearchErrors.EngineFault"/> would still leave a caller unable to tell "retry in a moment"
/// from "your request is wrong and will never succeed". The mapping below distinguishes the
/// retryable-transport class (<see cref="SearchErrors.Unreachable"/>,
/// <see cref="SearchErrors.Timeout"/>) from the permanent-request class
/// (<see cref="SearchErrors.Unauthorized"/>, <see cref="SearchErrors.IndexNotFound"/>,
/// <see cref="SearchErrors.InvalidFilter"/>), and only genuinely unclassifiable faults reach
/// <see cref="SearchErrors.EngineFault"/> — whose rate is, by design, the signal that this mapper has
/// drifted from the engine's current error vocabulary.
/// </para>
/// <para>
/// <b><see cref="OperationCanceledException"/> is never mapped.</b> Cancellation is the caller's own
/// instruction, not a search failure, and must keep propagating so a cancelled request unwinds instead
/// of being reported as an engine error. Every call site rethrows it ahead of this mapper.
/// </para>
/// </remarks>
internal static class MeilisearchFaultMapper
{
    /// <summary>
    /// Meilisearch's own error codes for an authentication or authorisation failure. Sourced from the
    /// engine's documented error-code list; a code outside this set falls through to the generic
    /// classification below rather than being guessed at.
    /// </summary>
    private static readonly HashSet<string> UnauthorizedCodes = new(StringComparer.Ordinal)
    {
        "invalid_api_key",
        "missing_authorization_header",
        "missing_master_key",
        "invalid_api_key_actions",
        "invalid_api_key_indexes",
        "invalid_api_key_expires_at",
    };

    /// <summary>Meilisearch's own error codes for a malformed filter expression.</summary>
    private static readonly HashSet<string> InvalidFilterCodes = new(StringComparer.Ordinal)
    {
        "invalid_search_filter",
        "invalid_document_filter",
        "invalid_search_attributes_to_search_on",
    };

    /// <summary>
    /// Maps <paramref name="exception"/> onto a canonical <see cref="Error"/> for
    /// <paramref name="operation"/> against <paramref name="indexName"/>.
    /// </summary>
    public static Error Map(Exception exception, string indexName, string operation, string endpoint)
    {
        return exception switch
        {
            global::Meilisearch.MeilisearchCommunicationError
                => SearchErrors.Unreachable(SearchWellKnown.MeilisearchProviderName, endpoint),

            global::Meilisearch.MeilisearchTimeoutError
                => SearchErrors.Timeout(operation, TimeSpan.Zero),

            global::Meilisearch.MeilisearchApiError apiError
                => MapApiError(apiError, indexName, operation),

            // The SDK does not wrap every transport failure in MeilisearchCommunicationError — a bare
            // HttpRequestException reaches callers on several call paths (verified against 0.20.0 on
            // Index.GetDocumentAsync and Index.GetSettingsAsync). A response-less instance means the
            // request never got an answer, which is unreachability; one carrying a status code is
            // classified the same way an API error would be.
            HttpRequestException { StatusCode: null }
                => SearchErrors.Unreachable(SearchWellKnown.MeilisearchProviderName, endpoint),

            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                => SearchErrors.Unauthorized(indexName, operation),

            HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                => SearchErrors.IndexNotFound(indexName),

            HttpRequestException
            {
                StatusCode: HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout,
            }
                => SearchErrors.Unreachable(SearchWellKnown.MeilisearchProviderName, endpoint),

            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout }
                => SearchErrors.Timeout(operation, TimeSpan.Zero),

            TimeoutException
                => SearchErrors.Timeout(operation, TimeSpan.Zero),

            // An HttpClient whose Timeout elapses raises TaskCanceledException, not TimeoutException —
            // and that is the single most common shape "the search engine is down" actually takes,
            // because an unresponsive or firewalled instance leaves the request hanging rather than
            // refusing it. Reaching this arm means the caller's own token was NOT the cause: every call
            // site rethrows caller cancellation before this mapper is consulted, precisely so the two
            // cannot be confused. Left unclassified it fell through to EngineFault, which told a caller
            // its request was malformed when the engine had simply stopped answering.
            OperationCanceledException
                => SearchErrors.Timeout(operation, TimeSpan.Zero),

            _ => SearchErrors.EngineFault(
                SearchWellKnown.MeilisearchProviderName, operation, exception.Message),
        };
    }

    private static Error MapApiError(
        global::Meilisearch.MeilisearchApiError apiError, string indexName, string operation)
    {
        var code = apiError.Code ?? string.Empty;

        if (UnauthorizedCodes.Contains(code))
        {
            return SearchErrors.Unauthorized(indexName, operation);
        }

        if (InvalidFilterCodes.Contains(code))
        {
            return SearchErrors.InvalidFilter(apiError.Message);
        }

        return code switch
        {
            "index_not_found" => SearchErrors.IndexNotFound(indexName),
            "index_already_exists" => SearchErrors.IndexAlreadyExists(indexName),
            "document_not_found" => SearchErrors.DocumentNotFound(indexName, "(unknown)"),
            "invalid_document_id" => SearchErrors.InvalidDocumentId("(rejected by engine)"),
            "invalid_search_offset"
                or "invalid_search_limit"
                or "invalid_search_page"
                or "invalid_search_hits_per_page"
                => SearchErrors.InvalidSearchRequest(apiError.Message),
            _ => SearchErrors.EngineFault(
                SearchWellKnown.MeilisearchProviderName, operation, $"{code}: {apiError.Message}"),
        };
    }
}
