using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Exceptions;

public sealed class SharedKernelExceptionHasErrorTests
{
    [Fact]
    public void SharedKernelException_ImplementsIHasError_ExposingTheSameError()
    {
        var error = Error.NotFound("order.not_found", "The order was not found.");
        Exception exception = new NotFoundException(error);

        var hasError = Assert.IsAssignableFrom<IHasError>(exception);

        Assert.Same(error, hasError.Error);
    }

    [Fact]
    public void EveryBaseExceptionType_ImplementsIHasError()
    {
        var error = Error.Unexpected("x.y", "m");

        Assert.IsAssignableFrom<IHasError>(new ConflictException(error));
        Assert.IsAssignableFrom<IHasError>(new DomainException(error));
        Assert.IsAssignableFrom<IHasError>(new ForbiddenException(error));
        Assert.IsAssignableFrom<IHasError>(new UnauthorizedException(error));
        Assert.IsAssignableFrom<IHasError>(new ValidationException(error));
    }
}
