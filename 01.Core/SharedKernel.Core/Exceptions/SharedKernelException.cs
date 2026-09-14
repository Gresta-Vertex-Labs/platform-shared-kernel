using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Base class for every exception thrown by SharedKernel packages. Each one carries a structured
/// <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// String-only constructors are deliberately not provided: every exception must carry an
/// <see cref="Error"/>. To throw the subclass that matches an error's <see cref="ErrorType"/>, use
/// <see cref="ErrorExceptionExtensions.ToException(Error)"/>.
/// </para>
/// <para>
/// The HTTP status a presentation layer returns is decided by <see cref="Error"/>.<see cref="Error.Type"/>,
/// not by the exception subclass. The subclasses exist so callers can catch a specific kind of failure.
/// </para>
/// </remarks>
public abstract class SharedKernelException : Exception
{
    /// <summary>
    /// Initialises a new <see cref="SharedKernelException"/> whose message is
    /// <paramref name="error"/>'s <see cref="Error.Message"/>.
    /// </summary>
    /// <param name="error">The structured error that caused this exception.</param>
    /// <param name="innerException">The exception that caused this exception, if any.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    protected SharedKernelException(Error error, Exception? innerException = null)
        : base(MessageOf(error), innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Initialises a new <see cref="SharedKernelException"/> with an explicit <paramref name="message"/>.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="error">The structured error that caused this exception.</param>
    /// <param name="innerException">The exception that caused this exception, if any.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    protected SharedKernelException(string message, Error error, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>Gets the structured <see cref="Error"/> that caused this exception.</summary>
    public Error Error { get; }

    private static string MessageOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Message;
    }
}
