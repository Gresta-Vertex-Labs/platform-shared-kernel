using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Errors;

/// <summary>
/// SK.14.PresentationConformance D-02: the generic errors every protocol answers with exist once, and the tables that
/// decide "server error" cannot drift apart.
/// </summary>
public sealed class EdgeErrorsTests
{
    [Fact]
    public void TheGenericErrors_AreTheDefaultCodes_OfTheirTypes()
    {
        EdgeErrors.Unexpected.Should().Be(Error.Unexpected(ErrorCodes.Unexpected.Default, "An unexpected error occurred."));
        EdgeErrors.Timeout.Should().Be(Error.Timeout(ErrorCodes.Timeout.Default, "The operation did not complete in time."));
        EdgeErrors.Unavailable.Should().Be(Error.Unavailable(ErrorCodes.Unavailable.Default, "The service is temporarily unavailable. Try again later."));
        EdgeErrors.Unexpected.Code.Should().Be("unexpected.default");
    }

    [Fact]
    public void EveryErrorType_ThatIsAServerError_HasAGenericErrorOfAServerType_AndNoOtherDoes()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            var status = ErrorTypeStatusCodeMap.Resolve(type);

            ErrorPresentation.IsServerError(type).Should().Be(status >= StatusCodes.Status500InternalServerError, type.ToString());

            if (ErrorPresentation.IsServerError(type))
            {
                ErrorPresentation.IsServerError(EdgeErrors.GenericFor(type).Type).Should().BeTrue(type.ToString());
            }
        }
    }

    [Theory]
    [InlineData(ErrorType.Unavailable, "unavailable.default")]
    [InlineData(ErrorType.Timeout, "timeout.default")]
    [InlineData(ErrorType.Unexpected, "unexpected.default")]
    [InlineData(ErrorType.None, "unexpected.default")]
    public void AServerErrorOutsideDevelopment_IsShownAsTheGenericErrorOfItsCategory(ErrorType type, string code)
    {
        var error = new Error("search.engine_fault", "Host db-7.internal refused the connection.", type);

        EdgeErrors.GenericFor(type).Code.Should().Be(code);
        ErrorPresentation.GetClientMessage(error, httpContext: null).Should().Be(EdgeErrors.GenericFor(type).Message);
    }
}
