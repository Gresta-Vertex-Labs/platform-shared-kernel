using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.NotFound"/> error (HTTP 404). Prefer returning a failed result from a lookup; throw where the caller cannot handle absence.
/// </remarks>
/// <example>
/// <code>
/// throw new NotFoundException(Error.NotFound("order.not_found", $"Order {orderId} does not exist."));
/// </code>
/// </example>
public sealed class NotFoundException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="NotFoundException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public NotFoundException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="NotFoundException"/> carrying <paramref name="error"/> and the exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public NotFoundException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
