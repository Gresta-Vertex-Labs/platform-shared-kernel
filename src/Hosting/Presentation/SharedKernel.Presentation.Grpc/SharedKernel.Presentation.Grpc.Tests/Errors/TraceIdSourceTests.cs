using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext D-04/T-11: the <c>traceId</c> of a status's <c>ErrorInfo</c> is Core's
/// <c>RequestFacts.GetTraceId</c>: the W3C id of the current activity, else the request's own identifier, the same
/// value an HTTP problem carries.
/// </summary>
public sealed class TraceIdSourceTests
{
    private static readonly Error Failure = Error.NotFound("order.not_found", "Order 42 was not found.");

    [Fact]
    public void ErrorInfo_CarriesTheCurrentActivitysId_WhenThereIsOne()
    {
        using var source = new ActivitySource("Test.TraceIdSource." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        using var activity = source.StartActivity("call");
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };

        var errorInfo = RpcStatusFactory.Create(Failure, context, "orders").ErrorInfo();

        activity.Should().NotBeNull();
        errorInfo.Metadata[ProblemDetailsExtensionNames.TraceId].Should().Be(activity!.Id);
    }

    [Fact]
    public void ErrorInfo_CarriesTheRequestsIdentifier_WithoutAnActivity()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };

        var errorInfo = RpcStatusFactory.CreateForStatus(global::Grpc.Core.StatusCode.Unavailable, "down", context, "orders").ErrorInfo();

        errorInfo.Metadata[ProblemDetailsExtensionNames.TraceId].Should().Be("kestrel-id");
    }

    [Fact]
    public void ErrorInfo_HasNoTraceId_WithNeitherAnActivityNorARequest()
    {
        Activity.Current = null;

        var errorInfo = RpcStatusFactory.Create(Failure, httpContext: null, "orders").ErrorInfo();

        errorInfo.Metadata.Should().NotContainKey(ProblemDetailsExtensionNames.TraceId);
    }

    private static ActivityListener Listen(ActivitySource source)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
