using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Http;

/// <summary>
/// P-579: the correlation id every presentation package reports (problem bodies, gRPC <c>ErrorInfo</c>, hub errors) is
/// the one held by the call's <see cref="RequestContextScope"/>, which <c>UseSharedKernelRequestContext()</c> opens.
/// </summary>
public sealed class RequestFactsTests
{
    [Fact]
    public void GetCorrelationId_ReadsTheAmbientScope()
    {
        using (RequestContextScope.Begin(new SystemRequestContext([], correlationId: "flow-1")))
        {
            RequestFacts.GetCorrelationId(new DefaultHttpContext()).Should().Be("flow-1");
            RequestFacts.GetCorrelationId(null).Should().Be("flow-1");
        }
    }

    [Fact]
    public void GetCorrelationId_AsksTheRegisteredAccessorFirst()
    {
        var services = new ServiceCollection()
            .AddSingleton<IRequestContextAccessor>(new FixedAccessor(new SystemRequestContext([], correlationId: "flow-2")))
            .BuildServiceProvider();

        RequestFacts.GetCorrelationId(new DefaultHttpContext { RequestServices = services }).Should().Be("flow-2");
    }

    [Fact]
    public void GetCorrelationId_IsNull_OutsideAnyScope()
    {
        RequestFacts.GetCorrelationId(new DefaultHttpContext()).Should().BeNull();
    }

    [Fact]
    public void GetTraceId_IsTheCurrentActivitysW3CId_WhenThereIsOne()
    {
        using var source = new System.Diagnostics.ActivitySource("Test.RequestFacts." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        using var activity = source.StartActivity("request");
        var context = new DefaultHttpContext { TraceIdentifier = "kestrel-id" };

        activity.Should().NotBeNull();
        RequestFacts.GetTraceId(context).Should().Be(activity!.Id).And.StartWith("00-");
        RequestFacts.GetTraceId(null).Should().Be(activity.Id);
    }

    [Fact]
    public void GetTraceId_IsTheRequestsOwnIdentifier_WithoutAnActivity()
    {
        System.Diagnostics.Activity.Current = null;

        RequestFacts.GetTraceId(new DefaultHttpContext { TraceIdentifier = "kestrel-id" }).Should().Be("kestrel-id");
    }

    [Fact]
    public void GetTraceId_IsNull_WithNeitherAnActivityNorARequest()
    {
        System.Diagnostics.Activity.Current = null;

        RequestFacts.GetTraceId(null).Should().BeNull();
    }

    private static System.Diagnostics.ActivityListener Listen(System.Diagnostics.ActivitySource source)
    {
        var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Theory]
    [InlineData("application/grpc", true)]
    [InlineData("application/grpc+proto", true)]
    [InlineData("application/json", false)]
    [InlineData(null, false)]
    public void IsGrpcRequest_ReadsTheContentType(string? contentType, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;

        RequestFacts.IsGrpcRequest(context).Should().Be(expected);
    }

    private sealed class FixedAccessor(IRequestContext context) : IRequestContextAccessor
    {
        public IRequestContext? Current => context;
    }
}
