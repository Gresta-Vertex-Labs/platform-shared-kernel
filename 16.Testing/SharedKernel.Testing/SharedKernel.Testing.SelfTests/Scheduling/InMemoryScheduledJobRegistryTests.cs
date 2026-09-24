using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Jobs;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Registry;
using SharedKernel.Testing.Scheduling;

namespace SharedKernel.Testing.SelfTests.Scheduling;

/// <summary>
/// Proves <see cref="InMemoryScheduledJobRegistry"/> genuinely implements
/// <see cref="IScheduledJobRegistry"/> — ticks fire only when the test calls
/// <see cref="InMemoryScheduledJobRegistry.TriggerAsync"/> (never real elapsed wall-clock time), and
/// the fire/skip(overlap)/misfire assertion helpers all behave correctly. No consuming service has
/// adopted this fake yet, so this self-test is the only behavioral proof today, per the SelfTests
/// routing rule.
/// </summary>
public sealed class InMemoryScheduledJobRegistryTests
{
    private sealed record TestCommand(int Value) : ICommand;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public static readonly List<int> Handled = [];

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            Handled.Add(request.Value);
            return Task.FromResult(Result.Success());
        }
    }

    private static ISender BuildSender()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<InMemoryScheduledJobRegistryTests>());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static void Configure(ScheduledJobOptions options)
    {
        options.MisfirePolicy = MisfirePolicy.Skip;
        options.OverlapPolicy = OverlapPolicy.Skip;
    }

    [Fact]
    public void AddRecurring_ThenAddDeferred_BothRegisterSuccessfully()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        var result = registry
            .AddRecurring<TestCommand>("recurring-job", "0 0 * * * ?", ctx => new TestCommand(1), Configure)
            .AddDeferred<TestCommand>("deferred-job", DateTimeOffset.UtcNow.AddHours(1), ctx => new TestCommand(2), Configure);

        Assert.Same(registry, result);
    }

    [Fact]
    public void AddRecurring_DuplicateJobName_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), Configure);

        Assert.Throws<ArgumentException>(() =>
            registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(2), Configure));
    }

    [Fact]
    public void AddRecurring_MisfirePolicyNotSet_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        Assert.Throws<ArgumentException>(() =>
            registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), options => options.OverlapPolicy = OverlapPolicy.Skip));
    }

    [Fact]
    public void AddDeferred_OverlapPolicyNotSet_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        Assert.Throws<ArgumentException>(() =>
            registry.AddDeferred<TestCommand>("job-1", DateTimeOffset.UtcNow, ctx => new TestCommand(1), options => options.MisfirePolicy = MisfirePolicy.Skip));
    }

    [Fact]
    public async Task TriggerAsync_UnregisteredJob_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.TriggerAsync("nope", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TriggerAsync_DispatchesCommandThroughSender_RecordsFire()
    {
        TestCommandHandler.Handled.Clear();
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(42), Configure);

        var result = await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsSuccess);
        Assert.Contains(42, TestCommandHandler.Handled);
        registry.ShouldHaveFired("job-1");
    }

    [Fact]
    public async Task TriggerAsync_PassesSimulatedFireTimeAndFencingTokenIntoExecutionContext()
    {
        ScheduledJobExecutionContext? captured = null;
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>(
            "job-1",
            "0 0 * * * ?",
            ctx =>
            {
                captured = ctx;
                return new TestCommand(1);
            },
            Configure);

        var fireTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await registry.TriggerAsync("job-1", fireTime, fencingToken: 7);

        Assert.NotNull(captured);
        Assert.Equal(fireTime, captured!.ScheduledFireTimeUtc);
        Assert.Equal(fireTime, captured.ActualFireTimeUtc);
        Assert.Equal(7, captured.FencingToken);
    }

    [Fact]
    public void ShouldHaveFired_WrongCount_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), Configure);

        Assert.Throws<InvalidOperationException>(() => registry.ShouldHaveFired("job-1", times: 1));
    }

    [Fact]
    public async Task TriggerAsync_ThreeTimes_ShouldHaveFiredThreeTimes()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), Configure);

        await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);
        await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);
        await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);

        registry.ShouldHaveFired("job-1", times: 3);
    }

    [Fact]
    public async Task BeginInFlight_OverlapSkip_DiscardsSecondTick()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>(
            "job-1",
            "0 0 * * * ?",
            ctx => new TestCommand(1),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        using (registry.BeginInFlight("job-1"))
        {
            var result = await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);
            Assert.Null(result);
        }

        registry.ShouldHaveSkipped("job-1");
        registry.ShouldHaveFired("job-1", times: 0);
    }

    [Fact]
    public async Task BeginInFlight_Disposed_AllowsSubsequentTickToFireNormally()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), Configure);

        using (registry.BeginInFlight("job-1"))
        {
            // still in flight — nothing triggered here
        }

        var result = await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        registry.ShouldHaveFired("job-1");
    }

    [Fact]
    public void ShouldHaveSkipped_NeverSkipped_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        Assert.Throws<InvalidOperationException>(() => registry.ShouldHaveSkipped("job-1"));
    }

    [Fact]
    public async Task TriggerAsync_SimulatedMisfire_SkipPolicy_DiscardsFire()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>(
            "job-1",
            "0 0 * * * ?",
            ctx => new TestCommand(1),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        var result = await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow, simulatedMisfire: true);

        Assert.Null(result);
        registry.ShouldHaveMisfired("job-1");
        registry.ShouldHaveFired("job-1", times: 0);
    }

    [Fact]
    public async Task TriggerAsync_SimulatedMisfire_FireOncePolicy_StillDispatches()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>(
            "job-1",
            "0 0 * * * ?",
            ctx => new TestCommand(1),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.FireOnce;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        var result = await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow, simulatedMisfire: true);

        Assert.NotNull(result);
        registry.ShouldHaveMisfired("job-1");
        registry.ShouldHaveFired("job-1", times: 1);
    }

    [Fact]
    public void ShouldHaveMisfired_NeverMisfired_Throws()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());

        Assert.Throws<InvalidOperationException>(() => registry.ShouldHaveMisfired("job-1"));
    }

    [Fact]
    public async Task Reset_ClearsRegistrationsAndRecordedState()
    {
        var registry = new InMemoryScheduledJobRegistry(BuildSender());
        registry.AddRecurring<TestCommand>("job-1", "0 0 * * * ?", ctx => new TestCommand(1), Configure);
        await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow, simulatedMisfire: true);

        registry.Reset();

        Assert.Empty(registry.Fired);
        Assert.Empty(registry.Skipped);
        Assert.Empty(registry.Misfired);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await registry.TriggerAsync("job-1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_NullSender_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryScheduledJobRegistry(null!));
    }
}
