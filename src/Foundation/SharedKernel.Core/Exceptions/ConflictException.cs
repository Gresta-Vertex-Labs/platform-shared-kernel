using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when an operation conflicts with the current state, such as a duplicate key or a lost optimistic-concurrency race.
/// </summary>
/// <remarks>
/// Pair it with an <see cref="ErrorType.Conflict"/> error (HTTP 409). Wrap the database or driver exception as the inner exception so the detail is kept for diagnostics but not returned to the client.
/// </remarks>
/// <example>
/// <code>
/// catch (DbUpdateConcurrencyException ex)
/// {
///     throw new ConflictException(
///         Error.Conflict(ErrorCodes.Conflict.Default, "The order was changed by someone else."), ex);
/// }
/// </code>
/// </example>
public sealed class ConflictException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ConflictException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ConflictException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="ConflictException"/> carrying <paramref name="error"/> and the exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ConflictException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
