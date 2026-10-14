using System.Diagnostics;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext D-04/T-11: the <c>traceId</c> of the trailer is Core's <c>RequestFacts.GetTraceId</c> for the
/// invocation: the W3C id of the current activity, else the connection request's own identifier. The correlation id is
/// the connection's.
/// </summary>
public sealed class TraceIdSourceTests
{
    [Fact]
    public void Trailer_CarriesTheInvocationsActivityId_WhenThereIsOne()
    {
        using var source = new ActivitySource("Test.TraceIdSource." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        using var activity = source.StartActivity("invocation");
        var context = new FakeHubCallerContext(new DefaultHttpContext { TraceIdentifier = "kestrel-id" });

        var text = HubErrorMessage.Format("x.y", "message", context);

        activity.Should().NotBeNull();
        HubErrorMessage.TryParse(text, out _, out _, out var traceId, out _).Should().BeTrue();
        traceId.Should().Be(activity!.Id);
    }

    [Fact]
    public void Trailer_CarriesTheConnectionRequestsIdentifier_WithoutAnActivity()
    {
        Activity.Current = null;
        var context = new FakeHubCallerContext(new DefaultHttpContext { TraceIdentifier = "kestrel-id" });

        var text = HubErrorMessage.Format("x.y", "message", context);

        HubErrorMessage.TryParse(text, out _, out _, out var traceId, out var correlationId).Should().BeTrue();
        traceId.Should().Be("kestrel-id");
        correlationId.Should().BeNull("no request context scope is open");
    }

    [Fact]
    public void Trailer_CarriesTheCorrelationIdOfTheOpenScope()
    {
        Activity.Current = null;
        var context = new FakeHubCallerContext(new DefaultHttpContext { TraceIdentifier = "kestrel-id" });

        using (RequestContextScope.Begin(new SystemRequestContext([], correlationId: "flow-1")))
        {
            var text = HubErrorMessage.Format("x.y", "message", context);

            HubErrorMessage.TryParse(text, out _, out _, out _, out var correlationId).Should().BeTrue();
            correlationId.Should().Be("flow-1");
        }
    }

    [Fact]
    public void NoTrailer_WithNeitherAnActivityNorARequest()
    {
        Activity.Current = null;
        var context = new FakeHubCallerContext(httpContext: null);

        HubErrorMessage.Format("x.y", "message", context).Should().Be("x.y: message");
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

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly FeatureCollection _features = new();

        public FakeHubCallerContext(HttpContext? httpContext)
        {
            if (httpContext is not null)
            {
                _features.Set<IHttpContextFeature>(new HttpContextFeature(httpContext));
            }
        }

        public override string ConnectionId => "connection-1";

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features => _features;

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    private sealed class HttpContextFeature(HttpContext httpContext) : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}
