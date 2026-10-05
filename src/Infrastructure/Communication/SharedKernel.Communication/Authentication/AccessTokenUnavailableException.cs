using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication;

/// <summary>
/// Thrown from a client's handler pipeline when the client could not get the access token it authenticates with; the
/// request was not sent. The result helpers of the REST and gRPC packages turn it into <see cref="Error"/>
/// (<see cref="CommunicationErrorCodes.AccessTokenUnavailable"/>).
/// </summary>
/// <remarks>
/// It is an <see cref="HttpRequestException"/>, so code that already treats a failed request as the service being
/// unreachable — and gRPC, which reports it as <c>Unavailable</c> — handles it without a special case.
/// </remarks>
public sealed class AccessTokenUnavailableException : HttpRequestException
{
    /// <summary>Initializes a new instance of the <see cref="AccessTokenUnavailableException"/> class.</summary>
    /// <param name="clientName">The client that has no token.</param>
    /// <param name="error">Why there is no token.</param>
    public AccessTokenUnavailableException(string clientName, Error error)
        : base($"Client '{clientName}' has no access token: {error.Message}")
    {
        ClientName = clientName;
        Error = error;
    }

    /// <summary>Gets the client that has no token.</summary>
    public string ClientName { get; }

    /// <summary>Gets why there is no token.</summary>
    public Error Error { get; }
}
