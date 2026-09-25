using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Converts an <see cref="Error"/> into the <see cref="SharedKernelException"/> subclass that matches its
/// <see cref="Error.Type"/>.
/// </summary>
public static class ErrorExceptionExtensions
{
    /// <summary>Creates the exception that matches <paramref name="error"/>'s type, ready to throw.</summary>
    /// <remarks>
    /// <list type="table">
    ///   <listheader><term><see cref="ErrorType"/></term><description>Exception created</description></listheader>
    ///   <item><term><see cref="ErrorType.Validation"/></term><description><see cref="ValidationException"/></description></item>
    ///   <item><term><see cref="ErrorType.NotFound"/></term><description><see cref="NotFoundException"/></description></item>
    ///   <item><term><see cref="ErrorType.Conflict"/></term><description><see cref="ConflictException"/></description></item>
    ///   <item><term><see cref="ErrorType.Unauthorized"/></term><description><see cref="UnauthorizedException"/></description></item>
    ///   <item><term><see cref="ErrorType.Forbidden"/></term><description><see cref="ForbiddenException"/></description></item>
    ///   <item><term><see cref="ErrorType.BusinessRule"/>, <see cref="ErrorType.Unexpected"/>, <see cref="ErrorType.Unavailable"/>, <see cref="ErrorType.Timeout"/>, and any future type</term><description><see cref="DomainException"/></description></item>
    /// </list>
    /// <para>
    /// No dedicated exception exists for <see cref="ErrorType.Unavailable"/> or
    /// <see cref="ErrorType.Timeout"/>. The <see cref="DomainException"/> carries the error
    /// unchanged, and a presentation layer maps an exception by its
    /// <see cref="SharedKernelException.Error"/>'s type rather than by its class, so the HTTP status
    /// stays 503 or 504.
    /// </para>
    /// </remarks>
    /// <param name="error">The error to wrap.</param>
    /// <returns>A new exception carrying <paramref name="error"/>. The method creates it; it does not throw it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="error"/> is of type <see cref="ErrorType.None"/>, which does not describe a failure.</exception>
    /// <example>
    /// <code>
    /// Result&lt;Order&gt; result = repository.Find(orderId);
    /// if (result.IsFailure)
    ///     throw result.Error.ToException();   // NotFoundException for a NotFound error
    /// </code>
    /// </example>
    public static SharedKernelException ToException(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Type switch
        {
            ErrorType.None => throw new ArgumentException("Error.None does not describe a failure.", nameof(error)),
            ErrorType.Validation => new ValidationException(error),
            ErrorType.NotFound => new NotFoundException(error),
            ErrorType.Conflict => new ConflictException(error),
            ErrorType.Unauthorized => new UnauthorizedException(error),
            ErrorType.Forbidden => new ForbiddenException(error),
            _ => new DomainException(error),
        };
    }
}
