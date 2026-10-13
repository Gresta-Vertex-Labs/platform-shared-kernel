using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Errors;

/// <summary>
/// SK.14.PresentationConformance D-02: <c>GrpcStatusCodeMap</c> keeps its inverse beside it, and the redaction category
/// of a status is exactly the HTTP side's "server error" for the type that maps to it.
/// </summary>
public sealed class GrpcStatusCodeMapConsistencyTests
{
    [Fact]
    public void EveryErrorType_IsAServerError_ExactlyWhenItsStatusIsOfTheServerCategory()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            var inServerCategory = GrpcStatusCodeMap.ServerCategory(GrpcStatusCodeMap.Resolve(type)) is not null;

            inServerCategory.Should().Be(ErrorPresentation.IsServerError(type), type.ToString());
        }
    }

    [Fact]
    public void EveryStatusOfTheServerCategory_IsPresentedAsAServerErrorType()
    {
        foreach (var status in Enum.GetValues<StatusCode>())
        {
            if (GrpcStatusCodeMap.ServerCategory(status) is { } type)
            {
                ErrorPresentation.IsServerError(type).Should().BeTrue(status.ToString());
                GrpcStatusCodeMap.Resolve(type).Should().Be(
                    status is StatusCode.Unknown or StatusCode.DataLoss ? StatusCode.Internal : status,
                    "the inverse names the type whose own status is this one, Unknown and DataLoss presented as Internal");
            }
        }
    }

    [Theory]
    [InlineData(StatusCode.OK)]
    [InlineData(StatusCode.Cancelled)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.OutOfRange)]
    [InlineData(StatusCode.Unimplemented)]
    [InlineData(StatusCode.Unauthenticated)]
    public void AClientCategoryStatus_HasNoServerType(StatusCode status) =>
        GrpcStatusCodeMap.ServerCategory(status).Should().BeNull();
}
