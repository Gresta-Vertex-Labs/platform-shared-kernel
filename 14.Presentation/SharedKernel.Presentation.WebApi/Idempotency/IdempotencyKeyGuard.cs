using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Reads and checks the <c>Idempotency-Key</c> header, for the header-requirements middleware, the
/// <see cref="IdempotencyKey"/> parameter and <c>GetIdempotencyKey()</c> alike.
/// </summary>
internal static partial class IdempotencyKeyGuard
{
    private const string RequiredMessage = "This request requires an Idempotency-Key header.";

    private static readonly string InvalidMessage = string.Create(
        CultureInfo.InvariantCulture,
        $"The Idempotency-Key header must be 1 to {IdempotencyKey.MaxLength} visible ASCII characters.");

    private enum KeyState
    {
        Missing,
        Invalid,
        Valid,
    }

    /// <summary>Returns the valid key of the request, or <see langword="null"/>.</summary>
    public static string? GetKey(HttpContext httpContext) =>
        Parse(httpContext.Request.Headers[WellKnownHeaders.IdempotencyKey], out var key) == KeyState.Valid ? key : null;

    /// <summary>
    /// Returns <see langword="null"/> when the request carries a valid key; otherwise logs the rejection (never the
    /// value) and returns the 400 response to send.
    /// </summary>
    public static IResult? Check(HttpContext httpContext)
    {
        var state = Parse(httpContext.Request.Headers[WellKnownHeaders.IdempotencyKey], out _);
        if (state == KeyState.Valid)
        {
            return null;
        }

        var code = state == KeyState.Missing
            ? PresentationErrorCodes.IdempotencyKeyRequired
            : PresentationErrorCodes.IdempotencyKeyInvalid;

        var logger = httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(IdempotencyKeyGuard))
            ?? NullLogger.Instance;
        Log.IdempotencyKeyRejected(logger, RequestFacts.GetEndpointDisplayName(httpContext), code);

        return new PresentationProblemResult(
            StatusCodes.Status400BadRequest,
            code,
            state == KeyState.Missing ? RequiredMessage : InvalidMessage);
    }

    // The IETF Idempotency-Key draft sends the key as a structured-field string, in double quotes; plain keys are
    // accepted as well. One header value only: two keys for one request is ambiguous, so it is invalid.
    private static KeyState Parse(StringValues values, out string? key)
    {
        key = null;

        if (values.Count == 0 || (values.Count == 1 && string.IsNullOrEmpty(values[0])))
        {
            return KeyState.Missing;
        }

        if (values.Count > 1)
        {
            return KeyState.Invalid;
        }

        var candidate = values[0]!;
        if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
        {
            candidate = candidate[1..^1];
        }

        if (!IsValid(candidate))
        {
            return KeyState.Invalid;
        }

        key = candidate;
        return KeyState.Valid;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is 1 to <see cref="IdempotencyKey.MaxLength"/> visible
    /// ASCII characters (0x21–0x7E).
    /// </summary>
    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= IdempotencyKey.MaxLength } && !key.AsSpan().ContainsAnyExceptInRange('!', '~');

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 3,
            Level = LogLevel.Warning,
            Message = "Rejected a request to {EndpointDisplayName} without a valid idempotency key ({ErrorCode}).")]
        public static partial void IdempotencyKeyRejected(ILogger logger, string endpointDisplayName, string errorCode);
    }
}
