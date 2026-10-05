namespace SharedKernel.Communication;

/// <summary>
/// The error codes of an outbound call that failed on the caller's side of the wire: the service could not be reached,
/// did not answer in time, or answered with a body that could not be read. Like every error code they are a contract —
/// callers branch on them and translations are keyed by them.
/// </summary>
/// <remarks>
/// A failure the called service reported keeps the service's own code (a ProblemDetails <c>errorCode</c>, a gRPC
/// <c>ErrorInfo</c> reason); one without a code gets <c>http.{status}</c> (<c>http.503</c>) or <c>grpc.{status}</c>
/// (<c>grpc.unavailable</c>), the codes <c>14.Presentation</c> itself uses.
/// </remarks>
public static class CommunicationErrorCodes
{
    /// <summary>
    /// <c>communication.unreachable</c> (<c>Unavailable</c>): no response — the connection was refused or reset, the
    /// name did not resolve, or TLS failed.
    /// </summary>
    public const string Unreachable = "communication.unreachable";

    /// <summary><c>communication.timeout</c> (<c>Timeout</c>): an attempt, or the whole call with its retries, ran out of time.</summary>
    public const string Timeout = "communication.timeout";

    /// <summary>
    /// <c>communication.circuit_open</c> (<c>Unavailable</c>): too many recent calls failed, so this one was refused
    /// without being sent, to let the service recover.
    /// </summary>
    public const string CircuitOpen = "communication.circuit_open";

    /// <summary>
    /// <c>communication.access_token_unavailable</c> (<c>Unavailable</c>): the client could not get the access token it
    /// authenticates with, so the call was not sent.
    /// </summary>
    public const string AccessTokenUnavailable = "communication.access_token_unavailable";

    /// <summary><c>communication.empty_body</c> (<c>Unexpected</c>): a success response that should carry a body had none.</summary>
    public const string EmptyBody = "communication.empty_body";

    /// <summary>
    /// <c>communication.invalid_body</c> (<c>Unexpected</c>): a success response body that is not valid JSON for the
    /// expected type.
    /// </summary>
    public const string InvalidBody = "communication.invalid_body";
}
