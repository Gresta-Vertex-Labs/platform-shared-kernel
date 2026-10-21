using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Scheduling.Diagnostics;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Options;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Scheduling.Registry;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Extensions;

/// <summary>
/// T-06 / DI registration / options-validation coverage: the registration surface resolves through a
/// real <see cref="IHost.StartAsync"/>, an unconfigured <c>IDistributedLockService</c> logs the
/// mandatory single-replica startup <c>Warning</c> (by structured <c>EventId</c>, never a
/// rendered-message string comparison), and invalid configuration fails fast at startup rather than at
/// first use.
/// </summary>
public sealed class SchedulingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSharedKernelScheduling_ResolvesThroughRealHostStartAsync()
    {
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IClock>(new SharedKernel.Testing.Clocks.FakeClock());
                services.AddSharedKernelApplication(typeof(RecordingCommand).Assembly, app => app.UseMediatR());
                services.AddSingleton<RecordingCommandRecorder>();

                ISchedulingBuilder builder = services.AddSharedKernelScheduling();
                builder.AddRecurring<RecordingCommand>(
                    "job-1",
                    "0 0 2 * * ?",
                    _ => new RecordingCommand(),
                    options =>
                    {
                        options.MisfirePolicy = MisfirePolicy.Skip;
                        options.OverlapPolicy = OverlapPolicy.Skip;
                    });
            })
            .Build();

        Func<Task> act = () => host.StartAsync();
        await act.Should().NotThrowAsync();

        host.Services.GetRequiredService<IScheduledJobRegistry>().Should().NotBeNull();
        host.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName).Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public void AddSharedKernelScheduling_OnABareServiceCollection_ResolvesTheMetrics()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelScheduling();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SchedulingMetrics>().Should().NotBeNull();
        provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>().Should().NotBeNull();
    }

    [Fact]
    public void TwoServiceProviders_GetIsolatedMeters()
    {
        using ServiceProvider first = BuildBare();
        using ServiceProvider second = BuildBare();
        using SharedKernel.Testing.Telemetry.MetricRecorder recorder = SharedKernel.Testing.Telemetry.MetricRecorder.StartRecording(SchedulingTelemetry.Name);

        first.GetRequiredService<SchedulingMetrics>().RecordMisfire("isolated-a");
        second.GetRequiredService<SchedulingMetrics>().RecordMisfire("isolated-b");

        first.GetRequiredService<SchedulingMetrics>().Should().NotBeSameAs(second.GetRequiredService<SchedulingMetrics>());
        first.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>().Should().NotBeSameAs(second.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        recorder.Sum(SchedulingMetrics.MisfiredName, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, "isolated-a")).Should().Be(1);
        recorder.Sum(SchedulingMetrics.MisfiredName, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, "isolated-b")).Should().Be(1);

        static ServiceProvider BuildBare()
        {
            var services = new ServiceCollection();
            services.AddSharedKernelScheduling();
            return services.BuildServiceProvider();
        }
    }

    [Fact]
    public void TheSharedKernelWildcard_SeesTheActivitySource()
    {
        using SharedKernel.Testing.Telemetry.ActivityRecorder recorder = SharedKernel.Testing.Telemetry.ActivityRecorder.StartRecordingPrefix("SharedKernel.");

        using System.Diagnostics.Activity? activity = SchedulingTelemetry.ActivitySource.StartActivity("scheduling fire");

        activity.Should().NotBeNull("a listener on the SharedKernel.* wildcard must receive the package source");
        SchedulingTelemetry.Name.Should().StartWith("SharedKernel.");
    }

    [Fact]
    public async Task NoDistributedLockServiceRegistered_LogsSingleReplicaStartupWarning()
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
                }));
        // No lockService supplied — single-replica mode.

        await harness.StartAsync();

        harness.HostedServiceLogRecords.Count(r => r.EventId.Id == 19002).Should().Be(1);
        LogRecord record = harness.HostedServiceLogRecords.ShouldHaveLogged(
            new EventId(19002), LogLevel.Warning);
        record.Message.Should().Contain("every job runs once per replica").And.Contain("AddRedisDistributedLocking");

        await harness.StopAsync();
    }

    [Fact]
    public async Task NoJobsRegisteredAndNoLockService_LogsNoSingleReplicaWarning()
    {
        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(registerJobs: _ => { });

        await harness.StartAsync();

        harness.HostedServiceLogRecords.Should().NotContain(r => r.EventId.Id == 19002);

        await harness.StopAsync();
    }

    [Fact]
    public async Task LockServiceRegisteredWithJobs_LogsNoSingleReplicaWarning()
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
            lockService: new SharedKernel.Testing.Caching.FakeDistributedLockService());

        await harness.StartAsync();

        harness.HostedServiceLogRecords.Should().NotContain(r => r.EventId.Id == 19002);

        await harness.StopAsync();
    }

    [Fact]
    public void AddSharedKernelScheduling_ValidConfiguration_BindsSuccessfully()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Scheduling:TickInterval"] = "00:00:02",
                ["SharedKernel:Scheduling:DefaultLockExpiry"] = "00:10:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSharedKernelScheduling();

        using ServiceProvider provider = services.BuildServiceProvider();
        SchedulingOptions options = provider.GetRequiredService<IOptions<SchedulingOptions>>().Value;

        options.TickInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.DefaultLockExpiry.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddSharedKernelScheduling_InvalidConfiguration_FailsAtStartup_NotFirstUse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Below the [Range] floor of 100ms.
                ["SharedKernel:Scheduling:TickInterval"] = "00:00:00.001",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSharedKernelScheduling();

        using ServiceProvider provider = services.BuildServiceProvider();

        // ValidateOnStart's IStartupValidator triggers eager validation — resolving it stands in for
        // IHost.StartAsync()'s own automatic invocation without pulling in the full generic host here.
        IEnumerable<IStartupValidator> validators = provider.GetServices<IStartupValidator>();

        Action act = () =>
        {
            foreach (IStartupValidator validator in validators)
            {
                validator.Validate();
            }
        };

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ProgrammaticConfigure_OverridesBoundConfigurationValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Scheduling:TickInterval"] = "00:00:02",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSharedKernelScheduling(options => options.TickInterval = TimeSpan.FromSeconds(5));

        using ServiceProvider provider = services.BuildServiceProvider();
        SchedulingOptions options = provider.GetRequiredService<IOptions<SchedulingOptions>>().Value;

        options.TickInterval.Should().Be(TimeSpan.FromSeconds(5));
    }
}
