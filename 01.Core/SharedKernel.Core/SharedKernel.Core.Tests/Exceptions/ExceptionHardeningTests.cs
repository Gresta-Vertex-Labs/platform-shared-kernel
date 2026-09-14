using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Exceptions;

/// <summary>Null safety, <see cref="ForbiddenException"/>, and <see cref="ErrorExceptionExtensions.ToException"/>.</summary>
public sealed class ExceptionHardeningTests
{
    [Fact]
    public void Constructors_NullError_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DomainException(null!));
        Assert.Throws<ArgumentNullException>(() => new NotFoundException(null!));
        Assert.Throws<ArgumentNullException>(() => new ConflictException(null!, new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => new UnauthorizedException(null!));
        Assert.Throws<ArgumentNullException>(() => new ForbiddenException(null!));
        Assert.Throws<ArgumentNullException>(() => new ValidationException((Error)null!));
    }

    [Fact]
    public void ForbiddenException_CarriesErrorAndInnerException()
    {
        var error = Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, "Not permitted.");
        var inner = new InvalidOperationException();

        var exception = new ForbiddenException(error, inner);

        Assert.Same(error, exception.Error);
        Assert.Equal("Not permitted.", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void ValidationException_SingleError_ExposesItAsTheOnlyEntry()
    {
        var error = Error.Validation("v", "bad");

        var exception = new ValidationException(error);

        Assert.Same(error, exception.Error);
        Assert.Equal([error], exception.Errors);
    }

    [Fact]
    public void ValidationException_CopiesTheList()
    {
        var errors = new List<Error> { Error.Validation("a", "A") };
        var exception = new ValidationException(errors);

        errors.Add(Error.Validation("b", "B"));

        Assert.Single(exception.Errors);
    }

    [Fact]
    public void ValidationException_NullEntry_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => new ValidationException([Error.Validation("a", "A"), null!]));

    [Fact]
    public void ValidationException_Empty_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => new ValidationException(Array.Empty<Error>()));

    [Theory]
    [InlineData(ErrorType.Validation, typeof(ValidationException))]
    [InlineData(ErrorType.NotFound, typeof(NotFoundException))]
    [InlineData(ErrorType.Conflict, typeof(ConflictException))]
    [InlineData(ErrorType.Unauthorized, typeof(UnauthorizedException))]
    [InlineData(ErrorType.Forbidden, typeof(ForbiddenException))]
    [InlineData(ErrorType.BusinessRule, typeof(DomainException))]
    [InlineData(ErrorType.Unexpected, typeof(DomainException))]
    public void ToException_PicksSubclassFromErrorType(ErrorType type, Type expected)
    {
        var error = new Error("code", "message", type);

        var exception = error.ToException();

        Assert.IsType(expected, exception);
        Assert.Same(error, exception.Error);
    }

    [Fact]
    public void ToException_ErrorNone_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => Error.None.ToException());

    [Fact]
    public void ToException_CoversEveryErrorType()
    {
        foreach (var type in Enum.GetValues<ErrorType>().Where(t => t != ErrorType.None))
            Assert.NotNull(new Error("c", "m", type).ToException());
    }
}
