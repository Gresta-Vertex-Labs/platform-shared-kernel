using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when the caller does not have permission to perform the requested operation.
/// </summary>
/// <remarks>
/// Map this exception to an HTTP 401 Unauthorized or HTTP 403 Forbidden at the presentation
/// layer depending on whether the caller is unauthenticated or authenticated-but-not-permitted.
/// </remarks>
public sealed class UnauthorizedException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="UnauthorizedException"/> carrying the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The authorization error describing the access denial.</param>
    public UnauthorizedException(Error error)
        : base(error.Message, error)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="UnauthorizedException"/> carrying the specified <paramref name="error"/>
    /// and wrapping an <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The authorization error describing the access denial.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public UnauthorizedException(Error error, Exception innerException)
        : base(error.Message, error, innerException)
    {
    }
}
