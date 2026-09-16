// consumer-verify — exercises SharedKernel.Workflows.Temporal exactly as a downstream microservice
// would: real DI composition through ProjectReference (standing in for a packed NuGet reference — the
// compiled surface is identical either way), driven through a real Host.CreateApplicationBuilder() ->
// IHost.StartAsync() composition, never a bare BuildServiceProvider(). Four surfaces:
//   1. .AsClientOnly() — the shape most consuming services use — resolves IWorkflowDispatcher/
//      IWorkflowIdFactory/IWorkflowServiceProbe with zero DI exceptions; no IHostedService is
//      registered; ITemporalRawClientAccessor is unreachable without .AllowRawClientAccess() (P-04).
//   2. .AddWorkflow<T>().AddActivities<T>().WithWorker(...) reaches IHost.StartAsync() against a real
//      Temporalio.Testing.WorkflowEnvironment (never a live cluster, so this harness's pass/fail never
//      depends on external infrastructure); the hosted worker service is registered; ITemporalClient
//      resolves as a singleton; a full start -> activity -> result round trip actually completes (P-05).
//   3. A Workflows:Temporal section that is entirely ABSENT fails even more eagerly than the phase
//      spec's literal wording: TemporalWorkflowsBuilder.Build() reads TargetHost/Namespace directly off
//      the raw IConfigurationSection and throws InvalidOperationException synchronously — before the
//      IHost is ever constructed, let alone started. A section that is PRESENT but holds an invalid
//      value (an empty TargetHost) instead passes that eager check and is caught by TemporalOptions'
//      [Required] + ValidateOnStart() at IHost.StartAsync(), throwing OptionsValidationException and
//      naming the property — exactly the mechanism the phase spec describes (P-06).
//   4. A worker composed with zero workflows and zero activities fails at .Build() — proving
//      composition errors surface at startup and never silently defer to the worker's first poll (P-06).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Google.Protobuf;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Codec;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Health;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Activities;
using Temporalio.Api.Common.V1;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Converters;
using Temporalio.Testing;
using Temporalio.Workflows;

await Surface1_AsClientOnlyResolvesWithZeroDiExceptions();
await Surface2_WorkerHostingRoundTripAgainstWorkflowEnvironment();
await Surface3_ConfigValidationFailsAtStartupNotFirstDispatch();
Surface4_BuildTimeCompositionErrorsFailEagerly();
await Surface5_PayloadEncryptionOpacityAndCrossWorkflowIdRejection();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// ── Surface 1: .AsClientOnly() — P-04 ────────────────────────────────────────
static async Task Surface1_AsClientOnlyResolvesWithZeroDiExceptions()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Workflows:Temporal:TargetHost"] = "localhost:59999",
        ["Workflows:Temporal:Namespace"] = "default",
    });
    builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    builder.Services.AddSingleton<IClock, SystemClock>();

    builder.Services
        .AddSharedKernelTemporalWorkflows(builder.Configuration)
        .AsClientOnly()
        .Build();

    Verify(
        !builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)),
        "AsClientOnly() registers no IHostedService descriptor");

    using IHost host = builder.Build();
    // Exercises the real ValidateOnStart() path through a genuine IHost, not just BuildServiceProvider().
    await host.StartAsync();

    using (IServiceScope scope = host.Services.CreateScope())
    {
        _ = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
    }

    _ = host.Services.GetRequiredService<IWorkflowIdFactory>();
    _ = host.Services.GetRequiredService<IWorkflowServiceProbe>();

    ITemporalRawClientAccessor? rawAccessor = host.Services.GetService<ITemporalRawClientAccessor>();
    Verify(rawAccessor is null, "ITemporalRawClientAccessor does not resolve without AllowRawClientAccess()");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: .AsClientOnly() resolves IWorkflowDispatcher/IWorkflowIdFactory/IWorkflowServiceProbe " +
        "through a real IHost.StartAsync() with zero DI exceptions, registers no IHostedService, and the raw " +
        "client hatch stays closed without AllowRawClientAccess()");
}

