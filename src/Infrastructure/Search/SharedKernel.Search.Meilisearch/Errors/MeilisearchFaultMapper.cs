using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <b>Why this type exists.</b> The Meilisearch SDK signals every failure by throwing: an unreachable
/// instance, a rejected API key, a missing index, a malformed filter. ElasticSearch's client does the
/// opposite: it returns a response whose <c>IsValidResponse</c> is <see langword="false"/> and throws
/// nothing. Left alone, the identical operational condition surfaced as a thrown exception out of a
/// <c>Task&lt;Result&lt;T&gt;&gt;</c> on one provider and as a failed <c>Result</c> on the other.
/// </para>
/// <para>
/// <b>Classify or rethrow.</b> <see cref="TryMap"/> returns an <see cref="Error"/> only for a recognised
/// engine or transport fault (it distinguishes the retryable-transport class from the permanent-request
/// class) and <see langword="null"/> for anything else, which the call site rethrows with a plain
/// <c>throw;</c>. A <c>SwitchExpressionException</c> from a translation switch, an
/// <see cref="InvalidOperationException"/> or a <see cref="NullReferenceException"/> is a defect and is
/// never reported as a soft <c>search.engine_fault</c>.
/// </para>
/// <para>
/// <b>No engine text in an <see cref="Error"/>.</b> <c>MeilisearchApiError.Message</c> echoes the filter
/// expression and <c>Exception.Message</c> may carry a URL. Neither is read here. The only engine-authored
/// value that survives is <see cref="BoundedEngineCode"/>, the engine's own error code when it is a short
/// snake-case token. The exception itself goes to the log.
/// </para>
/// <para>
/// <b>Cancellation.</b> A caller-requested <see cref="OperationCanceledException"/> is never mapped. A
/// cancellation or timeout the caller did <em>not</em> ask for (the HTTP-client timeout) is
/// <c>search.timeout</c>.
/// </para>
/// </remarks>
internal static partial class MeilisearchFaultMapper
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

    [GeneratedRegex("^[a-z0-9_.]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BoundedCodePattern();

    /// <summary>
    /// Returns <paramref name="code"/> when it matches <c>^[a-z0-9_.]{1,64}$</c>, otherwise
    /// <see langword="null"/>. Applied to every engine-supplied code before it can reach an
    /// <see cref="Error"/> or a log template.
    /// </summary>
    public static string? BoundedEngineCode(string? code) =>
        code is not null && BoundedCodePattern().IsMatch(code) ? code : null;

    /// <summary>
    /// Classifies <paramref name="exception"/> onto the canonical vocabulary; returns
    /// <see langword="false"/> when it is not a recognised engine or transport fault (the caller rethrows).
    /// </summary>
    /// <param name="exception">The exception the SDK or HTTP stack threw.</param>
    /// <param name="indexName">The index the operation targeted.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="cancellationToken">
    /// The caller's token. A cancellation raised while it is cancelled is the caller's own and is never mapped.
    /// </param>
    public static bool TryMap(
        Exception exception, string indexName, string operation, CancellationToken cancellationToken, out Error error)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            error = Error.None;
            return false;
        }

        error = exception switch
        {
            global::Meilisearch.MeilisearchCommunicationError
                => SearchErrors.Unavailable(SearchWellKnown.MeilisearchProviderName),

            global::Meilisearch.MeilisearchTimeoutError
                => SearchErrors.Timeout(operation),

            global::Meilisearch.MeilisearchApiError apiError
                => MapApiError(apiError, indexName, operation),

            // The SDK does not wrap every transport failure in MeilisearchCommunicationError: a bare
            // HttpRequestException reaches callers on several call paths (verified against 0.20.0 on
            // Index.GetDocumentAsync and Index.GetSettingsAsync). A response-less instance means the
            // request never got an answer, which is unreachability; one carrying a status code is
            // classified the same way an API error would be.
            HttpRequestException { StatusCode: null }
                => SearchErrors.Unavailable(SearchWellKnown.MeilisearchProviderName),

            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                => SearchErrors.EngineUnauthorized(indexName, operation),

            HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                => SearchErrors.IndexNotFound(indexName),

            HttpRequestException
            {
                StatusCode: HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout,
            }
                => SearchErrors.Unavailable(SearchWellKnown.MeilisearchProviderName),

            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout }
                => SearchErrors.Timeout(operation),

            // Any other HTTP status is a reply the kernel has no specific mapping for: a recognised engine
            // answer, so a soft failure without engine text.
            HttpRequestException
                => SearchErrors.EngineFault(SearchWellKnown.MeilisearchProviderName, operation),

            SocketException or IOException or System.Security.Authentication.AuthenticationException
                => SearchErrors.Unavailable(SearchWellKnown.MeilisearchProviderName),

            TimeoutException
                => SearchErrors.Timeout(operation),

            // An HttpClient whose Timeout elapses raises TaskCanceledException, not TimeoutException, and
            // that is the single most common shape "the search engine is down" actually takes, because an
            // unresponsive or firewalled instance leaves the request hanging rather than refusing it.
            // Reaching this arm means the caller's own token was NOT the cause (guarded above).
            OperationCanceledException
                => SearchErrors.Timeout(operation),

            // An engine payload the SDK cannot read is an engine-side fact.
            JsonException
                => SearchErrors.EngineFault(SearchWellKnown.MeilisearchProviderName, operation),

            _ => Error.None,
        };

        return error != Error.None;
    }

    private static Error MapApiError(
        global::Meilisearch.MeilisearchApiError apiError, string indexName, string operation)
    {
        var code = apiError.Code ?? string.Empty;
        var bounded = BoundedEngineCode(code);

        if (UnauthorizedCodes.Contains(code))
        {
            return SearchErrors.EngineUnauthorized(indexName, operation);
        }

        if (InvalidFilterCodes.Contains(code))
        {
            return SearchErrors.InvalidFilter(Describe("the engine rejected the filter", bounded));
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
                => SearchErrors.InvalidSearchRequest(Describe("the engine rejected the paging parameters", bounded)),
            _ => SearchErrors.EngineFault(SearchWellKnown.MeilisearchProviderName, operation, bounded),
        };
    }

    private static string Describe(string wording, string? boundedCode) =>
        boundedCode is null ? $"{wording}." : $"{wording} with error '{boundedCode}'.";
}
