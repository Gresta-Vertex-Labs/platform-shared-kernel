using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Error = SharedKernel.Primitives.Errors.Error;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.ElasticSearch.Errors;

/// <summary>
/// Classifies a failed ElasticSearch response, or a thrown exception, onto the canonical
/// <see cref="SearchErrors"/> vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// <b>Classification, not just failure.</b> The ElasticSearch client does not throw for a failed call: it
/// returns a response whose <c>IsValidResponse</c> is <see langword="false"/>. Both providers classify into
/// the same error vocabulary, so a consumer's handling is identical whichever engine is configured.
/// A transport-level failure (no HTTP status at all) is checked first; only then are server-reported
/// status codes consulted.
/// </para>
/// <para>
/// <b>No engine text in an <see cref="Error"/>.</b> The server <c>reason</c>, the response body and
/// <c>DebugInformation</c> repeat query text, filter values, hosts and headers, so none of them is read here.
/// The only engine-authored value that survives is <see cref="BoundedEngineCode"/>, the server's
/// <c>error.type</c> when it is a short snake-case token. Engine detail reaches the log through the exception
/// (<see cref="OriginalException"/>), at the trust level of logs.
/// </para>
/// <para>
/// <b>Classify or rethrow.</b> A recognised fault becomes a failure <see cref="Error"/>; an unrecognised
/// exception is rethrown with its stack by <see cref="ExceptionDispatchInfo"/> and never becomes a soft
/// failure. The caller's own cancellation always propagates.
/// </para>
/// </remarks>
internal static partial class ElasticSearchFaultMapper
{
    /// <summary>
    /// The <c>error.type</c> ElasticSearch reports (HTTP 404, as a root cause) for an expired or closed point-in-time. A PIT
    /// that lapsed between open and first read is the same signal. Named so no string comparison is
    /// scattered in the cursor class.
    /// </summary>
    internal const string PointInTimeMissingErrorType = "search_context_missing_exception";

    [GeneratedRegex("^[a-z0-9_.]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BoundedCodePattern();

    /// <summary>
    /// Returns the server's <c>error.type</c> when it matches <c>^[a-z0-9_.]{1,64}$</c>, otherwise
    /// <see langword="null"/>. Never the server <c>reason</c>.
    /// </summary>
    public static string? BoundedEngineCode(ElasticsearchResponse response)
    {
        var type = response.ElasticsearchServerError?.Error?.Type;
        return type is not null && BoundedCodePattern().IsMatch(type) ? type : null;
    }

    /// <summary>Gets the exception the transport captured for a failed <paramref name="response"/>, if any.</summary>
    public static Exception? OriginalException(ElasticsearchResponse response) =>
        response.ApiCallDetails?.OriginalException;

    /// <summary>
    /// Whether <paramref name="response"/> carries the point-in-time-missing signal: HTTP 404 and the
    /// <see cref="PointInTimeMissingErrorType"/> error type as the top-level type or as a root cause.
    /// </summary>
    /// <remarks>
    /// VERIFIED against ElasticSearch 9.4.2: a point-in-time that was closed (or has lapsed) answers 404 with a
    /// top-level <c>search_phase_execution_exception</c> whose single root cause is
    /// <c>search_context_missing_exception</c>. A bogus id answers 400 <c>illegal_argument_exception</c> and is
    /// <em>not</em> this signal.
    /// </remarks>
    public static bool IsPointInTimeMissing(ElasticsearchResponse response)
    {
        if (response.ApiCallDetails?.HttpStatusCode != 404 || response.ElasticsearchServerError?.Error is not { } error)
        {
            return false;
        }

        return string.Equals(error.Type, PointInTimeMissingErrorType, StringComparison.Ordinal)
            || (error.RootCause?.Any(cause => string.Equals(cause.Type, PointInTimeMissingErrorType, StringComparison.Ordinal)) ?? false);
    }

    /// <summary>
    /// The ElasticSearch client wraps most exceptions it surfaces in an <see cref="UnexpectedTransportException"/>
    /// (and a serializer fault in two layers). Returns the innermost non-wrapper exception.
    /// </summary>
    public static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is UnexpectedTransportException { InnerException: { } inner })
        {
            current = inner;
        }

        return current;
    }

    /// <summary>
    /// When the caller's token is cancelled and <paramref name="exception"/> (possibly wrapped by the client) is a
    /// cancellation, returns that cancellation; otherwise <see langword="null"/>.
    /// </summary>
    public static OperationCanceledException? CallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested && Unwrap(exception) is OperationCanceledException cancellation
            ? cancellation
            : null;

