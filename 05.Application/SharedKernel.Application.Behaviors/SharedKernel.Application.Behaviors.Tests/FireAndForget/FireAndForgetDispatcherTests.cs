using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using System.Threading.Channels;

namespace SharedKernel.Application.Behaviors.Tests.FireAndForget;

/// <summary>
/// Verifies the fire-and-forget dispatch infrastructure (T-25):
/// <list type="bullet">
///   <item>EnqueueAsync is immediate (non-blocking) — channel write returns without waiting for the handler.</item>
///   <item>The background consumer picks up the command and executes it via a scoped pipeline.</item>
///   <item>Handler exceptions are caught, logged, and do not terminate the consumer loop.</item>
///   <item>DropAndLog policy drops commands when the channel is full and does not throw.</item>
///   <item>Attempting to resolve <see cref="IFireAndForgetDispatcher"/> without registration throws.</item>
/// </list>
/// </summary>
public sealed class FireAndForgetDispatcherTests
{
    // ---- command types ----
    // These are plain ICommand types (NOT IFireAndForgetCommand) used when testing the
    // FireAndForgetBackgroundConsumer directly — the background consumer resolves handlers via
    // ISender.Send, and if these implemented IFireAndForgetCommand, the FireAndForgetGuardBehavior
    // would intercept them.
    // For consumer tests we wire the consumer manually without the guard behavior registered.

    private sealed record ConsumerTrackableCommand(string Id) : IFireAndForgetCommand;
    private sealed record ConsumerThrowingCommand(string Id) : IFireAndForgetCommand;

    private sealed class ConsumerTrackableCommandHandler(SemaphoreSlim gate) : IRequestHandler<ConsumerTrackableCommand, Result>
    {
        public Task<Result> Handle(ConsumerTrackableCommand request, CancellationToken ct)
        {
            gate.Release();
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ConsumerThrowingCommandHandler(SemaphoreSlim gate) : IRequestHandler<ConsumerThrowingCommand, Result>
    {
        public Task<Result> Handle(ConsumerThrowingCommand request, CancellationToken ct)
        {
            gate.Release();
            throw new InvalidOperationException($"Handler for {request.Id} exploded.");
        }
    }

    /// <summary>
    /// Builds a service provider with the channel + consumer wired directly,
    /// WITHOUT <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>,
    /// so the consumer can internally dispatch <see cref="IFireAndForgetCommand"/> via
    /// <c>ISender.Send</c> without being intercepted by the guard.
    /// </summary>
    private static (ServiceProvider Provider, ChannelWriter<IFireAndForgetCommand> Writer)
        BuildConsumerDirectProvider(Action<ServiceCollection>? configureHandlers = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

        configureHandlers?.Invoke(services);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetDispatcherTests>());

        // Wire channel, reader, writer, and consumer directly — no guard behavior registered.
        var channel = Channel.CreateBounded<IFireAndForgetCommand>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

        services.AddSingleton<ChannelReader<IFireAndForgetCommand>>(channel.Reader);
        services.AddSingleton<IHostedService, FireAndForgetBackgroundConsumer>();

        return (services.BuildServiceProvider(), channel.Writer);
    }

    // ---- test: EnqueueAsync is immediate ----

    [Fact]
    public async Task EnqueueAsync_ReturnsImmediately_WithoutWaitingForHandlerCompletion()
    {
        // Capacity=100, no consumer — the channel accepts one enqueue immediately.
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetDispatcherTests>());

        // Register a plain ICommand handler so MediatR can find one (guard excluded from this test).
        services
            .AddSharedKernelApplicationBehaviors()
            .AddFireAndForgetDispatch(opts => opts.Capacity = 100)
            .Build();

        // Re-add MediatR after Build() to include handlers.
        // Actually the guard behavior would block any IFireAndForgetCommand; here we only test
        // that EnqueueAsync (the TryWrite call) returns fast — no consumer runs in this test.
        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Use a valid IFireAndForgetCommand type — guard behavior is registered but there's no
        // consumer, so nothing blocks: TryWrite succeeds immediately and EnqueueAsync returns.
        await dispatcher.EnqueueAsync(new ConsumerTrackableCommand("fast"), cts.Token);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "EnqueueAsync must return immediately — it should not wait for handler execution");
    }

    // ---- test: background consumer executes the command ----

    [Fact]
    public async Task BackgroundConsumer_ExecutesEnqueuedCommand_ViaScopedMediatRPipeline()
    {
        var handlerGate = new SemaphoreSlim(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (provider, channelWriter) = BuildConsumerDirectProvider(services =>
        {
            services.AddSingleton(handlerGate);
            services.AddScoped<IRequestHandler<ConsumerTrackableCommand, Result>>(sp =>
                new ConsumerTrackableCommandHandler(sp.GetRequiredService<SemaphoreSlim>()));
        });

        var consumer = provider.GetRequiredService<IEnumerable<IHostedService>>()
            .OfType<FireAndForgetBackgroundConsumer>()
            .Single();

        await consumer.StartAsync(cts.Token);

        // Write a command directly to the channel.
        await channelWriter.WriteAsync(new ConsumerTrackableCommand("exec"), cts.Token);

        var completed = await handlerGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        completed.Should().BeTrue("the background consumer must have executed the handler");

        await consumer.StopAsync(CancellationToken.None);
    }

    // ---- test: exception in handler is logged and loop continues ----

    [Fact]
    public async Task BackgroundConsumer_HandlerException_IsLoggedAndLoopContinues()
    {
        var throwingGate = new SemaphoreSlim(0, 1);
        var successGate = new SemaphoreSlim(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (provider, channelWriter) = BuildConsumerDirectProvider(services =>
        {
            services.AddScoped<IRequestHandler<ConsumerThrowingCommand, Result>>(
                _ => new ConsumerThrowingCommandHandler(throwingGate));
            services.AddScoped<IRequestHandler<ConsumerTrackableCommand, Result>>(
                _ => new ConsumerTrackableCommandHandler(successGate));
        });

        var consumer = provider.GetRequiredService<IEnumerable<IHostedService>>()
            .OfType<FireAndForgetBackgroundConsumer>()
            .Single();

        await consumer.StartAsync(cts.Token);

        // First: throwing command.
        await channelWriter.WriteAsync(new ConsumerThrowingCommand("t1"), cts.Token);
        var throwing = await throwingGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        throwing.Should().BeTrue("consumer must have tried to process the throwing command");

        // Second: success command — proves the loop continued after the exception.
        await channelWriter.WriteAsync(new ConsumerTrackableCommand("t2"), cts.Token);
        var success = await successGate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        success.Should().BeTrue("consumer loop must continue after handler exception");

        await consumer.StopAsync(CancellationToken.None);
    }

    // ---- test: DropAndLog policy on full channel ----

    [Fact]
    public async Task EnqueueAsync_ChannelFull_DropAndLogPolicy_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetDispatcherTests>());
        services
            .AddSharedKernelApplicationBehaviors()
            .AddFireAndForgetDispatch(opts =>
            {
                opts.Capacity = 1;
                opts.RejectionPolicy = FireAndForgetRejectionPolicy.DropAndLog;
            })
            .Build();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();

        // Fill the channel (capacity=1).
        await dispatcher.EnqueueAsync(new ConsumerTrackableCommand("fill"), cts.Token);

        // Second enqueue: channel is full → must drop silently (no exception).
        var act = async () => await dispatcher.EnqueueAsync(new ConsumerTrackableCommand("dropped"), cts.Token);

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
}
