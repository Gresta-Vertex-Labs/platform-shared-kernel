using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using SharedKernel.Presentation.SignalR.Filters;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Filters;

/// <summary>
/// Tests for <see cref="HubInvocationRateLimitFilter"/> (WO-063, P-417).
/// </summary>
public class HubInvocationRateLimitFilterTests
{
    [Fact]
    public async Task InvokeMethodAsync_WithinPermitLimit_Succeeds()
    {
        var options = new HubInvocationRateLimitOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1) };
        var filter = new HubInvocationRateLimitFilter(options);
        var invocationContext = CreateInvocationContext();

        var firstResult = await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));
        var secondResult = await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        firstResult.Should().Be("ok");
        secondResult.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeMethodAsync_ExceedsPermitLimit_ThrowsHubExceptionWithSpecificMessage()
    {
        var options = new HubInvocationRateLimitOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1) };
        var filter = new HubInvocationRateLimitFilter(options);
        var invocationContext = CreateInvocationContext();

        await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));
        await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        var act = async () => await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Too many requests. Please slow down.");
    }

    [Fact]
    public async Task InvokeMethodAsync_TwoIndependentConnections_RateLimitAppliesPerConnectionOnly()
    {
        // T-61: a connection exceeding the configured invocation rate is throttled, while a second,
        // independent connection on the same hub is entirely unaffected.
        var options = new HubInvocationRateLimitOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) };
        var filter = new HubInvocationRateLimitFilter(options);
        var connectionAContext = CreateInvocationContext();
        var connectionBContext = CreateInvocationContext();

        // Exhaust connection A's single permit.
        await filter.InvokeMethodAsync(connectionAContext, _ => ValueTask.FromResult<object?>("ok"));
        var actOnA = async () => await filter.InvokeMethodAsync(connectionAContext, _ => ValueTask.FromResult<object?>("ok"));
        await actOnA.Should().ThrowAsync<HubException>();

        // Connection B has never been invoked before — its own independent permit must still be available.
        var resultOnB = await filter.InvokeMethodAsync(connectionBContext, _ => ValueTask.FromResult<object?>("ok"));

        resultOnB.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeMethodAsync_RateLimitRejection_ComposedWithHubExceptionMappingFilter_PreservesSpecificMessage()
    {
        // T-62 — Regression proving the D-64 composition-hazard fix: a rate-limit-rejected
        // invocation's HubException message must reach the client with its specific text intact,
        // never redacted to the generic "An unexpected error occurred." by
        // HubExceptionMappingFilter's outer catch-all.
        var rateLimitOptions = new HubInvocationRateLimitOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) };
        var rateLimitFilter = new HubInvocationRateLimitFilter(rateLimitOptions);
        var exceptionMappingFilter = new HubExceptionMappingFilter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HubExceptionMappingFilter>.Instance);
        var invocationContext = CreateInvocationContext();

        // Compose: HubExceptionMappingFilter (outer) wraps HubInvocationRateLimitFilter (inner)
        // wraps the real target method — mirroring AddSharedKernelSignalR's actual filter pipeline.
        Func<HubInvocationContext, ValueTask<object?>> pipeline = ctx =>
            exceptionMappingFilter.InvokeMethodAsync(
                ctx,
                innerCtx => rateLimitFilter.InvokeMethodAsync(innerCtx, _ => ValueTask.FromResult<object?>("ok")));

        await pipeline(invocationContext); // Consume the single permit.

        var act = async () => await pipeline(invocationContext);

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Too many requests. Please slow down.");
        exception.Which.Message.Should().NotBe("An unexpected error occurred.");
    }

    [Fact]
    public async Task InvokeMethodAsync_OversizedStringArgument_RejectedBeforeTargetMethodInvoked()
    {
        var options = new HubInvocationRateLimitOptions { MaxStringArgumentLength = 5 };
        var filter = new HubInvocationRateLimitFilter(options);
        var targetInvoked = false;
        var invocationContext = CreateInvocationContext(arguments: ["this string is too long"]);

        var act = async () => await filter.InvokeMethodAsync(invocationContext, _ =>
        {
            targetInvoked = true;
            return ValueTask.FromResult<object?>("ok");
        });

        await act.Should().ThrowAsync<HubException>();
        targetInvoked.Should().BeFalse("the target method body must never execute once argument validation rejects the invocation");
    }

    [Fact]
    public async Task InvokeMethodAsync_ArgumentWithinMaxLength_AllowsInvocation()
    {
        var options = new HubInvocationRateLimitOptions { MaxStringArgumentLength = 100 };
        var filter = new HubInvocationRateLimitFilter(options);
        var invocationContext = CreateInvocationContext(arguments: ["short"]);

        var result = await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeMethodAsync_CustomArgumentValidatorRejects_ThrowsHubExceptionWithValidatorMessage()
    {
        var options = new HubInvocationRateLimitOptions();
        options.ArgumentValidators.Add(_ => "custom rejection reason");
        var filter = new HubInvocationRateLimitFilter(options);
        var invocationContext = CreateInvocationContext();

        var act = async () => await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("custom rejection reason");
    }

    [Fact]
    public async Task InvokeMethodAsync_DefaultOptions_NeverConfigured_NoOpsRegardlessOfInvocationCount()
    {
        // T-63 (regression half): a host that does not configure configureRateLimit sees the filter
        // no-op — zero behavior change. Default HubInvocationRateLimitOptions has PermitLimit and
        // MaxStringArgumentLength both null and an empty ArgumentValidators collection.
        var filter = new HubInvocationRateLimitFilter();
        var invocationContext = CreateInvocationContext(arguments: [new string('x', 100_000)]);

        for (var i = 0; i < 20; i++)
        {
            var result = await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));
            result.Should().Be("ok");
        }
    }

    [Fact]
    public async Task OnDisconnectedAsync_DisposesPerConnectionRateLimiter_AndInvokesNext()
    {
        var options = new HubInvocationRateLimitOptions { PermitLimit = 1 };
        var filter = new HubInvocationRateLimitFilter(options);
        var invocationContext = CreateInvocationContext();
        // Create the per-connection rate limiter by invoking once.
        await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        var nextInvoked = false;
        var lifetimeContext = new HubLifetimeContext(invocationContext.Context, Substitute.For<IServiceProvider>(), Substitute.For<Hub>());

        await filter.OnDisconnectedAsync(lifetimeContext, exception: null, (_, _) =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
    }

    private static HubInvocationContext CreateInvocationContext(object[]? arguments = null)
    {
        var hubCallerContext = Substitute.For<HubCallerContext>();
        hubCallerContext.Items.Returns(new Dictionary<object, object?>());
        var hub = Substitute.For<Hub>();
        var methodInfo = typeof(HubInvocationRateLimitFilterTests).GetMethod(
            nameof(CreateInvocationContext),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return new HubInvocationContext(
            hubCallerContext,
            Substitute.For<IServiceProvider>(),
            hub,
            methodInfo,
            arguments ?? []);
    }
}
