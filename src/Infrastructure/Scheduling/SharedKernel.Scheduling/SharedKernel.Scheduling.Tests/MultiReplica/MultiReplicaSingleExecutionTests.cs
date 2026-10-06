using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Hosting;
using SharedKernel.Scheduling.Options;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Scheduling.Tests.MultiReplica;

/// <summary>
/// T-01, this domain's load-bearing test: two independent <see cref="SchedulingHostedService"/>
/// instances — genuinely separate DI containers, standing in for two separate process replicas — both
/// racing to fire the SAME registered job against a REAL Redis-backed <see cref="IDistributedLockService"/>.
/// Exactly one of them must win each tick. A mocked lock could never exhibit the contention this test
/// exists to rule out, so this never substitutes a fake for <see cref="IDistributedLockService"/>.
/// </summary>
[Collection("Redis")]
public sealed class MultiReplicaSingleExecutionTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _redis = new();

    public Task InitializeAsync() => _redis.InitializeAsync();

    public Task DisposeAsync() => _redis.DisposeAsync();

    private (ServiceProvider Provider, SchedulingHostedService HostedService) BuildReplica(
        string sharedJobName,
        RecordingCommandRecorder sharedRecorder,
        FakeClock sharedClock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton<IClock>(sharedClock);
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelApplication(typeof(RecordingCommand).Assembly, app => app.UseMediatR());
        // The SAME recorder instance is shared across both "replicas" so the test can assert the
        // total invocation count across both processes combined — exactly what proves single
        // execution ACROSS replicas rather than merely within one.
        services.AddSingleton(sharedRecorder);

        services.AddRedisConnection(o => o.ConnectionString = _redis.ConnectionString);
        services.AddRedisDistributedLocking();

        // 150ms — comfortably inside SchedulingOptions.TickInterval's [Range] floor of 100ms.
        ISchedulingBuilder builder = services.AddSharedKernelScheduling(o => o.TickInterval = TimeSpan.FromMilliseconds(150));
        builder.AddRecurring<RecordingCommand>(
            sharedJobName,
            "* * * * * ?",
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        ServiceProvider provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<SchedulingHostedService>();
        return (provider, hostedService);
    }

    [Fact]
    public async Task TwoReplicas_SameJob_FiresExactlyOncePerTick_NeverTwice()
    {
        const string jobName = "shared-nightly-job";
        var recorder = new RecordingCommandRecorder();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        (ServiceProvider providerA, SchedulingHostedService hostedA) = BuildReplica(jobName, recorder, clock);
        (ServiceProvider providerB, SchedulingHostedService hostedB) = BuildReplica(jobName, recorder, clock);

        await using var _a = providerA;
        await using var _b = providerB;

        // BackgroundService.StartAsync schedules ExecuteAsync via Task.Run and returns immediately —
        // it does not wait for the loop's startup prefix (computing the job's initial
        // NextFireTimeUtc from IClock.UtcNow) to have run. Advancing the shared clock before that
        // prefix executes on both replicas would corrupt their very first due-time computation, so
        // this waits for both to report IsRunning first (see SchedulingTestHarness.StartAsync's
        // identical rationale).
        await hostedA.StartAsync(CancellationToken.None);
        await hostedB.StartAsync(CancellationToken.None);
        (await Eventually.UntilAsync(() => hostedA.IsRunning && hostedB.IsRunning, TimeSpan.FromSeconds(5)))
            .Should().BeTrue("both replicas must complete their startup prefix before the clock advances");

        try
        {
            // Advance the shared clock through several due ticks. Both replicas observe the same
            // IClock, so both consider the job due at the same instants — real Redis-backed fencing
            // is the only thing standing between that and a duplicate fire.
            //
            // Each tick is fired before the clock moves on. A fixed real-time pause per tick was not
            // enough on a loaded runner: a replica whose loop ran late saw the occurrence more than one
            // TickInterval overdue, MisfirePolicy.Skip discarded it, and the run fell short of five
            // fires without any duplicate — a timing artefact, not a fencing failure.
            DateTimeOffset current = clock.UtcNow;
            for (var i = 1; i <= 5; i++)
            {
                current = current.AddSeconds(1);
                clock.Set(current);

                int expected = i;
                bool fired = await Eventually.UntilAsync(
                    () => recorder.InvocationCount >= expected,
                    timeout: TimeSpan.FromSeconds(15));
                fired.Should().BeTrue($"due tick {expected} of 5 should fire once across the two replicas combined");

                // Give the losing replica time to observe this instant and lose the race for it, so a
                // duplicate fire for this tick would show up before the next advance.
                await Task.Delay(400);
            }
            recorder.InvocationCount.Should().Be(5, "a real fencing lock must prevent both replicas from firing the same tick");
            recorder.MaxConcurrentObserved.Should().Be(1, "no two executions — from either replica — may run concurrently for the same tick");
        }
        finally
        {
            await hostedA.StopAsync(CancellationToken.None);
            await hostedB.StopAsync(CancellationToken.None);
        }
    }
}
