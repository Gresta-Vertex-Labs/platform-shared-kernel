using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Errors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Errors;

/// <summary>
/// P-562 R30/R31: the codes this package puts in a status itself are a contract — clients branch on them and
/// translations are keyed by them — so every value is pinned.
/// </summary>
public sealed class GrpcErrorCodesTests
{
    [Theory]
    [InlineData(StatusCode.OK, "grpc.ok")]
    [InlineData(StatusCode.Cancelled, "grpc.cancelled")]
    [InlineData(StatusCode.Unknown, "grpc.unknown")]
    [InlineData(StatusCode.InvalidArgument, "grpc.invalid_argument")]
    [InlineData(StatusCode.DeadlineExceeded, "grpc.deadline_exceeded")]
    [InlineData(StatusCode.NotFound, "grpc.not_found")]
    [InlineData(StatusCode.AlreadyExists, "grpc.already_exists")]
    [InlineData(StatusCode.PermissionDenied, "grpc.permission_denied")]
    [InlineData(StatusCode.ResourceExhausted, "grpc.resource_exhausted")]
    [InlineData(StatusCode.FailedPrecondition, "grpc.failed_precondition")]
    [InlineData(StatusCode.Aborted, "grpc.aborted")]
    [InlineData(StatusCode.OutOfRange, "grpc.out_of_range")]
    [InlineData(StatusCode.Unimplemented, "grpc.unimplemented")]
    [InlineData(StatusCode.Internal, "grpc.internal")]
    [InlineData(StatusCode.Unavailable, "grpc.unavailable")]
    [InlineData(StatusCode.DataLoss, "grpc.data_loss")]
    [InlineData(StatusCode.Unauthenticated, "grpc.unauthenticated")]
    public void ForStatus_IsTheCanonicalNameInLowerCase(StatusCode statusCode, string expected)
    {
        GrpcErrorCodes.ForStatus(statusCode).Should().Be(expected);
    }

    [Fact]
    public void ForStatus_OfAValueOutsideTheSpecification_IsItsNumber()
    {
        GrpcErrorCodes.ForStatus((StatusCode)42).Should().Be("grpc.42");
    }

    [Fact]
    public void ForStatus_CoversEveryDeclaredStatusCode_WithADistinctName()
    {
        var codes = Enum.GetValues<StatusCode>().Select(GrpcErrorCodes.ForStatus).ToArray();

        codes.Should().OnlyHaveUniqueItems().And.OnlyContain(code => code.StartsWith("grpc.", StringComparison.Ordinal) && !char.IsDigit(code[5]));
    }

    [Fact]
    public void MoreFieldViolations_IsPinned()
    {
        GrpcErrorCodes.MoreFieldViolations.Should().Be("grpc.more_field_violations");
    }
}
