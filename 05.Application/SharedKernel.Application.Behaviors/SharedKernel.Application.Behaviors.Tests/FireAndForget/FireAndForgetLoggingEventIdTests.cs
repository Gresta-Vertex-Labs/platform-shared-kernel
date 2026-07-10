using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.FireAndForget;

/// <summary>
/// Verifies (WO-041, T-62) that <see cref="ChannelFireAndForgetDispatcher"/>'s full-channel drop
/// log carries <c>EventId</c> 5110 and <see cref="FireAndForgetBackgroundConsumer"/>'s
/// handler-fault log carries <c>EventId</c> 5111, after the P-253 <c>[LoggerMessage]</c>
/// authoring-mechanism retrofit.
/// </summary>
/// <remarks>
/// <para>
/// A hand-rolled recording logger is used (rather than an NSubstitute mock) because
/// <c>[LoggerMessage]</c>-generated partial methods check <c>ILogger.IsEnabled(LogLevel)</c>
/// before calling <c>Log(...)</c> — the same rationale documented on
/// <c>StreamLoggingBehaviorTests</c>' <c>RecordingLogger&lt;T&gt;</c>.
/// </para>
/// <para>
/// <c>ChannelFireAndForgetDispatcher</c> is <see langword="internal"/> to
/// <c>SharedKernel.Application.Behaviors</c> — this test project cannot name it as a generic type
/// argument at compile time. <see cref="RecordingLogger{T}"/> is instead registered as the OPEN
/// generic <c>ILogger&lt;&gt;</c> mapping; the DI container closes it over the internal type at
/// runtime via ordinary generic-service resolution (accessibility restricts compile-time
/// references, not runtime generic instantiation), and every closed instance writes into the
/// shared <see cref="SharedRecordingSink"/>, keyed by category name captured from inside the
/// generic class body itself.
/// </para>
/// </remarks>
public sealed class FireAndForgetLoggingEventIdTests
{
    private static class SharedRecordingSink
    {
        public static readonly List<(string Category, LogLevel Level, EventId EventId, Exception? Ex)> Records = [];
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            SharedRecordingSink.Records.Add((typeof(T).Name, logLevel, eventId, exception));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed record TrackableCommand(string Id) : IFireAndForgetCommand;
    private sealed record ThrowingCommand(string Id) : IFireAndForgetCommand;

    private sealed class TrackableCommandHandler(SemaphoreSlim gate) : IRequestHandler<TrackableCommand, Result>
    {
        public Task<Result> Handle(TrackableCommand request, CancellationToken ct)
        {
            gate.Release();
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ThrowingCommandHandler(SemaphoreSlim gate) : IRequestHandler<ThrowingCommand, Result>
    {
        public Task<Result> Handle(ThrowingCommand request, CancellationToken ct)
        {
            gate.Release();
            throw new InvalidOperationException($"Handler for {request.Id} exploded.");
        }
    }

    [Fact]
    public async Task ChannelFireAndForgetDispatcher_ChannelFull_LogsWarningWithEventId5110()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetLoggingEventIdTests>());

        services.AddSharedKernelApplicationBehaviors()
            .AddFireAndForgetDispatch(opts =>
            {
                opts.Capacity = 1;
                opts.RejectionPolicy = FireAndForgetRejectionPolicy.DropAndLog;
            })
            .Build();

        // The builder wires its shared Channel<IFireAndForgetCommand> singleton with
        // BoundedChannelFullMode.DropOldest for the DropAndLog policy, under which TryWrite never
        // returns false (it drops the oldest item and succeeds instead) — so the full-channel branch
        // in ChannelFireAndForgetDispatcher.EnqueueAsync is unreachable through that exact wiring.
        // This test overrides the channel singleton (last registration wins) with
        // BoundedChannelFullMode.Wait, under which TryWrite legitimately returns false when full,
        // to exercise the log call site itself — an authoring-mechanism test, not a behavioral fix
        // to the builder's channel wiring, which is out of scope for this phase.
        services.AddSingleton(
            System.Threading.Channels.Channel.CreateBounded<IFireAndForgetCommand>(
                new System.Threading.Channels.BoundedChannelOptions(1)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                    SingleReader = true,
                }));

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();

        using var cts = new CancellationTokenSource();
        await dispatcher.EnqueueAsync(new TrackableCommand("fill"), cts.Token);
        await dispatcher.EnqueueAsync(new TrackableCommand("dropped"), cts.Token);

        SharedRecordingSink.Records.Should().Contain(
            r => r.Category == "ChannelFireAndForgetDispatcher" && r.Level == LogLevel.Warning && r.EventId.Id == 5110,
            "the full-channel drop log must carry EventId 5110");
    }

    [Fact]
    public async Task FireAndForgetBackgroundConsumer_HandlerFault_LogsErrorWithEventId5111()
    {
        var throwingGate = new SemaphoreSlim(0, 1);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));
        services.AddScoped<IRequestHandler<ThrowingCommand, Result>>(_ => new ThrowingCommandHandler(throwingGate));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetLoggingEventIdTests>());

        services.AddSharedKernelApplicationBehaviors()
            .AddFireAndForgetDispatch()
            .Build();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();
        var consumer = provider.GetRequiredService<IEnumerable<IHostedService>>()
            .OfType<FireAndForgetBackgroundConsumer>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);

        await dispatcher.EnqueueAsync(new ThrowingCommand("t1"), cts.Token);
        var faulted = await throwingGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        faulted.Should().BeTrue("consumer must have processed the throwing command");

        // Give the consumer's catch block a moment to record the log after the gate release.
        await Task.Delay(100, CancellationToken.None);

        await consumer.StopAsync(CancellationToken.None);

        SharedRecordingSink.Records.Should().Contain(
            r => r.Category == "FireAndForgetBackgroundConsumer" && r.Level == LogLevel.Error && r.EventId.Id == 5111,
            "the handler-fault log must carry EventId 5111");
    }
}
