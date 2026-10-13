using System.Globalization;

namespace SharedKernel.Communication;

/// <summary>
/// The error codes of an outbound call that failed on the caller's side of the wire: the service could not be reached,
/// did not answer in time, or answered with a body that could not be read. Like every error code they are a contract —
/// callers branch on them and translations are keyed by them.
/// </summary>
/// <remarks>
/// A failure the called service reported keeps the service's own code (a ProblemDetails <c>errorCode</c>, a gRPC
/// <c>ErrorInfo</c> reason); one without a code gets <c>http.{status}</c> (<see cref="ForHttpStatus"/>, <c>http.503</c>)
/// or <c>grpc.{status}</c> (<see cref="GrpcStatus"/>, <c>grpc.unavailable</c>), the codes <c>14.Presentation</c> itself
/// uses. Those two families are a hand-kept mirror of <c>14.Presentation</c>'s names: a rename there must be mirrored here.
/// </remarks>
public static class CommunicationErrorCodes
{
    /// <summary>
    /// <c>communication.unavailable</c> (<c>Unavailable</c>): no response — the connection was refused or reset, the
    /// name did not resolve, or TLS failed.
    /// </summary>
    public const string Unavailable = "communication.unavailable";

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

    /// <summary>
    /// <c>communication.grpc.invalid_money</c> (<c>Validation</c>): a <c>google.type.Money</c> message whose <c>units</c>
    /// and <c>nanos</c> break the message's rules.
    /// </summary>
    public const string GrpcInvalidMoney = "communication.grpc.invalid_money";

    // The prefix of the code given to an HTTP response that carried no code of its own (http.404). Internal: a bare
    // prefix is not a code, and the public face of the family is ForHttpStatus.
    internal const string HttpStatusPrefix = "http.";

    /// <summary>
    /// The code of an HTTP response without a code of its own: <c>http.{status}</c>. The error's category comes from
    /// the status.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>The code, such as <c>http.503</c>.</returns>
    public static string ForHttpStatus(int statusCode) =>
        string.Create(CultureInfo.InvariantCulture, $"{HttpStatusPrefix}{statusCode}");

    /// <summary>
    /// The codes of a gRPC status that carried no <c>ErrorInfo</c> of its own: <c>grpc.{status}</c>, the names
    /// <c>14.Presentation</c>'s <c>GrpcErrorCodes.ForStatus</c> gives a status. The error's category comes from the status.
    /// </summary>
    public static class GrpcStatus
    {
        /// <summary><c>grpc.ok</c>.</summary>
        public const string Ok = "grpc.ok";

        /// <summary><c>grpc.cancelled</c>.</summary>
        public const string Cancelled = "grpc.cancelled";

        /// <summary><c>grpc.unknown</c>.</summary>
        public const string Unknown = "grpc.unknown";

        /// <summary><c>grpc.invalid_argument</c>.</summary>
        public const string InvalidArgument = "grpc.invalid_argument";

        /// <summary><c>grpc.deadline_exceeded</c>.</summary>
        public const string DeadlineExceeded = "grpc.deadline_exceeded";

        /// <summary><c>grpc.not_found</c>.</summary>
        public const string NotFound = "grpc.not_found";

        /// <summary><c>grpc.already_exists</c>.</summary>
        public const string AlreadyExists = "grpc.already_exists";

        /// <summary><c>grpc.permission_denied</c>.</summary>
        public const string PermissionDenied = "grpc.permission_denied";

        /// <summary><c>grpc.resource_exhausted</c>.</summary>
        public const string ResourceExhausted = "grpc.resource_exhausted";

        /// <summary><c>grpc.failed_precondition</c>.</summary>
        public const string FailedPrecondition = "grpc.failed_precondition";

        /// <summary><c>grpc.aborted</c>.</summary>
        public const string Aborted = "grpc.aborted";

        /// <summary><c>grpc.out_of_range</c>.</summary>
        public const string OutOfRange = "grpc.out_of_range";

        /// <summary><c>grpc.unimplemented</c>.</summary>
        public const string Unimplemented = "grpc.unimplemented";

        /// <summary><c>grpc.internal</c>.</summary>
        public const string Internal = "grpc.internal";

        /// <summary><c>grpc.unavailable</c>.</summary>
        public const string Unavailable = "grpc.unavailable";

        /// <summary><c>grpc.data_loss</c>.</summary>
        public const string DataLoss = "grpc.data_loss";

        /// <summary><c>grpc.unauthenticated</c>.</summary>
        public const string Unauthenticated = "grpc.unauthenticated";

        // The prefix of the code of a status number outside the 17 named ones (grpc.99). Internal: a bare prefix is not a code.
        internal const string NumberPrefix = "grpc.";
    }
}
