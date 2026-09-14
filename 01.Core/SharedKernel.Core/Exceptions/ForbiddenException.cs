using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when an authenticated caller is not permitted to perform the requested operation.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Forbidden"/> error (HTTP 403). When the caller is not authenticated at all, use <see cref="UnauthorizedException"/> instead. The HTTP status is still decided by <see cref="Error.Type"/>; see <see cref="SharedKernelException"/>.
/// </remarks>
public sealed class ForbiddenException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ForbiddenException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public ForbiddenException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="ForbiddenException"/> carrying <paramref name="error"/> and wrapping <paramref name="innerException"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public ForbiddenException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
