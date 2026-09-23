using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

internal sealed class MassTransitMessageBus : IMessageBus
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly IReadOnlyList<IMessageHeaderPropagator> _propagators;
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
        _routeMap = routeMap;
        _resolver = resolver;

        // Resolved once per scope rather than per dispatch; an empty set is the common case.
        // GetService (not GetServices/GetRequiredService): a real Microsoft.Extensions.DependencyInjection
        // container always resolves IEnumerable<T> to at least an empty sequence, but a substituted
        // IServiceProvider — which several tests inject — returns null for anything unregistered, and
        // the throwing overloads turn that into a constructor failure.
        _propagators = [.. serviceProvider.GetService<IEnumerable<IMessageHeaderPropagator>>() ?? []];
    }

    public Task<Result> PublishAsync<T>(T message, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        return PublishCoreAsync(message, configure: null, ct);
    }

    public Task<Result> PublishAsync<T>(T message, Action<MessagingPublishContext> configure, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(configure);
        return PublishCoreAsync(message, configure, ct);
    }

    private async Task<Result> PublishCoreAsync<T>(
        T message,
        Action<MessagingPublishContext>? configure,
        CancellationToken ct)
        where T : class
    {
        var messageTypeName = typeof(T).Name;

        // P-560: Publish now emits its own span, matching SendAsync. Previously the platform's
        // most-used dispatch verb was the only one producing no Activity of its own.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("MessageBus.Publish");
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);

        // HP-03: propagators run first; the explicit callback runs last so its values win.
        var context = BuildContext(configure);

        try
        {
            await _publishEndpoint.Publish(message, pipe => ApplyContext(pipe, context), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Classify(ex, messageTypeName, "publish");
        }

        // P-348/WO-054: incremented only after the publish above completes without throwing.
        MessagingDiagnostics.PublishCounter.Add(
            1, new KeyValuePair<string, object?>(MessagingTagKeys.MessageType, messageTypeName));

        return Result.Success();
    }

    public async Task<Result> SendAsync<T>(T command, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageTypeName = typeof(T).Name;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("MessageBus.Send");
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);

        // Per-type route override first; otherwise the convention-based resolver.
        var endpointUri = _routeMap.TryGetValue(typeof(T), out var route)
            ? new Uri($"queue:{route}")
            : _resolver.Resolve<T>();

        // P-341: propagators run before dispatch, identical precedence to PublishAsync. SendAsync
        // exposes no configure callback, so propagator output alone shapes the context.
        var context = BuildContext(configure: null);

        try
        {
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(endpointUri).ConfigureAwait(false);
            await endpoint.Send(command, pipe => ApplyContext(pipe, context), ct).ConfigureAwait(false);
        }
        catch (EndpointNotFoundException)
        {
            return MessagingErrors.EndpointNotFound(messageTypeName, endpointUri.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Classify(ex, messageTypeName, "send");
        }

        // P-560: SendAsync previously produced an Activity but no counter, so send traffic was
        // invisible to the same dashboards that charted publishes.
        MessagingDiagnostics.SendCounter.Add(
            1, new KeyValuePair<string, object?>(MessagingTagKeys.MessageType, messageTypeName));

        return Result.Success();
    }

    /// <summary>
    /// Maps a transport exception onto the <c>messaging.*</c> failure contract, rethrowing anything
    /// the classifier does not recognise as an operational fault.
    /// </summary>
    private static Result Classify(Exception ex, string messageTypeName, string operation)
        => MessagingExceptionClassifier.TryClassify(ex, messageTypeName, operation) is { } error
            ? Result.Failure(error)
            : throw ex;

    /// <summary>
    /// Builds the per-dispatch context: every registered propagator in registration order, then the
    /// explicit callback last so it overrides them on any key both set.
    /// </summary>
    /// <remarks>
    /// Always returns an instance. The previous version tested
    /// <c>GetService&lt;IEnumerable&lt;IMessageHeaderPropagator&gt;&gt;() is not null</c> to skip
    /// this allocation, but Microsoft.Extensions.DependencyInjection resolves
    /// <c>IEnumerable&lt;T&gt;</c> to an empty sequence rather than <see langword="null"/>, so that
    /// test was always true and the fast path it guarded was unreachable (P-560).
    /// </remarks>
    private MessagingPublishContext BuildContext(Action<MessagingPublishContext>? configure)
    {
        var context = new MessagingPublishContext();

        for (var i = 0; i < _propagators.Count; i++)
            _propagators[i].Propagate(context);

        configure?.Invoke(context);
        return context;
    }

    /// <summary>
    /// Writes the context onto the outgoing message.
    /// </summary>
    /// <remarks>
    /// Shared with <c>IEventPublisher</c> through <see cref="PublishContextPipe"/> since P-561: the
    /// two paths each had their own copy and drifted, and the event one never wrote the tenant
    /// header at all.
    /// </remarks>
    private static void ApplyContext(SendContext pipe, MessagingPublishContext context)
        => PublishContextPipe.Apply(pipe, context, context.CorrelationId);
}
