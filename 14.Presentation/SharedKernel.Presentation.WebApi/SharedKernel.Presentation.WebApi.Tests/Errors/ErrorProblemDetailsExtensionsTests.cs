using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

public class ErrorProblemDetailsExtensionsTests
{
    [Fact]
    public void ToProblemDetails_MapsTitleDetailAndStatus()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");

        var problemDetails = error.ToProblemDetails();

        problemDetails.Title.Should().Be(error.Code);
        problemDetails.Detail.Should().Be(error.Message);
        problemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
        problemDetails.Type.Should().Be("https://httpstatuses.io/404");
    }

    [Fact]
    public void ToProblemDetails_PopulatesErrorCodeExtension()
    {
        var error = Error.Validation("field.required", "Field is required.");

        var problemDetails = error.ToProblemDetails();

        problemDetails.Extensions["errorCode"].Should().Be(error.Code);
    }

    [Fact]
    public void ToProblemDetails_WithActiveActivity_PopulatesTraceIdFromActivity()
    {
        using var activity = new Activity("test-activity").Start();

        var error = Error.Conflict("order.conflict", "Order already exists.");

        var problemDetails = error.ToProblemDetails();

        problemDetails.Extensions["traceId"].Should().Be(Activity.Current!.Id);

        activity.Stop();
    }

    [Fact]
    public void ToProblemDetails_NoActivity_FallsBackToHttpContextTraceIdentifier()
    {
        var error = Error.Unexpected("system.failure", "Something went wrong.");
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-123" };

        var problemDetails = error.ToProblemDetails(httpContext);

        problemDetails.Extensions["traceId"].Should().Be("trace-123");
    }

    [Fact]
    public void ToProblemDetails_NoActivityAndNoContext_TraceIdIsNull()
    {
        var error = Error.Unexpected("system.failure", "Something went wrong.");

        var problemDetails = error.ToProblemDetails();

        problemDetails.Extensions["traceId"].Should().BeNull();
    }
}
