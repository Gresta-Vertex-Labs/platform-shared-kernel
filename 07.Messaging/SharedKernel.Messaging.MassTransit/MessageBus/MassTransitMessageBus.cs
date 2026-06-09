using MassTransit;
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
    public Task PublishAsync<T>(T message, CancellationToken ct) where T : class =>
        _publishEndpoint.Publish(message, ct);

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, Action<MessagingPublishContext> configure, CancellationToken ct) where T : class
    {
        var ctx = new MessagingPublishContext();
        configure(ctx);

        return _publishEndpoint.Publish(message, pipe =>
        {
            if (ctx.CorrelationId.HasValue)
                pipe.CorrelationId = ctx.CorrelationId.Value;

            foreach (var (key, value) in ctx.Headers)
                pipe.Headers.Set(key, value);
        }, ct);
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
}
