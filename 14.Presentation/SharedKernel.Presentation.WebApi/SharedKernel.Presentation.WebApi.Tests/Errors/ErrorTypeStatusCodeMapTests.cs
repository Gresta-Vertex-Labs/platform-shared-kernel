using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

public class ErrorTypeStatusCodeMapTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    public void Resolve_KnownErrorType_ReturnsMappedStatusCode(ErrorType type, int expectedStatusCode)
    {
        var statusCode = ErrorTypeStatusCodeMap.Resolve(type);

        statusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void Resolve_NoneErrorType_FallsBackTo500()
    {
        var statusCode = ErrorTypeStatusCodeMap.Resolve(ErrorType.None);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void Resolve_UnmappedErrorType_FallsBackTo500()
    {
        var unmappedType = (ErrorType)999;

        var statusCode = ErrorTypeStatusCodeMap.Resolve(unmappedType);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
}
