using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Jobs;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Registry;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Registry;

/// <summary>
/// Registration-time validation: <see cref="MisfirePolicy"/>/<see cref="OverlapPolicy"/> mandatory,
/// invalid cron rejected via Quartz's <see cref="Quartz.CronExpression"/>, duplicate job names rejected
/// — all failing at the <c>AddRecurring</c>/<c>AddDeferred</c> call site, never lazily.
/// </summary>
public sealed class ScheduledJobRegistryTests
{
    private static ISchedulingBuilder NewBuilder() =>
        new ServiceCollection().AddSharedKernelScheduling();

    [Fact]
    public void AddRecurring_MisfirePolicyUnset_ThrowsImmediately()
    {
        ISchedulingBuilder builder = NewBuilder();

        Action act = () => builder.AddRecurring<RecordingCommand>(
            "job-1",
            "0 0 2 * * ?",
            _ => new RecordingCommand(),
            options => options.OverlapPolicy = OverlapPolicy.Skip);

        act.Should().Throw<ArgumentException>().WithMessage("*MisfirePolicy*");
    }

    [Fact]
    public void AddRecurring_OverlapPolicyUnset_ThrowsImmediately()
    {
        ISchedulingBuilder builder = NewBuilder();

        Action act = () => builder.AddRecurring<RecordingCommand>(
            "job-1",
            "0 0 2 * * ?",
            _ => new RecordingCommand(),
            options => options.MisfirePolicy = MisfirePolicy.Skip);

        act.Should().Throw<ArgumentException>().WithMessage("*OverlapPolicy*");
    }

    [Fact]
    public void AddDeferred_BothPoliciesUnset_ThrowsForMisfireFirst()
    {
        ISchedulingBuilder builder = NewBuilder();

        Action act = () => builder.AddDeferred<RecordingCommand>(
            "job-1",
            DateTimeOffset.UtcNow.AddMinutes(5),
            _ => new RecordingCommand(),
            _ => { });

        act.Should().Throw<ArgumentException>().WithMessage("*MisfirePolicy*");
    }

    [Fact]
    public void AddRecurring_BothPoliciesSet_Succeeds()
    {
        ISchedulingBuilder builder = NewBuilder();

        Action act = () => builder.AddRecurring<RecordingCommand>(
            "job-1",
            "0 0 2 * * ?",
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        act.Should().NotThrow();
    }

    [Fact]
    public void AddRecurring_InvalidCronExpression_ThrowsArgumentException()
    {
        ISchedulingBuilder builder = NewBuilder();

        Action act = () => builder.AddRecurring<RecordingCommand>(
            "job-1",
            "not a cron expression",
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not a valid cron expression*")
            .And.ParamName.Should().Be("cronExpression");
    }

    [Fact]
    public void AddRecurring_DuplicateJobName_ThrowsArgumentException()
    {
        ISchedulingBuilder builder = NewBuilder();
        builder.AddRecurring<RecordingCommand>(
            "duplicate-name",
            "0 0 2 * * ?",
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        Action act = () => builder.AddDeferred<RecordingCommand>(
            "duplicate-name",
            DateTimeOffset.UtcNow.AddMinutes(5),
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        act.Should().Throw<ArgumentException>().WithMessage("*already registered*");
    }

    [Fact]
    public void AddRecurring_ReturnsSameBuilder_ForFluentChaining()
    {
        ISchedulingBuilder builder = NewBuilder();

        IScheduledJobRegistry result = builder.AddRecurring<RecordingCommand>(
            "job-1",
            "0 0 2 * * ?",
            _ => new RecordingCommand(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddRecurring_CommandFactoryReceivesExecutionContext()
    {
        ISchedulingBuilder builder = NewBuilder();
        ScheduledJobExecutionContext? captured = null;

        builder.AddRecurring<RecordingCommand>(
            "job-1",
            "0 0 2 * * ?",
            ctx =>
            {
                captured = ctx;
                return new RecordingCommand();
            },
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            });

        // Registration itself never invokes the factory — only a later fire does.
        captured.Should().BeNull();
    }
}
