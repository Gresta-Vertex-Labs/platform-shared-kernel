using MassTransit;
using MassTransit.Courier.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Diagnostics;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// MassTransit implementation of <see cref="IMessageBus"/>.
/// Delegates publish to <see cref="IPublishEndpoint"/> and send to <see cref="ISendEndpointProvider"/>
/// with convention-based endpoint resolution, supporting per-type route overrides.
/// <see cref="SendAsync{T}"/>, <see cref="RequestAsync{TRequest, TResponse}"/>, and
/// <see cref="ExecuteRoutingSlipAsync"/> each start their own child <see cref="System.Diagnostics.Activity"/>
/// via <see cref="MessagingDiagnostics.ActivitySource"/> (P-348/WO-054); <see cref="PublishAsync{T}(T, CancellationToken)"/>
/// increments <see cref="MessagingDiagnostics.PublishCounter"/> on successful publish.
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
    public async Task PublishAsync<T>(T message, CancellationToken ct) where T : class
    {
        // HP-03: Run propagators first with no explicit configure callback.
        var ctx = BuildContextFromPropagators(configure: null);

        if (ctx is null)
        {
            await _publishEndpoint.Publish(message, ct).ConfigureAwait(false);
        }
        else
        {
            await _publishEndpoint.Publish(message, pipe =>
            {
                if (ctx.CorrelationId.HasValue)
                    pipe.CorrelationId = ctx.CorrelationId.Value;

                foreach (var (key, value) in ctx.Headers)
                    pipe.Headers.Set(key, value);

                // P-344/WO-054: maps to RabbitMQ routing-key affinity / Azure Service Bus session identity.
                pipe.ApplyPartitionKey(ctx.PartitionKey);
            }, ct).ConfigureAwait(false);
        }

        // P-348/WO-054: incremented only after the publish call above completes without throwing.
        MessagingDiagnostics.PublishCounter.Add(1, new KeyValuePair<string, object?>("messaging.message_type", typeof(T).Name));
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(T message, Action<MessagingPublishContext> configure, CancellationToken ct) where T : class
    {
        // HP-03: Propagators run first; explicit configure callback runs after (explicit wins on same key).
        // BuildContextFromPropagators always returns non-null when configure is non-null.
        var ctx = BuildContextFromPropagators(configure)!;

        await _publishEndpoint.Publish(message, pipe =>
        {
            if (ctx.CorrelationId.HasValue)
                pipe.CorrelationId = ctx.CorrelationId.Value;

            foreach (var (key, value) in ctx.Headers)
                pipe.Headers.Set(key, value);

            // P-344/WO-054: maps to RabbitMQ routing-key affinity / Azure Service Bus session identity.
            pipe.ApplyPartitionKey(ctx.PartitionKey);
        }, ct).ConfigureAwait(false);

        // P-348/WO-054: incremented only after the publish call above completes without throwing.
        MessagingDiagnostics.PublishCounter.Add(1, new KeyValuePair<string, object?>("messaging.message_type", typeof(T).Name));
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
        // P-348/WO-054: closes the completeness gap — prior to this phase, SendAsync produced
        // no activity at all. Disposed after the send call completes or throws.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("MessageBus.Send");
        activity?.SetTag("messaging.message_type", typeof(T).Name);

        // Check per-type route override first; fall back to convention-based resolver.
        var queueName = _routeMap.TryGetValue(typeof(T), out var route)
            ? route
            : _resolver.Resolve<T>();
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}")).ConfigureAwait(false);

        // P-341: Run registered propagators before dispatch, identical precedence to PublishAsync.
        // SendAsync has no Action<PublishContext> overload, so "explicit callback" reduces to "none" —
        // propagator output alone determines CorrelationId/headers here.
        var ctx = BuildContextFromPropagators(configure: null);

        if (ctx is null)
        {
            await endpoint.Send(command, ct).ConfigureAwait(false);
            return;
        }

        await endpoint.Send(command, pipe =>
        {
            if (ctx.CorrelationId.HasValue)
                pipe.CorrelationId = ctx.CorrelationId.Value;

            foreach (var (key, value) in ctx.Headers)
                pipe.Headers.Set(key, value);

            // P-344/WO-054: maps to RabbitMQ routing-key affinity / Azure Service Bus session identity.
            pipe.ApplyPartitionKey(ctx.PartitionKey);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        // P-348/WO-054: closes the completeness gap — prior to this phase, RequestAsync produced
        // no activity at all. Disposed after the request/response round-trip completes or throws.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("MessageBus.Request");
        activity?.SetTag("messaging.request_type", typeof(TRequest).Name);
        activity?.SetTag("messaging.response_type", typeof(TResponse).Name);

        // P-341: Run registered propagators before dispatch, identical precedence to PublishAsync/SendAsync.
        var ctx = BuildContextFromPropagators(configure: null);

        // Use IServiceProvider to resolve IRequestClient<TRequest> via MassTransit DI integration.
        // The CancellationToken is passed via the ct parameter — callers must pass a timeout-bound token.
        var client = _serviceProvider.CreateRequestClient<TRequest>();

        if (ctx is null)
        {
            var response = await client.GetResponse<TResponse>(request, ct).ConfigureAwait(false);
            return response.Message;
        }

        // IRequestClient<TRequest>.GetResponse does not accept the raw Action<SendContext<T>> pipe
        // shape Send/Publish use — it exposes an IRequestPipeConfigurator<TRequest> callback instead.
        // UseExecute() adds a synchronous execute filter over the underlying SendContext<TRequest>,
        // giving the same CorrelationId/Headers access as the Send/Publish pipe callbacks.
        var propagatedResponse = await client.GetResponse<TResponse>(request, requestPipeConfigurator =>
        {
            requestPipeConfigurator.UseExecute(sendContext =>
            {
                if (ctx.CorrelationId.HasValue)
                    sendContext.CorrelationId = ctx.CorrelationId.Value;

                foreach (var (key, value) in ctx.Headers)
                    sendContext.Headers.Set(key, value);
            });
        }, ct).ConfigureAwait(false);

        return propagatedResponse.Message;
    }

    /// <inheritdoc />
    public async Task ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)
    {
        // P-348/WO-054: closes the completeness gap — prior to this phase, ExecuteRoutingSlipAsync
        // produced no activity at all. Disposed after dispatch completes or throws, including the
        // argument-validation throws below (the activity_count tag is populated once the itinerary
        // is known — it cannot be set on the "not a RoutingSlip at all" throw path).
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("MessageBus.ExecuteRoutingSlip");

        if (routingSlip is not RoutingSlip slip)
            throw new ArgumentException(
                $"The routingSlip argument must be a {typeof(RoutingSlip).FullName} produced by " +
                $"{typeof(SharedKernel.Messaging.Abstractions.RoutingSlips.IRoutingSlipBuilder).FullName}.Build(). " +
                $"Received: {routingSlip?.GetType().FullName ?? "null"}.",
                nameof(routingSlip));

        activity?.SetTag("messaging.routing_slip.activity_count", slip.Itinerary.Count);

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
