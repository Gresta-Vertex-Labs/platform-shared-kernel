using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

public sealed class ErrorTypeStatusCodeMapTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    [InlineData(ErrorType.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(ErrorType.Timeout, StatusCodes.Status504GatewayTimeout)]
    [InlineData(ErrorType.None, StatusCodes.Status500InternalServerError)]
    [InlineData((ErrorType)999, StatusCodes.Status500InternalServerError)]
    public void Resolve_MapsEveryType(ErrorType type, int expected)
    {
        ErrorTypeStatusCodeMap.Resolve(type).Should().Be(expected);
    }

    [Fact]
    public void Resolve_GivesEveryDeclaredTypeAnErrorStatus()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            ErrorTypeStatusCodeMap.Resolve(type).Should().BeInRange(400, 599, $"{type} must map to an error status");
        }
    }
}
