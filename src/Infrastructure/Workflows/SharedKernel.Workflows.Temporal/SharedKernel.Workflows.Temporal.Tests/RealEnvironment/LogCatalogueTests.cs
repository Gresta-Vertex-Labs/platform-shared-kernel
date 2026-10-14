using System.Reflection;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Codec;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Exceptions;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Health;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Activities;
using Temporalio.Api.Common.V1;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Testing;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// The log catalogue of the package: every event declared in <see cref="WorkflowLoggingEventIds"/> (17000 to 17015) is
/// emitted by at least one scenario run here, each record follows the log conventions and lies in the package sub-block,
/// and each event carries its documented template. A declared event no scenario can emit is dead code.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class LogCatalogueTests(TemporalTestFixture fixture)
{
    private const string Queue = "sk-workflows-catalogue-queue";

    private static readonly Dictionary<int, string> Templates = new()
    {
        [17000] = "Started a workflow of type '{WorkflowType}'.",
        [17001] = "The '{Operation}' workflow dispatch was refused because no tenant scope was supplied.",
        [17002] = "Dispatched signal '{SignalName}' to a workflow.",
        [17003] = "Dispatched query '{QueryName}' to a workflow.",
        [17004] = "A workflow was terminated and no compensation runs.",
        [17005] = "No tenant header was present on a workflow of type '{WorkflowType}', so its tenant scope is global.",
        [17006] = "The payload codec failed to {Operation} a payload.",
        [17007] = "Built a Temporal worker for task queue '{TaskQueue}' with {WorkflowCount} workflows and {ActivityCount} activity types.",
        [17008] = "Built a client-only Temporal composition with no worker hosted.",
        [17009] = "The workflow service probe is degraded with reachable {Reachable}, namespace addressable {NamespaceAddressable} and worker pollers active {WorkerPollersActive}.",
        [17010] = "Payload encryption is configured for this Temporal client composition.",
        [17011] = "Recorded a heartbeat for activity type '{ActivityType}'.",
        [17012] = "Raw Temporal client access is enabled and bypasses tenant scoping and workflow id composition.",
        [17013] = "The workflow service probe {Check} check faulted.",
        [17014] = "The '{Operation}' workflow dispatch failed with error code '{ErrorCode}' of type {ErrorType}.",
        [17015] = "The '{Operation}' workflow dispatch hit a Temporal failure with no mapping.",
    };

    private sealed class HeartbeatingActivity(ILogger<HeartbeatingActivity> logger, IClock clock) : ActivityBase(logger, clock)
    {
        public Task<string> RunAsync()
        {
            Heartbeat("progress");
            return Task.FromResult("done");
        }
    }

    private static IConfiguration Configuration(string targetHost, string @namespace) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:Temporal:TargetHost"] = targetHost,
            ["Workflows:Temporal:Namespace"] = @namespace,
        })
        .Build();

    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = Queue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private static List<LogRecord> RecordsOf(InMemoryLoggerFactory factory) =>
        [.. factory.Loggers.Values.SelectMany(logger => logger.Records)];

    [Fact]
    public async Task EveryDeclaredEvent_IsEmittedByAtLeastOneScenario_WithItsTemplate_InsideTheSubBlock_AndFollowingTheConvention()
    {
        string targetHost = fixture.Environment.Client.Connection.Options.TargetHost!;
        string @namespace = fixture.Environment.Client.Options.Namespace;
        List<LogRecord> records = [];

        // A worker composition with encryption and raw client access: 17007 (hosted start), 17010 (client built with
        // the codec), 17012 (accessor resolved), then the dispatch scenarios.
        var workerServices = new ServiceCollection();
        workerServices.AddInMemoryLoggerFactory();
        workerServices.AddSingleton<IClock>(new FakeClock());
        workerServices.AddSingleton<IEncryptionKeyProvider>(
            new StaticEncryptionKeyProvider("v1", [new CryptographicKey("v1", Enumerable.Repeat((byte)7, 32).ToArray())]));
        workerServices.AddSharedKernelCryptography(Configuration(targetHost, @namespace)).AddSymmetricEncryption();
        workerServices
            .AddSharedKernelTemporalWorkflows(Configuration(targetHost, @namespace))
            .AddWorkflow<EchoWorkflow>()
            .AddWorkflow<CompensatingWorkflow>()
            .AddActivities<EchoActivity>()
            .AddActivities<CompensationActivity>()
            .WithWorker(Queue)
            .WithPayloadEncryption()
            .AllowRawClientAccess()
            .Build();
        await using ServiceProvider workerProvider = workerServices.BuildServiceProvider();
        var workerLoggers = (InMemoryLoggerFactory)workerProvider.GetRequiredService<ILoggerFactory>();
        List<IHostedService> hosted = [.. workerProvider.GetServices<IHostedService>()];
        foreach (IHostedService service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            _ = workerProvider.GetRequiredService<ITemporalRawClientAccessor>();
            using (IServiceScope scope = workerProvider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
                TenantScope tenant = TenantScope.For(TestTenants.A);

                Result<IWorkflowHandle> started = await dispatcher.StartAsync<CompensatingWorkflow, string>(
                    "catalogue-compensation", Options($"catalogue-{Guid.NewGuid():N}"), tenant);
                started.IsSuccess.Should().BeTrue();
                (await started.Value.SignalAsync("SetMessage", "hello")).IsSuccess.Should().BeTrue();
                (await started.Value.QueryAsync<string>("GetMessage")).IsSuccess.Should().BeTrue();
                (await started.Value.TerminateAsync("catalogue scenario")).IsSuccess.Should().BeTrue();

                (await dispatcher.StartAsync<CompensatingWorkflow, string>("x", Options("global"), TenantScope.Global))
                    .IsFailure.Should().BeTrue();
                (await dispatcher.DescribeAsync($"{TestTenants.A}:CompensatingWorkflow:never-started", tenant))
                    .IsFailure.Should().BeTrue();
            }

            // 17005: a workflow started by a client that writes no tenant header.
            WorkflowHandle untenanted = await fixture.Environment.Client.StartWorkflowAsync(
                "EchoWorkflow", ["x"], new WorkflowOptions($"untenanted:EchoWorkflow:{Guid.NewGuid():N}", Queue));
            // The worker encrypts the result, which a client without the codec cannot read: wait for completion by status.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while ((await untenanted.DescribeAsync()).Status != WorkflowExecutionStatus.Completed)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        finally
        {
            foreach (IHostedService service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }

        records.AddRange(RecordsOf(workerLoggers));

        // 17008: a client-only composition logs once, when its client is first built.
        var clientOnlyServices = new ServiceCollection();
        clientOnlyServices.AddInMemoryLoggerFactory();
        clientOnlyServices.AddSingleton<IClock>(new FakeClock());
        clientOnlyServices.AddSharedKernelTemporalWorkflows(Configuration(targetHost, @namespace)).AsClientOnly().Build();
        await using ServiceProvider clientOnlyProvider = clientOnlyServices.BuildServiceProvider();
        _ = clientOnlyProvider.GetRequiredService<ITemporalClient>().Connection;
        records.AddRange(RecordsOf((InMemoryLoggerFactory)clientOnlyProvider.GetRequiredService<ILoggerFactory>()));

        // 17009 and 17013: the readiness probe against a service nobody listens on.
        ITemporalClient unreachable = TemporalClient.CreateLazy(
            new TemporalClientConnectOptions("127.0.0.1:1") { Namespace = "default" });
        var probeLogger = new InMemoryLogger<WorkflowServiceProbe>();
        var probeServices = new ServiceCollection();
        probeServices.AddSingleton(unreachable);
        probeServices.AddSingleton<IOptions<TemporalOptions>>(
            AsOptions(new TemporalOptions { TargetHost = "unused", Namespace = "default" }));
        probeServices.AddSingleton<ILogger<WorkflowServiceProbe>>(probeLogger);
        await new WorkflowServiceProbe(probeServices.BuildServiceProvider()).ProbeAsync();
        records.AddRange(probeLogger.Records);

        // 17006: a payload that carries the codec marker but is not an encrypted payload.
        var codecLogger = new InMemoryLogger<EncryptionPayloadCodec>();
        var codec = new EncryptionPayloadCodec(new SharedKernel.Testing.Cryptography.FakeSymmetricEncryptionService(), codecLogger);
        var malformed = new Payload { Data = ByteString.CopyFromUtf8("not an encrypted payload") };
        malformed.Metadata["encoding"] = ByteString.CopyFromUtf8("binary/encrypted-sk-v2");
        await FluentActions.Invoking(() => codec.DecodeAsync([malformed])).Should().ThrowAsync<WorkflowPayloadCodecException>();
        records.AddRange(codecLogger.Records);

        // 17011: an activity heartbeat.
        var activityLogger = new InMemoryLogger<HeartbeatingActivity>();
        var activity = new HeartbeatingActivity(activityLogger, new FakeClock());
        await new ActivityEnvironment { Heartbeater = _ => { } }.RunAsync(() => activity.RunAsync());
        records.AddRange(activityLogger.Records);

        // 17015: a Temporal failure with no mapping row.
        ITemporalClient faulting = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(targetHost)
        {
            Namespace = @namespace,
            Interceptors = [new DispatchCatchNarrowingTests.ThrowingInterceptor(
                () => new DispatchCatchNarrowingTests.UnmappedTemporalException("unmapped"))],
        });
        var dispatchLogger = new InMemoryLogger<WorkflowDispatcher>();
        var faultingDispatcher = new WorkflowDispatcher(faulting, new WorkflowIdFactory(), dispatchLogger);
        (await faultingDispatcher.DescribeAsync($"{TestTenants.A}:CompensatingWorkflow:any", TenantScope.For(TestTenants.A)))
            .IsFailure.Should().BeTrue();
        records.AddRange(dispatchLogger.Records);

        int[] declared = typeof(WorkflowLoggingEventIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field is { IsLiteral: true } && field.Name != nameof(WorkflowLoggingEventIds.Base))
            .Select(field => (int)field.GetRawConstantValue()!)
            .Order()
            .ToArray();
        declared.Should().Equal(Enumerable.Range(LoggingEventIdRanges.Workflows, 16), because: "the catalogue is 17000 to 17015, gap free");

        List<LogRecord> packageRecords = records.Where(record => declared.Contains(record.EventId.Id)).ToList();
        foreach (int eventId in declared)
        {
            packageRecords.ShouldHaveLoggedTemplate(new EventId(eventId), Templates[eventId]);
        }

        packageRecords.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Workflows, 0);
        packageRecords.ShouldFollowLogConvention();
        packageRecords.ShouldNotLeak(TestTenants.A.ToString(), "catalogue-compensation");
    }

    private static IOptions<TemporalOptions> AsOptions(TemporalOptions options) => Microsoft.Extensions.Options.Options.Create(options);
}
