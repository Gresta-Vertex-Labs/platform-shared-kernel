using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication;

/// <summary>
/// Thrown from a client's handler pipeline when the client could not get the access token it authenticates with; the
/// request was not sent. The result helpers of the REST and gRPC packages turn it into <see cref="Error"/>
/// (<see cref="CommunicationErrorCodes.AccessTokenUnavailable"/>).
/// </summary>
/// <remarks>
/// It is a <see cref="SharedKernelException"/> carrying an <see cref="ErrorType.Unavailable"/> error, so an inbound
/// edge that lets it escape (a typed client used through its raw <see cref="HttpClient"/>) answers 503 / Unavailable.
/// It is not an <see cref="HttpRequestException"/>; the REST resilience pipeline still retries, breaks and hedges on it
/// through an explicit predicate. The exception message is <see cref="SharedKernelException.Error"/>'s message, which
/// never carries a URL, a host, an OAuth client id or text from the remote side.
/// </remarks>
public sealed class AccessTokenUnavailableException : SharedKernelException
{
    /// <summary>Initializes a new instance of the <see cref="AccessTokenUnavailableException"/> class.</summary>
    /// <param name="error">Why there is no token.</param>
    public AccessTokenUnavailableException(Error error)
        : base(error)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AccessTokenUnavailableException"/> class.</summary>
    /// <param name="error">Why there is no token.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public AccessTokenUnavailableException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AccessTokenUnavailableException"/> class.</summary>
    /// <param name="clientName">The client that has no token.</param>
    /// <param name="error">Why there is no token.</param>
    public AccessTokenUnavailableException(string clientName, Error error)
        : base(error)
    {
        ClientName = clientName;
    }

    /// <summary>Gets the client that has no token; <see langword="null"/> when it was not recorded.</summary>
    public string? ClientName { get; }
}