// ── Surface 2: worker hosting against a real WorkflowEnvironment — P-05 ──────
static async Task Surface2_WorkerHostingRoundTripAgainstWorkflowEnvironment()
{
    await using WorkflowEnvironment environment = await WorkflowEnvironment.StartTimeSkippingAsync();
    const string taskQueue = "consumer-verify-workflows-queue";

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Workflows:Temporal:TargetHost"] = environment.Client.Connection.Options.TargetHost,
        ["Workflows:Temporal:Namespace"] = environment.Client.Options.Namespace,
    });
    builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    builder.Services.AddSingleton<IClock, SystemClock>();

    builder.Services
        .AddSharedKernelTemporalWorkflows(builder.Configuration)
        .AddWorkflow<ConsumerVerifyEchoWorkflow>()
        .AddActivities<ConsumerVerifyEchoActivity>()
        .WithWorker(taskQueue)
        .Build();

    Verify(
        builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)),
        "a worker-hosting composition registers at least one IHostedService");

    using IHost host = builder.Build();
    await host.StartAsync();

    ITemporalClient clientA = host.Services.GetRequiredService<ITemporalClient>();
    ITemporalClient clientB = host.Services.GetRequiredService<ITemporalClient>();
    Verify(ReferenceEquals(clientA, clientB), "ITemporalClient resolves as a singleton");

    using (IServiceScope scope = host.Services.CreateScope())
    {
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        var startOptions = new WorkflowStartOptions
        {
            TaskQueue = taskQueue,
            BusinessKey = Guid.NewGuid().ToString("n"),
            IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
            IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
        };

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<ConsumerVerifyEchoWorkflow, string, string>(
            "hello from consumer-verify",
            startOptions,
            TenantScope.Of("consumer-verify-tenant"));

        Verify(
            startResult.IsSuccess,
            $"workflow starts successfully: {(startResult.IsFailure ? startResult.Error.Message : string.Empty)}");

        Result<string> outcome = await startResult.Value.GetResultAsync();

        Verify(
            outcome.IsSuccess,
            $"workflow completes successfully: {(outcome.IsFailure ? outcome.Error.Message : string.Empty)}");
        Verify(
            outcome.Value == "HELLO FROM CONSUMER-VERIFY",
            "the workflow result round-trips through the activity correctly");
    }

    await host.StopAsync();
    Console.WriteLine(
        "Surface 2 PASS: worker-hosting composition reaches IHost.StartAsync() against a real " +
        "WorkflowEnvironment (never a live cluster), the hosted worker service is registered, " +
        "ITemporalClient resolves as a singleton, and a full start -> activity -> result round trip completes");
}

// ── Surface 3: config validation — P-06 ──────────────────────────────────────
static async Task Surface3_ConfigValidationFailsAtStartupNotFirstDispatch()
{
    // 3a — the Workflows:Temporal section is entirely ABSENT. TemporalWorkflowsBuilder.Build() reads
    // TargetHost/Namespace directly off the raw IConfigurationSection and throws synchronously — this
    // is even eagerer than "at IHost.StartAsync()": the IHost is never constructed at all, because the
    // consumer code never reaches builder.Build() for the host itself.
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        // Deliberately no Workflows:Temporal section at all.
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        builder.Services.AddSingleton<IClock, SystemClock>();

        InvalidOperationException? caught = null;
        try
        {
            builder.Services.AddSharedKernelTemporalWorkflows(builder.Configuration).AsClientOnly().Build();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        Verify(
            caught is not null,
            "an entirely-missing Workflows:Temporal:TargetHost fails at .Build() — before any IHost exists (no I/O was ever possible)");
        Verify(
            caught!.Message.Contains("TargetHost", StringComparison.Ordinal),
            "the .Build()-time exception names the missing TargetHost property");
    }

    // 3b — the section is PRESENT but TargetHost holds an invalid (empty) value. The eager .Build()
    // check only guards against an absent key (a null configuration read), so this passes .Build() and
    // is instead caught by TemporalOptions' [Required] + ValidateOnStart() at IHost.StartAsync() —
    // genuinely OptionsValidationException, genuinely at StartAsync(), naming the property.
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:Temporal:TargetHost"] = string.Empty,
            ["Workflows:Temporal:Namespace"] = "default",
        });
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        builder.Services.AddSingleton<IClock, SystemClock>();

        // Passes .Build() — the raw config read sees a non-null (empty) string.
        builder.Services.AddSharedKernelTemporalWorkflows(builder.Configuration).AsClientOnly().Build();

        using IHost host = builder.Build();

        OptionsValidationException? caught = null;
        try
        {
            await host.StartAsync();
        }
        catch (OptionsValidationException ex)
        {
            caught = ex;
        }

        Verify(
            caught is not null,
            "a present-but-empty TargetHost throws OptionsValidationException at IHost.StartAsync() — not a silent default, not a first-dispatch failure");
        Verify(
            caught!.Failures.Any(failure => failure.Contains("TargetHost", StringComparison.Ordinal)),
            "the OptionsValidationException message names the invalid TargetHost property");
    }

    Console.WriteLine(
        "Surface 3 PASS: an entirely-missing Workflows:Temporal config fails even before IHost exists; " +
        "a present-but-invalid value fails at IHost.StartAsync() via OptionsValidationException — both name the property");
}

