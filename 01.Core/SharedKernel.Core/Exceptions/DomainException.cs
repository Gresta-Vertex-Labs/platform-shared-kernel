using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when a domain rule or invariant is violated.
/// </summary>
/// <remarks>
/// Throw it from entities and aggregates when an operation would leave the domain in an invalid state. <see cref="SharedKernel.Guards.Guard.Throw"/> throws it too. Pair it with an <see cref="ErrorType.BusinessRule"/> or <see cref="ErrorType.Validation"/> error. Not sealed, so a domain can derive its own. The HTTP status is still decided by <see cref="Error.Type"/>; see <see cref="SharedKernelException"/>.
/// </remarks>
public class DomainException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="DomainException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public DomainException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="DomainException"/> carrying <paramref name="error"/> and wrapping <paramref name="innerException"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public DomainException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
