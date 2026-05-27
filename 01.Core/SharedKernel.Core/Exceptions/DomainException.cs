using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when a domain rule or invariant is violated.
/// </summary>
/// <remarks>
/// Raise this exception inside domain entities or aggregate roots when an operation
/// would leave the domain in an invalid state. Map it to an HTTP 422 Unprocessable Entity
/// (or equivalent) at the presentation layer.
/// </remarks>
public class DomainException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="DomainException"/> carrying the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The domain error that was violated.</param>
    public DomainException(Error error)
        : base(error.Message, error)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="DomainException"/> carrying the specified <paramref name="error"/>
    /// and wrapping an <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The domain error that was violated.</param>
    /// <param name="innerException">The exception that caused this domain exception.</param>
    public DomainException(Error error, Exception innerException)
        : base(error.Message, error, innerException)
    {
    }
}
