using FluentAssertions;
using SharedKernel.Presentation.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Errors;

public class ErrorTypeStatusCodeMapTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.BusinessRule, 422)]
    [InlineData(ErrorType.Unexpected, 500)]
    public void Resolve_KnownErrorType_ReturnsMappedStatusCode(ErrorType type, int expectedStatusCode)
    {
        var statusCode = ErrorTypeStatusCodeMap.Resolve(type);

        statusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void Resolve_NoneErrorType_FallsBackTo500()
    {
        var statusCode = ErrorTypeStatusCodeMap.Resolve(ErrorType.None);

        statusCode.Should().Be(500);
    }

    [Fact]
    public void Resolve_UnmappedErrorType_FallsBackTo500()
    {
        var unmappedType = (ErrorType)999;

        var statusCode = ErrorTypeStatusCodeMap.Resolve(unmappedType);

        statusCode.Should().Be(500);
    }
}
