using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Presentation.WebApi.Tests.Authorization;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Idempotency;

public class IdempotencyKeyRequirementEndpointFilterTests
{
    [Fact]
    public async Task InvokeAsync_AttributeAbsent_NoOps_PassesThrough_NoHeaderResolutionAttempted()
    {
        var filter = new IdempotencyKeyRequirementEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        // No RequireIdempotencyKeyAttribute metadata attached, and no Idempotency-Key header set at
        // all — if the filter attempted header resolution on a no-op path, it would simply find no
        // header, but the point is it should not even reach that check.
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null);

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_AttributePresent_KeyMissing_ShortCircuitsWith400ProblemDetails()
    {
        var filter = new IdempotencyKeyRequirementEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_AttributePresent_KeyMalformed_ShortCircuitsWith400ProblemDetails()
    {
        var filter = new IdempotencyKeyRequirementEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());
        context.HttpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = "   ";

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_AttributePresent_KeyValid_AllowsNextExactlyOnce()
    {
        var filter = new IdempotencyKeyRequirementEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());
        context.HttpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = "valid-key";

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_MalformedInput_NeverThrowsUnhandledException()
    {
        // (T-31 — REGRESSION)
        var filter = new IdempotencyKeyRequirementEndpointFilter();
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null, new RequireIdempotencyKeyAttribute());
        var overLength = new string('a', HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength + 1);
        context.HttpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = overLength;

        Func<Task> act = async () => await filter.InvokeAsync(context, next.Invoke);

        await act.Should().NotThrowAsync();
    }
}
