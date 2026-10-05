using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Primitives.Health;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Probes;

/// <summary>
/// T-07 — The scheduler readiness probe reports <c>IsRunning</c>/<c>RegisteredJobCount</c>/
/// <c>LastTickUtc</c> correctly and performs zero I/O. Zero-I/O is proven by wiring an
/// <see cref="IDistributedLockService"/> substitute that throws if ever touched, then calling
/// the scheduler readiness probe directly — never through the hosted loop.
/// </summary>
public sealed class SchedulerServiceProbeTests
{
    private static IDistributedLockService NewThrowingLockService()
    {
        var lockService = Substitute.For<IDistributedLockService>();
        lockService
            .TryAcquireLeaseAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the scheduler readiness probe must never perform I/O."));
        lockService
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockOptions?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the scheduler readiness probe must never perform I/O."));
        return lockService;
    }

    [Fact]
    public async Task ProbeAsync_BeforeStart_ReportsNotRunning_AndNoLastTick()
    {
        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "job-1",
                "0 0 2 * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            lockService: NewThrowingLockService());

        IReadinessProbe probe = harness.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName);

        ReadinessReport health = await probe.ProbeAsync();

        health.Data[SchedulerReadiness.IsRunningKey].Should().Be(false);
        health.Data[SchedulerReadiness.RegisteredJobCountKey].Should().Be(1);
        health.Data.Should().NotContainKey(SchedulerReadiness.LastTickUtcKey);
    }

    [Fact]
    public async Task ProbeAsync_AfterStart_ReportsRunning_AndRegisteredJobCount()
    {
        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder
                .AddRecurring<RecordingCommand>(
                    "job-1",
                    "0 0 2 * * ?",
                    _ => new RecordingCommand(),
                    options =>
                    {
                        options.MisfirePolicy = MisfirePolicy.Skip;
                        options.OverlapPolicy = OverlapPolicy.Skip;
                    })
                .AddDeferred<RecordingCommand>(
                    "job-2",
                    DateTimeOffset.UtcNow.AddDays(1),
                    _ => new RecordingCommand(),
                    options =>
                    {
                        options.MisfirePolicy = MisfirePolicy.Skip;
                        options.OverlapPolicy = OverlapPolicy.Skip;
                    }),
            lockService: NewThrowingLockService());

        await harness.StartAsync();

        IReadinessProbe probe = harness.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName);
        ReadinessReport health = await probe.ProbeAsync();

        health.Data[SchedulerReadiness.IsRunningKey].Should().Be(true);
        health.IsHealthy.Should().BeTrue();
        health.Data[SchedulerReadiness.RegisteredJobCountKey].Should().Be(2);

        await harness.StopAsync();
    }

    [Fact]
    public async Task ProbeAsync_AfterAtLeastOneTick_ReportsLastTickUtc()
    {
        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "job-1",
                "0 0 2 * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            lockService: NewThrowingLockService());

        await harness.StartAsync();

        IReadinessProbe probe = harness.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName);

        bool observed = await Eventually.UntilAsync(() => harness.HostedService.LastTickUtc is not null);
        observed.Should().BeTrue();

        ReadinessReport health = await probe.ProbeAsync();
        health.Data.Should().ContainKey(SchedulerReadiness.LastTickUtcKey);

        await harness.StopAsync();
    }

    [Fact]
    public async Task ProbeAsync_AfterStop_ReportsNotRunning()
    {
        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "job-1",
                "0 0 2 * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            lockService: NewThrowingLockService());

        await harness.StartAsync();
        await harness.StopAsync();

        IReadinessProbe probe = harness.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName);
        ReadinessReport health = await probe.ProbeAsync();

        health.Data[SchedulerReadiness.IsRunningKey].Should().Be(false);
    }
}
