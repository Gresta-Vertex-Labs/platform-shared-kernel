using System.Diagnostics;
using FluentAssertions;
using Google.Rpc;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Errors;

/// <summary>The one status builder shared by the interceptor and the result extensions.</summary>
public sealed class RpcStatusFactoryTests
{
    [Fact]
    public void TraceId_IsTheCurrentActivity_AsInAnHttpProblem()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();

        var status = RpcStatusFactory.Create(Error.NotFound("order.not_found", "Not found."), new DefaultHttpContext(), "orders");

        status.ErrorInfo().Metadata[ProblemDetailsExtensionNames.TraceId].Should().Be(activity.Id);
    }

    [Fact]
    public void TraceId_WithoutAnActivity_IsTheRequestIdentifier()
    {
        Activity.Current = null;
        var httpContext = new DefaultHttpContext { TraceIdentifier = "0HN1:00000001" };

        var status = RpcStatusFactory.Create(Error.NotFound("order.not_found", "Not found."), httpContext, "orders");

        var metadata = status.ErrorInfo().Metadata;
        metadata[ProblemDetailsExtensionNames.TraceId].Should().Be("0HN1:00000001");
        metadata.Should().NotContainKey(ProblemDetailsExtensionNames.CorrelationId, "only the correlation middleware assigns one");
    }

    [Fact]
    public void FieldViolation_OfAnErrorNamingNoField_IsKeyedByItsCode()
    {
        var status = RpcStatusFactory.Create(
            Error.Validation([Error.Validation("order.lines_empty", "An order needs at least one line.")]),
            httpContext: null,
            "orders");

        status.FieldViolations().Should().Equal(("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    [Fact]
    public void ExplicitStatusCodeAndMessage_Win()
    {
        var status = RpcStatusFactory.Create(
            Error.Unexpected(ErrorCodes.Unexpected.Default, "An unexpected error occurred."),
            httpContext: null,
            "orders",
            statusCode: StatusCode.DataLoss,
            clientMessage: "details");

        status.Code.Should().Be((int)StatusCode.DataLoss);
        status.Message.Should().Be("details");
        status.GetDetail<BadRequest>().Should().BeNull();
    }

    [Fact]
    public void CreateException_CarriesTheStatusInItsTrailers()
    {
        var exception = RpcStatusFactory.CreateException(Error.Conflict("order.version_conflict", "Changed."), httpContext: null, "orders");

        var status = exception.ShouldHaveRichStatus(StatusCode.Aborted);
        status.ErrorInfo().Should().BeEquivalentTo(new { Reason = "order.version_conflict", Domain = "orders" });
    }
}
