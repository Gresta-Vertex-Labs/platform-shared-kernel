using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when an operation conflicts with existing state, such as a duplicate key or an optimistic concurrency violation.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Conflict"/> error (HTTP 409). The HTTP status is still decided by <see cref="Error.Type"/>; see <see cref="SharedKernelException"/>.
/// </remarks>
public sealed class ConflictException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ConflictException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public ConflictException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="ConflictException"/> carrying <paramref name="error"/> and wrapping <paramref name="innerException"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public ConflictException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
