using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when the caller is not authenticated: credentials are missing, invalid, or expired.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Unauthorized"/> error (HTTP 401), which tells the client to authenticate. When the caller is authenticated but not allowed to perform the operation, throw <see cref="ForbiddenException"/> instead; re-authenticating cannot fix that.
/// </remarks>
/// <example>
/// <code>
/// throw new UnauthorizedException(Error.Unauthorized(ErrorCodes.Unauthorized.Expired, "The access token has expired."));
/// </code>
/// </example>
public sealed class UnauthorizedException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="UnauthorizedException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public UnauthorizedException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="UnauthorizedException"/> carrying <paramref name="error"/> and the exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public UnauthorizedException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
