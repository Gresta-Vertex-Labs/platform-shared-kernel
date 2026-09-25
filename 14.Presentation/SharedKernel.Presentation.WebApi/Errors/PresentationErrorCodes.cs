using System.Globalization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The error codes this package itself produces at the HTTP boundary, for failures that happen before any
/// application code runs.
/// </summary>
/// <remarks>
/// Every other code comes from the <see cref="SharedKernel.Primitives.Errors.Error"/> the application returned, or
/// from <see cref="SharedKernel.Primitives.Errors.ErrorCodes"/>. Like those, these values are a contract: clients
/// branch on them and translations are keyed by them.
/// </remarks>
public static class PresentationErrorCodes
{
    /// <summary><c>request.too_large</c>: the request body is larger than the endpoint accepts (413).</summary>
    public const string RequestTooLarge = "request.too_large";

    /// <summary><c>idempotency.key_required</c>: the endpoint requires an <c>Idempotency-Key</c> header and none was sent (400).</summary>
    public const string IdempotencyKeyRequired = ErrorCodes.Idempotency.KeyRequired;

    /// <summary><c>idempotency.key_invalid</c>: the <c>Idempotency-Key</c> header is not 1 to 256 visible ASCII characters (400).</summary>
    public const string IdempotencyKeyInvalid = ErrorCodes.Idempotency.KeyInvalid;

    /// <summary>
    /// <c>precondition.required</c>: the endpoint requires an <c>If-Match</c> header naming the version it changes, and
    /// none was sent, or only <c>*</c> (428).
    /// </summary>
    public const string PreconditionRequired = "precondition.required";

    /// <summary>
    /// <c>precondition.invalid</c>: the <c>If-Match</c> header of an endpoint that requires it is malformed or names
    /// more than one entity tag (400).
    /// </summary>
    public const string PreconditionInvalid = "precondition.invalid";

    /// <summary>
    /// <c>precondition.failed</c>: the <c>If-Match</c> entity tag can never match the current version — it is weak
    /// (<c>If-Match</c> compares strongly, RFC 9110 section 13.1.1) or not a version this endpoint uses (412).
    /// </summary>
    /// <remarks>
    /// A version that is well formed but no longer current is reported with the application's own conflict code,
    /// also as 412 (see <c>Problems:PreconditionFailedErrorCodes</c>).
    /// </remarks>
    public const string PreconditionFailed = "precondition.failed";

    /// <summary>
    /// <c>validation.invalid_value</c>: MVC model validation refused a value — a validation attribute such as
    /// <c>[Required]</c> or a model-binding rule — and the message is that attribute's or the framework's (400, one
    /// entry per field in <c>errors</c>). A value that could not be read at all is
    /// <see cref="Primitives.Errors.ErrorCodes.Validation.InvalidFormat"/> instead.
    /// </summary>
    public const string InvalidValue = "validation.invalid_value";

    /// <summary>
    /// <c>forbidden.origin_not_allowed</c>: a WebSocket request came from a browser origin the CORS policy does not
    /// allow (403). Browsers apply no CORS to WebSockets, so the server checks the origin itself.
    /// </summary>
    public const string OriginNotAllowed = "forbidden.origin_not_allowed";

    /// <summary><c>rate_limit.exceeded</c>: the caller sent too many requests (429).</summary>
    public const string RateLimitExceeded = "rate_limit.exceeded";

    /// <summary>
    /// <c>unauthorized.step_up_required</c>: the caller is signed in, but the endpoint requires a more recent sign-in
    /// or a stronger authentication method (401 with an RFC 9470 challenge).
    /// </summary>
    public const string StepUpRequired = "unauthorized.step_up_required";

    /// <summary>The prefix of <see cref="ForStatus"/> codes.</summary>
    private const string HttpStatusPrefix = "http.";

    /// <summary>
    /// Returns the code for an error response the framework produced without an error, such as <c>http.404</c> for
    /// an unmatched route: <c>http.</c> followed by the status code.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns><c>http.{statusCode}</c>.</returns>
    /// <remarks><c>SharedKernel.Communication.Rest</c> produces the same code for a response without a body.</remarks>
    internal static string ForStatus(int statusCode) =>
        HttpStatusPrefix + statusCode.ToString(CultureInfo.InvariantCulture);
}
