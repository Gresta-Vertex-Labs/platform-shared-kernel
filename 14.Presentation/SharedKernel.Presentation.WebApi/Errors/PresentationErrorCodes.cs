using System.Globalization;

namespace SharedKernel.Presentation.WebApi.Errors;

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
    public const string IdempotencyKeyRequired = "idempotency.key_required";

    /// <summary><c>idempotency.key_invalid</c>: the <c>Idempotency-Key</c> header is not 1 to 256 visible ASCII characters (400).</summary>
    public const string IdempotencyKeyInvalid = "idempotency.key_invalid";

    /// <summary><c>precondition.required</c>: the endpoint requires an <c>If-Match</c> header and none was sent (428).</summary>
    public const string PreconditionRequired = "precondition.required";

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
    public static string ForStatus(int statusCode) =>
        HttpStatusPrefix + statusCode.ToString(CultureInfo.InvariantCulture);
}
