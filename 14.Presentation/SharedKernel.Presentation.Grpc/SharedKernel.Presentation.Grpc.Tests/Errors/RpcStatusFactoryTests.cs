using System.Diagnostics;
using FluentAssertions;
using Google.Protobuf;
using Google.Rpc;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Errors;

/// <summary>The one status builder behind every error the interceptor maps.</summary>
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
    public void AnErrorWithoutFieldErrors_HasNoBadRequest_EvenAValidationError()
    {
        // As over HTTP, where the errors map comes from Error.Details only.
        var status = RpcStatusFactory.Create(Error.Validation("customer.name_required", "Name is required."), httpContext: null, "orders");

        status.GetDetail<BadRequest>().Should().BeNull();
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

    [Fact]
    public void R30_CreateForStatus_CarriesTheCodeTheMessage_AndOnlyThisServicesErrorInfo()
    {
        Activity.Current = null;
        var httpContext = new DefaultHttpContext { TraceIdentifier = "0HN1:00000002" };

        var exception = RpcStatusFactory.CreateExceptionForStatus(StatusCode.ResourceExhausted, "Quota exhausted.", httpContext, "orders");

        var status = exception.ShouldHaveRichStatus(StatusCode.ResourceExhausted);
        status.Message.Should().Be("Quota exhausted.");
        status.Details.Should().ContainSingle();
        var errorInfo = status.ErrorInfo();
        errorInfo.Reason.Should().Be("grpc.resource_exhausted");
        errorInfo.Domain.Should().Be("orders");
        errorInfo.Metadata.Should().Equal(new Dictionary<string, string> { [ProblemDetailsExtensionNames.TraceId] = "0HN1:00000002" });
        exception.Trailers.Select(entry => entry.Key).Should().Equal("grpc-status-details-bin");
    }

    [Fact]
    public void R31_FiftyFieldErrors_AreAllListed_WithoutASummary()
    {
        var status = RpcStatusFactory.Create(Error.Validation(ItemErrors(RpcStatusFactory.MaxFieldViolations)), httpContext: null, "orders");

        var violations = status.FieldViolations();
        violations.Should().HaveCount(RpcStatusFactory.MaxFieldViolations);
        violations.Should().NotContain(violation => violation.Reason == GrpcErrorCodes.MoreFieldViolations);
    }

    [Theory]
    [InlineData(51, 1)]
    [InlineData(400, 350)]
    [InlineData(3000, 2950)]
    public void R31_MoreThanFiftyFieldErrors_ListTheFirstFifty_AndSumUpTheRest(int count, int omitted)
    {
        var status = RpcStatusFactory.Create(Error.Validation(ItemErrors(count)), httpContext: null, "orders");

        var violations = status.FieldViolations();
        violations.Should().HaveCount(RpcStatusFactory.MaxFieldViolations + 1);
        violations.Take(RpcStatusFactory.MaxFieldViolations).Select(violation => violation.Field)
            .Should().Equal(Enumerable.Range(0, RpcStatusFactory.MaxFieldViolations).Select(index => $"items[{index}].name"));
        violations[^1].Should().Be((GrpcErrorCodes.MoreFieldViolations, $"… and {omitted} more.", GrpcErrorCodes.MoreFieldViolations));
    }

    [Fact]
    public void R31_LongFieldErrors_AreCappedByTheByteBudget_BeforeTheCount()
    {
        var message = new string('x', 500);
        var errors = Enumerable.Range(0, 100)
            .Select(index => Field(Error.Validation("item.note_invalid", message), $"items[{index}].note"))
            .ToArray();

        var status = RpcStatusFactory.Create(Error.Validation(errors), httpContext: null, "orders");

        var badRequest = status.GetDetail<BadRequest>()!;
        var listed = badRequest.FieldViolations.Take(badRequest.FieldViolations.Count - 1).ToArray();
        listed.Should().NotBeEmpty().And.HaveCountLessThan(RpcStatusFactory.MaxFieldViolations);
        listed.Sum(violation => 1 + CodedOutputStream.ComputeMessageSize(violation)).Should().BeLessThanOrEqualTo(RpcStatusFactory.MaxFieldViolationBytes);
        badRequest.FieldViolations[^1].Description.Should().Be($"… and {100 - listed.Length} more.");
        status.CalculateSize().Should().BeLessThan(4 * 1024, "base64 in a trailer, the status stays well under 8 KB");
    }

    [Fact]
    public void R31_AFieldErrorLargerThanTheWholeBudget_LeavesOnlyTheSummary()
    {
        var error = Error.Validation([Field(Error.Validation("item.note_invalid", new string('x', 4000)), "items[0].note")]);

        var status = RpcStatusFactory.Create(error, httpContext: null, "orders");

        status.FieldViolations().Should().Equal((GrpcErrorCodes.MoreFieldViolations, "… and 1 more.", GrpcErrorCodes.MoreFieldViolations));
    }

    private static Error[] ItemErrors(int count) =>
        [.. Enumerable.Range(0, count).Select(index => Field(Error.Validation("item.name_required", "Name is required."), $"items[{index}].name"))];

    private static Error Field(Error error, string path) =>
        error with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = path } };
}
