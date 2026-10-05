using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Testing;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-14 — <see cref="ActivityEnvironment"/> isolation tests: heartbeating, cancellation observed
/// mid-activity, and the <see cref="Result"/>→failure mapping, all with no server and no workflow.
/// </summary>
public sealed class ActivityEnvironmentTests
{
    private sealed class HeartbeatingActivity(ILogger<HeartbeatingActivity> logger, IClock clock) : ActivityBase(logger, clock)
    {
        public Task<string> RunAsync(string detail)
        {
            Heartbeat(detail);
            return Task.FromResult("heartbeat-done");
        }
    }

    private sealed class CancellableActivity(ILogger<CancellableActivity> logger, IClock clock) : ActivityBase(logger, clock)
    {
        public async Task<string> RunAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ActivityExecutionContext.Current.CancellationToken);
            return "unreachable";
        }
    }

    private sealed class FailingActivity(ILogger<FailingActivity> logger, IClock clock) : ActivityBase(logger, clock)
    {
        public Task<string> RunAsync(Result<string> upstreamResult)
        {
            if (upstreamResult.IsFailure)
            {
                throw Fail(upstreamResult.Error);
            }

            return Task.FromResult(upstreamResult.Value);
        }
    }

    [Fact]
    public async Task Heartbeat_IsRecordedThroughTheActivityEnvironment()
    {
        List<object?[]> recordedHeartbeats = [];
        var env = new ActivityEnvironment { Heartbeater = details => recordedHeartbeats.Add(details) };

        var activity = new HeartbeatingActivity(NullLogger<HeartbeatingActivity>.Instance, new FixedClock());

        string result = await env.RunAsync(() => activity.RunAsync("progress-marker"));

        result.Should().Be("heartbeat-done");
        recordedHeartbeats.Should().ContainSingle();
        recordedHeartbeats[0].Should().ContainSingle(detail => Equals(detail, "progress-marker"));
    }

    [Fact]
    public async Task Cancellation_IsObservedMidActivity()
    {
        var cts = new CancellationTokenSource();
        var env = new ActivityEnvironment { CancellationTokenSource = cts };

        var activity = new CancellableActivity(NullLogger<CancellableActivity>.Instance, new FixedClock());

        Task<string> runTask = env.RunAsync(() => activity.RunAsync());
        await cts.CancelAsync();

        Func<Task> act = () => runTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResultFailure_MapsToApplicationFailureException_ThroughFail()
    {
        var env = new ActivityEnvironment();
        var activity = new FailingActivity(NullLogger<FailingActivity>.Instance, new FixedClock());
        Error error = Error.Conflict("wf.test.activity_conflict", "duplicate detected");

        Func<Task> act = () => env.RunAsync(() => activity.RunAsync(Result<string>.Failure(error)));

        (await act.Should().ThrowAsync<ApplicationFailureException>())
            .Which.ErrorType.Should().Be(error.Code);
    }

    [Fact]
    public async Task ResultSuccess_DoesNotThrow_AndReturnsTheValue()
    {
        var env = new ActivityEnvironment();
        var activity = new FailingActivity(NullLogger<FailingActivity>.Instance, new FixedClock());

        string result = await env.RunAsync(() => activity.RunAsync(Result<string>.Success("all-good")));

        result.Should().Be("all-good");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);
    }
}
