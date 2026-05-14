using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Base class for all exceptions thrown by SharedKernel packages.
/// Every exception in this hierarchy carries a structured <see cref="Error"/> payload.
/// String-only constructors are not provided — callers must supply an <see cref="Error"/>.
/// </summary>
public abstract class SharedKernelException : Exception
{
    /// <summary>
    /// Initialises a new <see cref="SharedKernelException"/> with the specified
    /// <paramref name="message"/> and <paramref name="error"/>.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="error">The structured error that caused this exception.</param>
    protected SharedKernelException(string message, Error error)
        : base(message)
    {
        Error = error;
    }

    /// <summary>
    /// Initialises a new <see cref="SharedKernelException"/> with the specified
    /// <paramref name="message"/>, <paramref name="error"/>, and <paramref name="innerException"/>.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="error">The structured error that caused this exception.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    protected SharedKernelException(string message, Error error, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Gets the structured <see cref="Error"/> that caused this exception.</summary>
    public Error Error { get; }
}
