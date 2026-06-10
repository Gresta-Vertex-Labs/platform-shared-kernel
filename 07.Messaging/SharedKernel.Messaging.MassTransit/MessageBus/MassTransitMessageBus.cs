using MassTransit;
using MassTransit.Courier.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// MassTransit implementation of <see cref="IMessageBus"/>.
/// Delegates publish to <see cref="IPublishEndpoint"/> and send to <see cref="ISendEndpointProvider"/>
/// with convention-based endpoint resolution, supporting per-type route overrides.
/// </summary>
internal sealed class MassTransitMessageBus : IMessageBus
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<Type, string> _routeMap;
    private readonly ConventionSendEndpointResolver _resolver;

    public MassTransitMessageBus(
        IPublishEndpoint publishEndpoint,
        ISendEndpointProvider sendEndpointProvider,
        IServiceProvider serviceProvider,
        IReadOnlyDictionary<Type, string> routeMap,
        ConventionSendEndpointResolver resolver)
    {
        _publishEndpoint = publishEndpoint;
        _sendEndpointProvider = sendEndpointProvider;
        _serviceProvider = serviceProvider;
        _routeMap = routeMap;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct) where T : class
    {
        // HP-03: Run propagators first with no explicit configure callback.
        var ctx = BuildContextFromPropagators(configure: null);

        if (ctx is null)
            return _publishEndpoint.Publish(message, ct);

        return _publishEndpoint.Publish(message, pipe =>
        {
            if (ctx.CorrelationId.HasValue)
                pipe.CorrelationId = ctx.CorrelationId.Value;

            foreach (var (key, value) in ctx.Headers)
                pipe.Headers.Set(key, value);
        }, ct);
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, Action<MessagingPublishContext> configure, CancellationToken ct) where T : class
    {
        // HP-03: Propagators run first; explicit configure callback runs after (explicit wins on same key).
        // BuildContextFromPropagators always returns non-null when configure is non-null.
        var ctx = BuildContextFromPropagators(configure)!;

        return _publishEndpoint.Publish(message, pipe =>
        {
            if (ctx.CorrelationId.HasValue)
                pipe.CorrelationId = ctx.CorrelationId.Value;

            foreach (var (key, value) in ctx.Headers)
                pipe.Headers.Set(key, value);
        }, ct);
    }

    /// Builds a <see cref="MessagingPublishContext"/> by running all registered propagators first,
    /// then applying the optional explicit configure callback (which wins on key conflicts).
    private MessagingPublishContext? BuildContextFromPropagators(Action<MessagingPublishContext>? configure)
    {
        var propagators = _serviceProvider.GetService<IEnumerable<IMessageHeaderPropagator>>();
        var hasPropagators = propagators is not null;
        var hasExplicit = configure is not null;

        if (!hasPropagators && !hasExplicit)
            return null;

        var ctx = new MessagingPublishContext();

        // Propagators run first — their values can be overridden by the explicit callback.
        if (propagators is not null)
        {
            foreach (var propagator in propagators)
                propagator.Propagate(ctx);
        }

        // Explicit callback runs last — its values overwrite anything set by propagators.
        configure?.Invoke(ctx);

        return ctx;
    }

    /// <inheritdoc />
    public async Task SendAsync<T>(T command, CancellationToken ct) where T : class
    {
        // Check per-type route override first; fall back to convention-based resolver.
        var queueName = _routeMap.TryGetValue(typeof(T), out var route)
            ? route
            : _resolver.Resolve<T>();
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}")).ConfigureAwait(false);
        await endpoint.Send(command, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        // Use IServiceProvider to resolve IRequestClient<TRequest> via MassTransit DI integration.
        // The CancellationToken is passed via the ct parameter — callers must pass a timeout-bound token.
        var client = _serviceProvider.CreateRequestClient<TRequest>();
        var response = await client.GetResponse<TResponse>(request, ct).ConfigureAwait(false);
        return response.Message;
    }

    /// <inheritdoc />
    public async Task ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)
    {
        if (routingSlip is not RoutingSlip slip)
            throw new ArgumentException(
                $"The routingSlip argument must be a {typeof(RoutingSlip).FullName} produced by " +
                $"{typeof(SharedKernel.Messaging.Abstractions.RoutingSlips.IRoutingSlipBuilder).FullName}.Build(). " +
                $"Received: {routingSlip?.GetType().FullName ?? "null"}.",
                nameof(routingSlip));

        if (slip.Itinerary.Count == 0)
            throw new ArgumentException(
                "The routingSlip argument has an empty itinerary. " +
                "Add at least one activity via IRoutingSlipBuilder.AddActivity() before calling Build().",
                nameof(routingSlip));

        // Courier dispatch: send the routing slip to the first activity's execute address.
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(slip.Itinerary[0].Address).ConfigureAwait(false);
        await endpoint.Send<RoutingSlip>(slip, ct).ConfigureAwait(false);
    }
}
