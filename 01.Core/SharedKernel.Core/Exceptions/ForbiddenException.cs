using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when an authenticated caller is not permitted to perform the requested operation.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Forbidden"/> error (HTTP 403). Use <see cref="UnauthorizedException"/> only when the caller is not authenticated at all; mixing the two makes an authorization failure look like a missing credential.
/// </remarks>
/// <example>
/// <code>
/// throw new ForbiddenException(Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, "Only the account owner can close the account."));
/// </code>
/// </example>
public sealed class ForbiddenException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ForbiddenException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ForbiddenException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="ForbiddenException"/> carrying <paramref name="error"/> and the exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ForbiddenException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
