using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext D-04/T-11: the <c>traceId</c> of every problem is read through Core's
/// <c>RequestFacts.GetTraceId</c>: the W3C id of the current activity, else the request's own identifier. The value is
/// what HTTP has always sent, so no client sees a difference.
/// </summary>
public sealed class TraceIdSourceTests
{
    [Fact]
    public void Problem_CarriesTheCurrentActivitysId_WhenThereIsOne()
    {
        using var source = new ActivitySource("Test.TraceIdSource." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        using var activity = source.StartActivity("request");
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };
        var problem = new ProblemDetails { Status = 404 };

        ProblemDetailsCustomizer.Apply(context, problem);

        activity.Should().NotBeNull();
        problem.Extensions[ProblemDetailsExtensionNames.TraceId].Should().Be(activity!.Id);
    }

    [Fact]
    public void Problem_CarriesTheRequestsIdentifier_WithoutAnActivity()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };
        var problem = new ProblemDetails { Status = 404 };

        ProblemDetailsCustomizer.Apply(context, problem);

        problem.Extensions[ProblemDetailsExtensionNames.TraceId].Should().Be("kestrel-id");
    }

    [Fact]
    public void TheValue_IsExactlyWhatItWasBeforeTheHelper()
    {
        using var source = new ActivitySource("Test.TraceIdSource." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };

        Activity.Current = null;
        RequestFacts.GetTraceId(context).Should().Be(Activity.Current?.Id ?? context.TraceIdentifier);

        using var activity = source.StartActivity("request");
        RequestFacts.GetTraceId(context).Should().Be(Activity.Current?.Id ?? context.TraceIdentifier);
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