// ── Surface 4: Build()-time composition errors — P-06 ────────────────────────
static void Surface4_BuildTimeCompositionErrorsFailEagerly()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Workflows:Temporal:TargetHost"] = "localhost:59999",
        ["Workflows:Temporal:Namespace"] = "default",
    });
    builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    builder.Services.AddSingleton<IClock, SystemClock>();

    InvalidOperationException? caught = null;
    try
    {
        builder.Services
            .AddSharedKernelTemporalWorkflows(builder.Configuration)
            .WithWorker("consumer-verify-empty-queue")
            .Build();
    }
    catch (InvalidOperationException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "a worker composed with zero workflows and zero activities fails at .Build()");
    Verify(
        !builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)),
        "the failed composition never registered a hosted worker service — no IHost was ever reached that could poll an empty queue");

    Console.WriteLine(
        "Surface 4 PASS: a worker with zero workflows and zero activities fails at .Build(), " +
        "never silently polling an empty task queue");
}

// ── Surface 5: payload encryption opacity + cross-WorkflowId AAD rejection — P-08/WO-081 ────
static async Task Surface5_PayloadEncryptionOpacityAndCrossWorkflowIdRejection()
{
    await using WorkflowEnvironment environment = await WorkflowEnvironment.StartTimeSkippingAsync();
    const string taskQueue = "consumer-verify-encrypted-queue";
    const string secretMarker = "consumer-verify-secret-9f3a2b";

    var services = new ServiceCollection();
    services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    services.AddSingleton<IClock, SystemClock>();

    IConfiguration configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:Temporal:TargetHost"] = environment.Client.Connection.Options.TargetHost,
            ["Workflows:Temporal:Namespace"] = environment.Client.Options.Namespace,
        })
        .Build();

    services.AddSingleton<IEncryptionKeyProvider>(
        new StaticEncryptionKeyProvider("v1", [new CryptographicKey("v1", Enumerable.Repeat((byte)7, 32).ToArray())]));
    services.AddSharedKernelCryptography(configuration).AddSymmetricEncryption();

    services
        .AddSharedKernelTemporalWorkflows(configuration)
        .AddWorkflow<ConsumerVerifyEchoWorkflow>()
        .AddActivities<ConsumerVerifyEchoActivity>()
        .WithWorker(taskQueue)
        .WithPayloadEncryption()
        .Build();

    await using ServiceProvider provider = services.BuildServiceProvider();
    List<IHostedService> hostedServices = [.. provider.GetServices<IHostedService>()];
    foreach (IHostedService hostedService in hostedServices)
    {
        await hostedService.StartAsync(CancellationToken.None);
    }

    Result<IWorkflowHandle<string>> startResult;
    using (IServiceScope scope = provider.CreateScope())
    {
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        startResult = await dispatcher.StartAsync<ConsumerVerifyEchoWorkflow, string, string>(
            secretMarker,
            new WorkflowStartOptions
            {
                TaskQueue = taskQueue,
                BusinessKey = $"encrypted-{Guid.NewGuid():N}",
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
                IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
            },
            TenantScope.Of("consumer-verify-encrypted-tenant"));
    }

    Verify(startResult.IsSuccess, "an encrypted workflow starts successfully through the public IWorkflowDispatcher surface");
    await startResult.Value.GetResultAsync();

    // Fetch the REAL captured history through the raw Temporal client — a public SDK type, exactly
    // what a downstream consumer could inspect for themselves via the Temporal CLI/Web UI.
    WorkflowHandle rawHandle = environment.Client.GetWorkflowHandle(startResult.Value.WorkflowId);
    WorkflowHistory history = await rawHandle.FetchHistoryAsync();

    Payload capturedInputPayload = history.Events
        .First(e => e.WorkflowExecutionStartedEventAttributes is not null)
        .WorkflowExecutionStartedEventAttributes.Input.Payloads_[0];

    string capturedAsLatin1 = System.Text.Encoding.Latin1.GetString(capturedInputPayload.ToByteArray());
    Verify(
        !capturedAsLatin1.Contains(secretMarker, StringComparison.Ordinal),
        "the encrypted argument is genuinely opaque in the REAL captured Temporal history — the secret marker never appears as plaintext");

    Verify(
        EncryptedPayload.TryParse(capturedInputPayload.Data.Span, out EncryptedPayload? capturedEncryptedPayload)
            && capturedEncryptedPayload.KeyId == "v1",
        "the captured payload's data is SharedKernel.Cryptography's canonical EncryptedPayload layout, carrying the key id");

    // Reconstruct the codec directly (internal access — see the production csproj's narrowly-scoped
    // InternalsVisibleTo grant to this harness) using the SAME ISymmetricEncryptionService this
    // composition registered, then prove the AAD binding against the REAL captured ciphertext above —
    // a stronger proof than a synthetic Payload, since it exercises exactly what a real cluster stored.
    var encryptionService = provider.GetRequiredService<ISymmetricEncryptionService>();
    var codecLogger = provider.GetRequiredService<ILogger<EncryptionPayloadCodec>>();
    var baseCodec = new EncryptionPayloadCodec(encryptionService, codecLogger);

    IPayloadCodec codecForTheRealWorkflowId = baseCodec.WithSerializationContext(
        new ISerializationContext.Workflow(environment.Client.Options.Namespace, startResult.Value.WorkflowId));
    IReadOnlyCollection<Payload> decodedUnderCorrectId = await codecForTheRealWorkflowId.DecodeAsync([capturedInputPayload]);
    string decodedText = System.Text.Encoding.UTF8.GetString(decodedUnderCorrectId.Single().Data.ToByteArray());
    Verify(
        decodedText.Contains(secretMarker, StringComparison.Ordinal),
        "the captured ciphertext decodes correctly under a codec bound to the SAME WorkflowId that produced it");

    IPayloadCodec codecForADifferentWorkflowId = baseCodec.WithSerializationContext(
        new ISerializationContext.Workflow(environment.Client.Options.Namespace, "a-completely-different-workflow-id"));

    InvalidOperationException? crossWorkflowIdFailure = null;
    try
    {
        await codecForADifferentWorkflowId.DecodeAsync([capturedInputPayload]);
    }
    catch (InvalidOperationException ex)
    {
        crossWorkflowIdFailure = ex;
    }

    Verify(
        crossWorkflowIdFailure is not null,
        "the SAME captured ciphertext genuinely fails to decode under a codec bound to a DIFFERENT WorkflowId — a captured payload can never be replayed against another execution");

    foreach (IHostedService hostedService in hostedServices)
    {
        await hostedService.StopAsync(CancellationToken.None);
    }

    Console.WriteLine(
        "Surface 5 PASS: an encrypted workflow argument is genuinely opaque in the REAL captured Temporal " +
        "history, decodes correctly under the codec bound to the WorkflowId that produced it, and genuinely " +
        "fails to decode under a codec bound to a different WorkflowId — proven against real captured " +
        "ciphertext, not a synthetic payload");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  - {label}");
}

