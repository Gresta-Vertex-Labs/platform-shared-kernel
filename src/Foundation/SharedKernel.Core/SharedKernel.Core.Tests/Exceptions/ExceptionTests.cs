using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Exceptions;

public sealed class ExceptionTests
{
    [Fact]
    public void DomainException_CarriesError()
    {
        var error = Error.Unexpected("d.1", "domain rule violated");
        var ex = new DomainException(error);
        Assert.Equal(error, ex.Error);
        Assert.Equal(error.Message, ex.Message);
    }

    [Fact]
    public void DomainException_IsSharedKernelException()
        => Assert.IsAssignableFrom<SharedKernelException>(new DomainException(Error.None));

    [Fact]
    public void NotFoundException_CarriesError()
    {
        var error = Error.NotFound("nf.1", "entity not found");
        var ex = new NotFoundException(error);
        Assert.Equal(error, ex.Error);
        Assert.Equal("entity not found", ex.Message);
    }

    [Fact]
    public void ConflictException_CarriesError()
    {
        var error = Error.Conflict("c.1", "duplicate key");
        var ex = new ConflictException(error);
        Assert.Equal(error, ex.Error);
    }

    [Fact]
    public void UnauthorizedException_CarriesError()
    {
        var error = Error.Unauthorized("a.1", "access denied");
        var ex = new UnauthorizedException(error);
        Assert.Equal(error, ex.Error);
    }

    [Fact]
    public void ValidationException_SingleError_MessageIsErrorMessage()
    {
        var errors = new[] { Error.Validation("v.1", "required field") };
        var ex = new ValidationException(errors);
        Assert.Equal("required field", ex.Message);
        Assert.Single(ex.Errors);
        Assert.Equal(ex.Errors[0], ex.Error);
    }

    [Fact]
    public void ValidationException_MultipleErrors_MessageContainsAll()
    {
        var errors = new[]
        {
            Error.Validation("v.1", "required"),
            Error.Validation("v.2", "too long"),
        };
        var ex = new ValidationException(errors);
        Assert.Contains("required", ex.Message);
        Assert.Contains("too long", ex.Message);
        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public void ValidationException_EmptyErrors_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ValidationException(Array.Empty<Error>()));
    }

    [Fact]
    public void DomainException_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("inner");
        var error = Error.Unexpected("d.2", "outer");
        var ex = new DomainException(error, inner);
        Assert.Equal(inner, ex.InnerException);
    }

    [Fact]
    public void AllExceptionTypes_DeriveFromSharedKernelException()
    {
        var error = Error.None;
        var errors = new[] { Error.Validation("x", "y") };

        Assert.IsAssignableFrom<SharedKernelException>(new DomainException(error));
        Assert.IsAssignableFrom<SharedKernelException>(new NotFoundException(error));
        Assert.IsAssignableFrom<SharedKernelException>(new ConflictException(error));
        Assert.IsAssignableFrom<SharedKernelException>(new UnauthorizedException(error));
        Assert.IsAssignableFrom<SharedKernelException>(new ValidationException(errors));
    }
}
