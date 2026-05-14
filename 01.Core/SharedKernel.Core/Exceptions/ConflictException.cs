using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when an operation conflicts with existing state
/// (e.g., duplicate key, optimistic concurrency violation).
/// </summary>
/// <remarks>
/// Map this exception to an HTTP 409 Conflict at the presentation layer.
/// </remarks>
public sealed class ConflictException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="ConflictException"/> carrying the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The conflict error describing the violation.</param>
    public ConflictException(Error error)
        : base(error.Message, error)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="ConflictException"/> carrying the specified <paramref name="error"/>
    /// and wrapping an <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The conflict error describing the violation.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ConflictException(Error error, Exception innerException)
        : base(error.Message, error, innerException)
    {
    }
}