// A minimal, deterministic sample workflow + activity used only by Surface 2's round trip. The
// activity is ordinary DI-resolved code (ActivityBase); the workflow takes no constructor
// dependencies and touches nothing but Workflow.* (WorkflowBase) — obeying the same determinism
// rules any consuming service's own workflows must obey.
internal sealed class ConsumerVerifyEchoActivity : ActivityBase
{
    public ConsumerVerifyEchoActivity(ILogger<ConsumerVerifyEchoActivity> logger, IClock clock)
        : base(logger, clock)
    {
    }

    // Explicitly named — Temporal derives an unnamed [Activity] method's registered name from the
    // METHOD's own name, never the declaring type, so an unnamed shared base method would collide
    // the moment a second CommandActivity<>-shaped type reused it. This activity has no shared base
    // method, but the explicit name is still required because WorkflowBase.ExecuteAsync dispatches by
    // typeof(TActivity).Name.
    [Activity(nameof(ConsumerVerifyEchoActivity))]
    public Task<string> RunAsync(string input) => Task.FromResult(input.ToUpperInvariant());
}

[Workflow]
internal sealed class ConsumerVerifyEchoWorkflow : WorkflowBase
{
    [WorkflowRun]
    public Task<string> RunAsync(string input) =>
        ExecuteAsync<ConsumerVerifyEchoActivity, string, string>(input);
}
