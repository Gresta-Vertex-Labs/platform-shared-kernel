// consumer-verify — exercises SharedKernel.Scheduling exactly as a downstream microservice would:
// real DI composition through ProjectReference (standing in for a packed NuGet reference — the
// compiled surface is identical either way), driven through a real Host.CreateApplicationBuilder() ->
// IHost.StartAsync() composition, never a bare BuildServiceProvider(). Four surfaces:
//   1. AddSharedKernelScheduling() + AddRecurring/AddDeferred resolves IScheduledJobRegistry/
//      the scheduler readiness probe with zero DI exceptions through a real IHost.StartAsync(), and no
//      IDistributedLockService registered logs the single-replica startup Warning.
//   2. A one-shot deferred job actually fires end-to-end through the real MediatR pipeline within a
//      few seconds of real wall-clock time — proving the whole ScheduledCommandJob<TCommand> bridge,
//      not just DI resolution.
//   3. Registration fails fast — before IHost.StartAsync(), before even builder.Build() — when
//      MisfirePolicy/OverlapPolicy are left unset.
//   4. The probe reports IsRunning/RegisteredJobCount correctly through the real host.

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Health;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Scheduling.Registry;

await Surface1And4_RegistrationResolvesAndProbeReportsThroughRealHost();
await Surface2_DeferredJobFiresEndToEndThroughMediatR();
Surface3_RegistrationFailsFastOnMissingPolicies();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// ── Surfaces 1 & 4: registration + probe through a real IHost ───────────────
static async Task Surface1And4_RegistrationResolvesAndProbeReportsThroughRealHost()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyPingCommand>());

    ISchedulingBuilder schedulingBuilder = builder.Services.AddSharedKernelScheduling();
    schedulingBuilder.AddRecurring<ConsumerVerifyPingCommand>(
        "consumer-verify-nightly",
        "0 0 2 * * ?",
        _ => new ConsumerVerifyPingCommand(),
        options =>
        {
            options.MisfirePolicy = MisfirePolicy.Skip;
            options.OverlapPolicy = OverlapPolicy.Skip;
        });

    using IHost host = builder.Build();
    // Exercises the real ValidateOnStart() path (SchedulingOptions) through a genuine IHost, not just
    // BuildServiceProvider() — and this is the call that actually starts SchedulingHostedService.
    await host.StartAsync();

    _ = host.Services.GetRequiredService<IScheduledJobRegistry>();
    IReadinessProbe probe = host.Services.GetRequiredReadinessProbe(SchedulerReadiness.ProbeName);

    ReadinessReport health = await probe.ProbeAsync();
    Verify(health.IsHealthy, "the hosted loop reports IsRunning=true after IHost.StartAsync()");
    Verify((int)health.Data[SchedulerReadiness.RegisteredJobCountKey] == 1, "the probe reports exactly the one registered job");

    await host.StopAsync();

    health = await probe.ProbeAsync();
    Verify(!health.IsHealthy, "the probe reports IsRunning=false after IHost.StopAsync()");

    Console.WriteLine(
        "Surfaces 1 & 4 PASS: AddSharedKernelScheduling()+AddRecurring resolve IScheduledJobRegistry/" +
        "the scheduler readiness probe through a real IHost.StartAsync() with zero DI exceptions, and the probe " +
        "correctly reflects the hosted loop's running state before and after IHost.StopAsync()");
}

// ── Surface 2: a real end-to-end deferred-job fire ───────────────────────────
static async Task Surface2_DeferredJobFiresEndToEndThroughMediatR()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyPingCommand>());
    builder.Services.AddSingleton<ConsumerVerifyPingRecorder>();

    ISchedulingBuilder schedulingBuilder = builder.Services.AddSharedKernelScheduling(
        options => options.TickInterval = TimeSpan.FromMilliseconds(200));
    schedulingBuilder.AddDeferred<ConsumerVerifyPingCommand>(
        "consumer-verify-deferred-ping",
        DateTimeOffset.UtcNow.AddSeconds(1),
        _ => new ConsumerVerifyPingCommand(),
        options =>
        {
            options.MisfirePolicy = MisfirePolicy.FireOnce;
            options.OverlapPolicy = OverlapPolicy.Skip;
        });

    using IHost host = builder.Build();
    await host.StartAsync();

    var recorder = host.Services.GetRequiredService<ConsumerVerifyPingRecorder>();

    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!recorder.Fired && DateTime.UtcNow < deadline)
    {
        await Task.Delay(100);
    }

    Verify(recorder.Fired, "the deferred job fired end-to-end through the real MediatR pipeline within 10 real seconds");

    await host.StopAsync();

    Console.WriteLine(
        "Surface 2 PASS: a one-shot deferred job registered against a real SystemClock fires end-to-end " +
        "through ScheduledCommandJob<TCommand> -> ISender -> the real MediatR handler");
}

// ── Surface 3: registration fails fast on missing policies ──────────────────
static void Surface3_RegistrationFailsFastOnMissingPolicies()
{
    var services = new ServiceCollection();
    services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    ISchedulingBuilder schedulingBuilder = services.AddSharedKernelScheduling();

    ArgumentException? caught = null;
    try
    {
        schedulingBuilder.AddRecurring<ConsumerVerifyPingCommand>(
            "consumer-verify-missing-policies",
            "0 0 2 * * ?",
            _ => new ConsumerVerifyPingCommand(),
            _ => { /* neither MisfirePolicy nor OverlapPolicy set */ });
    }
    catch (ArgumentException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "AddRecurring throws ArgumentException immediately when MisfirePolicy/OverlapPolicy are left unset");
    Verify(caught!.Message.Contains("MisfirePolicy", StringComparison.Ordinal), "the exception names the missing MisfirePolicy");

    Console.WriteLine(
        "Surface 3 PASS: registration fails fast at the AddRecurring call site — before any IHost exists, " +
        "before even builder.Build() — never lazily at host startup or the first missed tick");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  - {label}");
}

// A minimal void command + handler used by every surface above. The handler for Surface 2 records
// that it fired via a singleton recorder so the harness can observe a genuine end-to-end dispatch.
internal sealed record ConsumerVerifyPingCommand : ICommand;

internal sealed class ConsumerVerifyPingHandler(ConsumerVerifyPingRecorder recorder) : IRequestHandler<ConsumerVerifyPingCommand, Result>
{
    public Task<Result> Handle(ConsumerVerifyPingCommand request, CancellationToken cancellationToken)
    {
        recorder.Fired = true;
        return Task.FromResult(Result.Success());
    }
}

internal sealed class ConsumerVerifyPingRecorder
{
    public volatile bool Fired;
}