    /// <summary>
    /// Classifies a thrown <paramref name="exception"/>; returns <see langword="false"/> when it is not a
    /// recognised engine or transport fault (the caller then rethrows).
    /// </summary>
    /// <remarks>
    /// A caller-requested cancellation is never classified here; a cancellation or timeout the caller did
    /// not ask for (the HTTP-client timeout) is <c>search.timeout</c>.
    /// </remarks>
    public static bool TryClassify(Exception exception, string operation, string? documentTypeName, out Error error)
    {
        var leaf = Unwrap(exception);
        switch (leaf)
        {
            case TimeoutException:
            case OperationCanceledException:
                error = SearchErrors.Timeout(operation, TimeSpan.Zero);
                return true;
            case HttpRequestException:
            case SocketException:
            case IOException:
            case TransportException:
            case System.Security.Authentication.AuthenticationException:
                error = SearchErrors.Unreachable(SearchWellKnown.ElasticSearchProviderName);
                return true;
            case JsonException:
                error = SearchErrors.EngineFault(SearchWellKnown.ElasticSearchProviderName, operation);
                return true;
            case NotSupportedException or InvalidOperationException when IsSerializerContextFault(exception):
                error = ElasticSearchErrors.SourceSerializerContextMissing(documentTypeName ?? "unknown");
                return true;
            default:
                error = Error.None;
                return false;
        }
    }

    /// <summary>
    /// Maps the failed <paramref name="response"/> for <paramref name="operation"/> against
    /// <paramref name="indexName"/> onto a canonical <see cref="Error"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// The response failed because the caller's <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <remarks>An unrecognised transport exception is rethrown (never mapped to a soft failure).</remarks>
    public static Error Map(
        ElasticsearchResponse response,
        string indexName,
        string operation,
        CancellationToken cancellationToken,
        string? documentTypeName = null)
    {
        var apiCall = response.ApiCallDetails;
        var original = apiCall?.OriginalException;

        if (original is not null && CallerCancellation(original, cancellationToken) is { } cancellation)
        {
            ExceptionDispatchInfo.Capture(cancellation).Throw();
        }

        // No HTTP status means the request never reached a node: connection refused, DNS failure,
        // TLS handshake failure, a client-side timeout, or every node in the pool already marked dead.
        if (apiCall is null || apiCall.HttpStatusCode is null)
        {
            if (original is null)
            {
                return SearchErrors.Unreachable(SearchWellKnown.ElasticSearchProviderName);
            }

            if (!TryClassify(original, operation, documentTypeName, out var classified))
            {
                ExceptionDispatchInfo.Capture(original).Throw();
            }

            return classified;
        }

        return apiCall.HttpStatusCode.Value switch
        {
            401 or 403 => SearchErrors.Unauthorized(indexName, operation),
            404 => SearchErrors.IndexNotFound(indexName),
            408 => SearchErrors.Timeout(operation, TimeSpan.Zero),

            // 429 is ElasticSearch's rejected-execution / circuit-breaker signal. It is a capacity
            // condition, not a malformed request: the identical call succeeds once load drops, which is
            // the same operational shape as unreachability and must be told apart from a 400.
            429 or 502 or 503 or 504 => SearchErrors.Unreachable(SearchWellKnown.ElasticSearchProviderName),

            409 => SearchErrors.IndexAlreadyExists(indexName),
            400 => SearchErrors.InvalidSearchRequest(DescribeRejection(response)),
            _ => SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, operation, BoundedEngineCode(response)),
        };
    }

    /// <summary>
    /// Maps a failed write response, preserving the write-rejected classification for the
    /// server-refused-this-document case while still reporting a down or overloaded cluster as such.
    /// </summary>
    public static Error MapWrite(
        ElasticsearchResponse response,
        string indexName,
        string operation,
        CancellationToken cancellationToken,
        string? documentTypeName = null)
    {
        var error = Map(response, indexName, operation, cancellationToken, documentTypeName);

        // A 400 on a write is the engine rejecting this specific document (a mapping conflict, a
        // malformed value), which is a write rejection rather than a malformed search request.
        return error.Code == SearchErrors.InvalidSearchRequest(string.Empty).Code
            ? SearchErrors.WriteRejected(indexName, DescribeRejection(response))
            : error;
    }

    /// <summary>Kernel wording for an engine rejection, plus the bounded engine code when there is one.</summary>
    internal static string DescribeRejection(ElasticsearchResponse response) =>
        BoundedEngineCode(response) is { } code
            ? $"the engine rejected the request with error '{code}'."
            : "the engine rejected the request.";

    /// <summary>Kernel wording for one bulk item the engine rejected, plus its bounded error type when there is one.</summary>
    public static string DescribeItemRejection(string? errorType) =>
        errorType is not null && BoundedCodePattern().IsMatch(errorType)
            ? $"the engine rejected the document with error '{errorType}'."
            : "the engine rejected the document.";

    // Serializer-context fault detection: with reflection-based serialization disabled and no
    // source-generated context, System.Text.Json throws InvalidOperationException or NotSupportedException
    // whose text names the serializer-context mechanism. Only the exception type and a marker are inspected
    // here; the text is never copied into an Error.
    private static bool IsSerializerContextFault(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("JsonSerializerContext", StringComparison.Ordinal)
                || message.Contains("TypeInfoResolver", StringComparison.Ordinal)
                || message.Contains("Reflection-based serialization", StringComparison.Ordinal)
                || message.Contains("JsonTypeInfo", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
