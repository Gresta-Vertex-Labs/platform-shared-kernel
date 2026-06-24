using System.Diagnostics;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class AmbientActivityTestHelperTests
{
    [Fact]
    public void Start_SetsAmbientActivityWithGivenTraceId()
    {
        var traceId = ActivityTraceId.CreateRandom();

        using var scope = AmbientActivityTestHelper.Start(traceId);

        Assert.Equal(traceId, Activity.Current?.TraceId);
        Assert.Same(scope.Activity, Activity.Current);
    }

    [Fact]
    public void Dispose_RestoresPriorAmbientActivity()
    {
        var previousTraceId = ActivityTraceId.CreateRandom();
        using var outer = AmbientActivityTestHelper.Start(previousTraceId);

        var innerTraceId = ActivityTraceId.CreateRandom();
        var inner = AmbientActivityTestHelper.Start(innerTraceId);
        inner.Dispose();

        Assert.Equal(previousTraceId, Activity.Current?.TraceId);
    }

    [Fact]
    public void Start_WithParentSpanId_EstablishesParentChildRelationship()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var parentSpanId = ActivitySpanId.CreateRandom();

        using var scope = AmbientActivityTestHelper.Start(traceId, parentSpanId);

        Assert.Equal(parentSpanId, scope.Activity.ParentSpanId);
    }
}
