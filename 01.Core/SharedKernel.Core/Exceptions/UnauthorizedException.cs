using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when the caller is not authenticated, or its credentials are missing, invalid, or expired.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Unauthorized"/> error (HTTP 401). When the caller is authenticated but not permitted, use <see cref="ForbiddenException"/> instead. The HTTP status is still decided by <see cref="Error.Type"/>; see <see cref="SharedKernelException"/>.
/// </remarks>
public sealed class UnauthorizedException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="UnauthorizedException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public UnauthorizedException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="UnauthorizedException"/> carrying <paramref name="error"/> and wrapping <paramref name="innerException"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public UnauthorizedException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
