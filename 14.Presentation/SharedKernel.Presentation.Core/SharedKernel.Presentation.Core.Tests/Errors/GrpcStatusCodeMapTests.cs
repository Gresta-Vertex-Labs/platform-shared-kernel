using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Errors;

public class GrpcStatusCodeMapTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCode.InvalidArgument)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Forbidden, StatusCode.PermissionDenied)]
    [InlineData(ErrorType.NotFound, StatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, StatusCode.Aborted)]
    [InlineData(ErrorType.BusinessRule, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.Unexpected, StatusCode.Internal)]
    public void Resolve_MapsEveryExplicitlyHandledErrorType(ErrorType errorType, StatusCode expected)
    {
        GrpcStatusCodeMap.Resolve(errorType).Should().Be(expected);
    }

    [Fact]
    public void Resolve_NoneFallsBackToUnknown()
    {
        GrpcStatusCodeMap.Resolve(ErrorType.None).Should().Be(StatusCode.Unknown);
    }

    [Fact]
    public void Resolve_UnmappedErrorTypeFallsBackToUnknown()
    {
        GrpcStatusCodeMap.Resolve((ErrorType)999).Should().Be(StatusCode.Unknown);
    }

    [Fact]
    public void Resolve_EveryDeclaredErrorTypeMemberIsExplicitlyHandled()
    {
        // Exhaustiveness proof mirroring ErrorTypeStatusCodeMapTests' shape: every member of
        // ErrorType except None (which has no dedicated business meaning) must resolve to a
        // status code other than the Unknown fallback, proving no member was silently forgotten.
        foreach (var errorType in Enum.GetValues<ErrorType>())
        {
            if (errorType == ErrorType.None)
            {
                continue;
            }

            GrpcStatusCodeMap.Resolve(errorType).Should().NotBe(StatusCode.Unknown, $"{errorType} should have an explicit mapping");
        }
    }
}
