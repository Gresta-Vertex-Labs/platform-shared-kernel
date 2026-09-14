using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Creates the <see cref="SharedKernelException"/> subclass that matches an <see cref="Error"/>.
/// </summary>
public static class ErrorExceptionExtensions
{
    /// <summary>
    /// Returns a new exception of the subclass that matches <paramref name="error"/>'s
    /// <see cref="Error.Type"/>, ready to throw.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    ///   <listheader><term><see cref="ErrorType"/></term><description>Exception</description></listheader>
    ///   <item><term><see cref="ErrorType.Validation"/></term><description><see cref="ValidationException"/></description></item>
    ///   <item><term><see cref="ErrorType.NotFound"/></term><description><see cref="NotFoundException"/></description></item>
    ///   <item><term><see cref="ErrorType.Conflict"/></term><description><see cref="ConflictException"/></description></item>
    ///   <item><term><see cref="ErrorType.Unauthorized"/></term><description><see cref="UnauthorizedException"/></description></item>
    ///   <item><term><see cref="ErrorType.Forbidden"/></term><description><see cref="ForbiddenException"/></description></item>
    ///   <item><term>Any other type</term><description><see cref="DomainException"/></description></item>
    /// </list>
    /// <see cref="ErrorType.BusinessRule"/> and <see cref="ErrorType.Unexpected"/> have no dedicated
    /// subclass and map to <see cref="DomainException"/>. The HTTP status still comes from the error.
    /// </remarks>
    /// <param name="error">The error to wrap.</param>
    /// <returns>An exception carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="error"/> is of type <see cref="ErrorType.None"/>, which is not a failure.</exception>
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
