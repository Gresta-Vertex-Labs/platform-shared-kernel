using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when a domain rule or invariant would be violated.
/// </summary>
/// <remarks>
/// Throw it from entities and aggregates when an operation would leave the domain in an invalid state; <see cref="SharedKernel.Guards.Guard.Throw"/> throws it for guard violations. Pair it with an <see cref="ErrorType.BusinessRule"/> error (HTTP 422) or, for invalid input, <see cref="ErrorType.Validation"/> (HTTP 400). The class is not sealed, so a domain can derive a more specific exception.
/// </remarks>
/// <example>
/// <code>
/// throw new DomainException(Error.BusinessRule("order.already_shipped", "A shipped order cannot be cancelled."));
/// </code>
/// </example>
public class DomainException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="DomainException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public DomainException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="DomainException"/> carrying <paramref name="error"/> and the exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public DomainException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
