using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-16 (real-environment part) — an encrypted workflow argument is <b>not</b> present as plaintext
/// in the captured history payload; a control composition (encryption disabled) proves the technique
/// itself works by showing the marker DOES appear when encryption is off.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class EncryptedPayloadRealEnvironmentTests(TemporalTestFixture fixture) : IAsyncLifetime
{
    private const string EncryptedTaskQueue = "sk-workflows-tests-encrypted-queue";
    private const string PlainTaskQueue = "sk-workflows-tests-plain-queue";

    private ServiceProvider? _encryptedProvider;
    private ServiceProvider? _plainProvider;
    private List<IHostedService> _encryptedHostedServices = [];
    private List<IHostedService> _plainHostedServices = [];

    public async Task InitializeAsync()
    {
        IConfiguration BaseConfiguration() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflows:Temporal:TargetHost"] = fixture.Environment.Client.Connection.Options.TargetHost,
                ["Workflows:Temporal:Namespace"] = fixture.Environment.Client.Options.Namespace,
            })
            .Build();

        var encryptedServices = new ServiceCollection();
        encryptedServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        encryptedServices.AddSingleton<IClock>(new FakeClock());
        encryptedServices.AddSingleton<IEncryptionKeyProvider>(
            new StaticEncryptionKeyProvider("v1", [new CryptographicKey("v1", Enumerable.Repeat((byte)7, 32).ToArray())]));
        encryptedServices.AddSharedKernelCryptography(BaseConfiguration()).AddSymmetricEncryption();
        encryptedServices
            .AddSharedKernelTemporalWorkflows(BaseConfiguration())
            .AddWorkflow<EchoWorkflow>()
            .AddActivities<EchoActivity>()
            .WithWorker(EncryptedTaskQueue)
            .WithPayloadEncryption()
            .Build();
        _encryptedProvider = encryptedServices.BuildServiceProvider();
        _encryptedHostedServices = [.. _encryptedProvider.GetServices<IHostedService>()];
        foreach (IHostedService hostedService in _encryptedHostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        var plainServices = new ServiceCollection();
        plainServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        plainServices.AddSingleton<IClock>(new FakeClock());
        plainServices
            .AddSharedKernelTemporalWorkflows(BaseConfiguration())
            .AddWorkflow<EchoWorkflow>()
            .AddActivities<EchoActivity>()
            .WithWorker(PlainTaskQueue)
            .Build();
        _plainProvider = plainServices.BuildServiceProvider();
        _plainHostedServices = [.. _plainProvider.GetServices<IHostedService>()];
        foreach (IHostedService hostedService in _plainHostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (IHostedService hostedService in _encryptedHostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        foreach (IHostedService hostedService in _plainHostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        if (_encryptedProvider is not null)
        {
            await _encryptedProvider.DisposeAsync();
        }

        if (_plainProvider is not null)
        {
            await _plainProvider.DisposeAsync();
        }
    }

    private static WorkflowStartOptions Options(string taskQueue, string businessKey) => new()
    {
        TaskQueue = taskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    /// <summary>
    /// <see cref="WorkflowHistory.ToJson"/> renders every <c>bytes</c> field (including a
    /// <c>Payload</c>'s <c>data</c>) as base64 — protobuf's standard JSON mapping — so the RAW marker
    /// substring never appears in the JSON text either way, encrypted or not. The correct technique is
    /// to compute the exact base64 the default (unencrypted) JSON payload converter would have produced
    /// for this string argument, and check whether THAT substring is present.
    /// </summary>
    private static string ExpectedPlaintextPayloadBase64(string marker)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"\"{marker}\""));

    [Fact]
    public async Task EncryptedComposition_MarkerNeverAppearsAsPlaintext_InCapturedHistory()
    {
        const string secretMarker = "top-secret-marker-9f3a2b";

        using IServiceScope scope = _encryptedProvider!.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            secretMarker,
            Options(EncryptedTaskQueue, $"encrypted-{Guid.NewGuid():N}"),
            TenantScope.Of("tenant-encrypted"));
        startResult.IsSuccess.Should().BeTrue();
        await startResult.Value.GetResultAsync();

        WorkflowHandle rawHandle = fixture.Environment.Client.GetWorkflowHandle(startResult.Value.WorkflowId);
        WorkflowHistory history = await rawHandle.FetchHistoryAsync();

        string historyJson = history.ToJson();
        historyJson.Should().NotContain(secretMarker, because: "the raw marker must never appear even as literal text");
        historyJson.Should().NotContain(
            ExpectedPlaintextPayloadBase64(secretMarker),
            because: "an encrypted workflow argument must never be present as the plaintext-equivalent payload bytes in the captured history");
    }

    [Fact]
    public async Task ControlComposition_WithoutEncryption_MarkerDoesAppearAsPlaintext()
    {
        const string controlMarker = "control-plaintext-marker-4c1d";

        using IServiceScope scope = _plainProvider!.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            controlMarker,
            Options(PlainTaskQueue, $"plain-{Guid.NewGuid():N}"),
            TenantScope.Of("tenant-plain"));
        startResult.IsSuccess.Should().BeTrue();
        await startResult.Value.GetResultAsync();

        WorkflowHandle rawHandle = fixture.Environment.Client.GetWorkflowHandle(startResult.Value.WorkflowId);
        WorkflowHistory history = await rawHandle.FetchHistoryAsync();

        string historyJson = history.ToJson();
        historyJson.Should().Contain(
            ExpectedPlaintextPayloadBase64(controlMarker),
            because: "this is the companion control proving the plaintext-detection technique itself works — without encryption, the payload bytes ARE the plaintext");
    }
}
