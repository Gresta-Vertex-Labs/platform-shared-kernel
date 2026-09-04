using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Probes;

/// <summary>
/// T-07 — <see cref="ISchedulerServiceProbe"/> reports <c>IsRunning</c>/<c>RegisteredJobCount</c>/
/// <c>LastTickUtc</c> correctly and performs zero I/O. Zero-I/O is proven by wiring an
/// <see cref="IDistributedLockService"/> substitute that throws if ever touched, then calling
/// <see cref="ISchedulerServiceProbe.ProbeAsync"/> directly — never through the hosted loop.
/// </summary>
public sealed class SchedulerServiceProbeTests
{
    private static IDistributedLockService NewThrowingLockService()
    {
        var lockService = Substitute.For<IDistributedLockService>();
        lockService
            .AcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("ISchedulerServiceProbe must never perform I/O."));
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

        ISchedulerServiceProbe probe = harness.Services.GetRequiredService<ISchedulerServiceProbe>();

        SchedulerServiceHealth health = await probe.ProbeAsync();

        health.IsRunning.Should().BeFalse();
        health.RegisteredJobCount.Should().Be(1);
        health.LastTickUtc.Should().BeNull();
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

        ISchedulerServiceProbe probe = harness.Services.GetRequiredService<ISchedulerServiceProbe>();
        SchedulerServiceHealth health = await probe.ProbeAsync();

        health.IsRunning.Should().BeTrue();
        health.RegisteredJobCount.Should().Be(2);

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

        ISchedulerServiceProbe probe = harness.Services.GetRequiredService<ISchedulerServiceProbe>();

        bool observed = await Eventually.UntilAsync(() => harness.HostedService.LastTickUtc is not null);
        observed.Should().BeTrue();

        SchedulerServiceHealth health = await probe.ProbeAsync();
        health.LastTickUtc.Should().NotBeNull();

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

        ISchedulerServiceProbe probe = harness.Services.GetRequiredService<ISchedulerServiceProbe>();
        SchedulerServiceHealth health = await probe.ProbeAsync();

        health.IsRunning.Should().BeFalse();
    }
}
