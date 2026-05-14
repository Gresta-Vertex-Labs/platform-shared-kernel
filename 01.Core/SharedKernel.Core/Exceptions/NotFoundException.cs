using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when a requested resource cannot be found.
/// </summary>
/// <remarks>
/// Map this exception to an HTTP 404 Not Found at the presentation layer.
/// </remarks>
public sealed class NotFoundException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="NotFoundException"/> carrying the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The not-found error describing what could not be located.</param>
    public NotFoundException(Error error)
        : base(error.Message, error)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="NotFoundException"/> carrying the specified <paramref name="error"/>
    /// and wrapping an <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The not-found error describing what could not be located.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public NotFoundException(Error error, Exception innerException)
        : base(error.Message, error, innerException)
    {
    }
}
