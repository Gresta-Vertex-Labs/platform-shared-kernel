using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.FireAndForget;

/// <summary>
/// Verifies the fire-and-forget dispatch infrastructure (T-25, WO-039 P-238 end-to-end fix):
/// <list type="bullet">
///   <item>EnqueueAsync is immediate (non-blocking) — channel write returns without waiting for the handler.</item>
///   <item>The background consumer picks up the command and executes it via a scoped pipeline, wired exactly
///   per the documented <see cref="ApplicationBehaviorsBuilder.AddFireAndForgetDispatch"/> shape — including
///   the globally-registered <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>, which no longer
///   self-blocks the consumer's own internal dispatch (WO-039, P-238).</item>
///   <item>Handler exceptions are caught, logged, and do not terminate the consumer loop.</item>
///   <item>DropAndLog policy drops commands when the channel is full and does not throw.</item>
///   <item>Attempting to resolve <see cref="IFireAndForgetDispatcher"/> without registration throws.</item>
///   <item>A caller's direct <c>ISender.Send</c> of an <see cref="IFireAndForgetCommand"/> is still rejected.</item>
/// </list>
/// </summary>
public sealed class FireAndForgetDispatcherTests
{
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

    /// <summary>
    /// Builds a service provider wired exactly per the documented, real
    /// <see cref="ApplicationBehaviorsBuilder.AddFireAndForgetDispatch"/> shape — including the globally
    /// registered <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>. This replaces the prior
    /// <c>BuildConsumerDirectProvider</c> workaround (which wired the consumer manually, without the guard
    /// behavior, to sidestep the now-fixed self-blocking bug — WO-039, P-238).
    /// </summary>
    private static (ServiceProvider Provider, IFireAndForgetDispatcher Dispatcher, FireAndForgetBackgroundConsumer Consumer)
        BuildRealProvider(Action<ServiceCollection>? configureHandlers = null, Action<FireAndForgetOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

        configureHandlers?.Invoke(services);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetDispatcherTests>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddFireAndForgetDispatch(configureOptions)
            .Build();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();
        var consumer = provider.GetRequiredService<IEnumerable<IHostedService>>()
            .OfType<FireAndForgetBackgroundConsumer>()
            .Single();

        return (provider, dispatcher, consumer);
    }

    // ---- test: EnqueueAsync is immediate ----

    [Fact]
    public async Task EnqueueAsync_ReturnsImmediately_WithoutWaitingForHandlerCompletion()
    {
        using var cts = new CancellationTokenSource();
        var (_, dispatcher, _) = BuildRealProvider(configureOptions: opts => opts.Capacity = 100);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // No consumer running in this test — TryWrite succeeds immediately and EnqueueAsync returns
        // without waiting for handler execution.
        await dispatcher.EnqueueAsync(new TrackableCommand("fast"), cts.Token);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "EnqueueAsync must return immediately — it should not wait for handler execution");
    }

    // ---- test: background consumer executes the command end-to-end via the real, documented wiring ----

    [Fact]
    public async Task BackgroundConsumer_ExecutesEnqueuedCommand_ViaRealDocumentedWiring()
    {
        var handlerGate = new SemaphoreSlim(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (_, dispatcher, consumer) = BuildRealProvider(services =>
        {
            services.AddSingleton(handlerGate);
            services.AddScoped<IRequestHandler<TrackableCommand, Result>>(sp =>
                new TrackableCommandHandler(sp.GetRequiredService<SemaphoreSlim>()));
        });

        await consumer.StartAsync(cts.Token);

        // Dispatch via the real IFireAndForgetDispatcher — exercises the full documented path, including
        // FireAndForgetGuardBehavior<,> registered globally by AddFireAndForgetDispatch(). Prior to the
        // WO-039 P-238 fix, the guard self-blocked the consumer's own internal ISender.Send and this
        // command would never actually execute.
        await dispatcher.EnqueueAsync(new TrackableCommand("exec"), cts.Token);

        var completed = await handlerGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        completed.Should().BeTrue(
            "the background consumer must actually execute the handler through the real, documented AddFireAndForgetDispatch() wiring");

        await consumer.StopAsync(CancellationToken.None);
    }

    // ---- test: exception in handler is logged and loop continues ----

    [Fact]
    public async Task BackgroundConsumer_HandlerException_IsLoggedAndLoopContinues()
    {
        var throwingGate = new SemaphoreSlim(0, 1);
        var successGate = new SemaphoreSlim(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (_, dispatcher, consumer) = BuildRealProvider(services =>
        {
            services.AddScoped<IRequestHandler<ThrowingCommand, Result>>(
                _ => new ThrowingCommandHandler(throwingGate));
            services.AddScoped<IRequestHandler<TrackableCommand, Result>>(
                _ => new TrackableCommandHandler(successGate));
        });

        await consumer.StartAsync(cts.Token);

        // First: throwing command.
        await dispatcher.EnqueueAsync(new ThrowingCommand("t1"), cts.Token);
        var throwing = await throwingGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        throwing.Should().BeTrue("consumer must have tried to process the throwing command");

        // Second: success command — proves the loop continued after the exception.
        await dispatcher.EnqueueAsync(new TrackableCommand("t2"), cts.Token);
        var success = await successGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        success.Should().BeTrue("consumer loop must continue after handler exception");

        await consumer.StopAsync(CancellationToken.None);
    }

    // ---- test: DropAndLog policy on full channel ----

    [Fact]
    public async Task EnqueueAsync_ChannelFull_DropAndLogPolicy_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        var (_, dispatcher, _) = BuildRealProvider(configureOptions: opts =>
        {
            opts.Capacity = 1;
            opts.RejectionPolicy = FireAndForgetRejectionPolicy.DropAndLog;
        });

        // Fill the channel (capacity=1) — no consumer running in this test.
        await dispatcher.EnqueueAsync(new TrackableCommand("fill"), cts.Token);

        // Second enqueue: channel is full → must drop silently (no exception).
        var act = async () => await dispatcher.EnqueueAsync(new TrackableCommand("dropped"), cts.Token);

        await act.Should().NotThrowAsync("DropAndLog policy must silently drop when the channel is full");
    }

    // ---- test: IFireAndForgetDispatcher not registered without AddFireAndForgetDispatch ----

    [Fact]
    public void IFireAndForgetDispatcher_NotRegistered_WhenAddFireAndForgetDispatchNotCalled()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetDispatcherTests>());
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IFireAndForgetDispatcher>();

        act.Should().Throw<InvalidOperationException>(
            "IFireAndForgetDispatcher must not be registered without AddFireAndForgetDispatch()");
    }

    // ---- test: a caller's direct ISender.Send is still rejected (WO-039, P-238 — guard not weakened) ----

    [Fact]
    public async Task GuardBehavior_RejectsDirectSenderSend_EvenAfterTrustedDispatchFix()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (provider, _, _) = BuildRealProvider(services =>
        {
            services.AddScoped<IRequestHandler<TrackableCommand, Result>>(
                _ => new TrackableCommandHandler(new SemaphoreSlim(0, 1)));
        });

        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TrackableCommand("direct"), cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an external caller's direct ISender.Send of an IFireAndForgetCommand must still be rejected");
    }
}
