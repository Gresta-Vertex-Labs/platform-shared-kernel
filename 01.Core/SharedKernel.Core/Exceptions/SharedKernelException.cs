using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Base class for every SharedKernel exception. Each instance carries a structured <see cref="Error"/>, so an
/// exception and a failed <see cref="Result{T}"/> describe a failure the same way.
/// </summary>
/// <remarks>
/// <para>
/// <b>When to throw instead of returning a result.</b> Return a failed result for outcomes the caller is
/// expected to handle. Throw where a result cannot flow: constructors, invariant checks, or a boundary serving
/// callers that do not use results. <see cref="ErrorExceptionExtensions.ToException(Error)"/> and
/// <c>result.GetValueOrThrow()</c> bridge the two.
/// </para>
/// <para>
/// <b>HTTP status.</b> A presentation layer maps the exception through its <see cref="Error"/>'s
/// <see cref="Error.Type"/>, not through the exception class, so keep the subclass and the error type
/// consistent (<see cref="NotFoundException"/> with <see cref="ErrorType.NotFound"/>, and so on).
/// </para>
/// <para>
/// <b>Message.</b> <see cref="Exception.Message"/> is <see cref="Error.Message"/>, which a presentation layer
/// may return to the client. Keep secrets and raw exception text out of it; put diagnostic detail in the
/// inner exception.
/// </para>
/// <para>
/// String-only constructors are deliberately absent, and analyzer <c>SK0005</c> reports one on a subclass.
/// </para>
/// </remarks>
public abstract class SharedKernelException : Exception
{
    /// <summary>Initialises the exception with <paramref name="error"/>, using its message as the exception message.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    protected SharedKernelException(Error error, Exception? innerException = null)
        : base(MessageOf(error), innerException)
    {
        Error = error;
    }

    /// <summary>Initialises the exception with an explicit message, for a subclass that composes its own.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    protected SharedKernelException(string message, Error error, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>Gets the error describing the failure. Never <see langword="null"/>.</summary>
    public Error Error { get; }

    private static string MessageOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Message;
    }
}
