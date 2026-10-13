using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Messaging.MassTransit.Transports;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;

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
    private readonly MessagingTransport? _transport;
    private readonly ILogger<MassTransitMessageBus> _logger;

    public MassTransitMessageBus(
        IPublishEndpoint publishEndpoint,
        ISendEndpointProvider sendEndpointProvider,
        IServiceProvider serviceProvider,
        IReadOnlyDictionary<Type, string> routeMap,
        ConventionSendEndpointResolver resolver,
        ILogger<MassTransitMessageBus> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
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

        // The configured transport maps a partition key to its ordered-delivery mechanism (P-570). Absent when
        // the bus is composed without MessagingBusBuilder, which then sets only the routing key.
        _transport = serviceProvider.GetService<MessagingTransport>();
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

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("messaging publish", ActivityKind.Producer);
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);

        // Propagators run first; the explicit callback runs last so its values win.
        var context = BuildContext(configure);

        // One outcome feeds the span's result and the counter's tag.
        var outcome = WellKnownOutcomes.Success;
        try
        {
            await _publishEndpoint.Publish(message, pipe => ApplyContext(pipe, context), ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException ex)
        {
            outcome = activity.RecordException(ex);
            throw;
        }
        catch (Exception ex)
        {
            // A recognised transport fault becomes a failed Result; anything else is a bug and keeps its
            // original stack trace through a plain rethrow.
            if (MessagingExceptionClassifier.TryClassify(ex, messageTypeName, "publish") is { } error)
            {
                activity.AddExceptionEvents(ex);
                outcome = activity.RecordFailure(error);
                MessagingLog.DispatchFailed(_logger, messageTypeName, "publish", error.Code, ex);
                return Result.Failure(error);
            }

            outcome = activity.RecordException(ex);
            throw;
        }
        finally
        {
            MessagingDiagnostics.PublishCounter.Add(1, MessagingDiagnostics.OutcomeTags(MessagingTagKeys.MessageType, messageTypeName, outcome));
        }
    }

    public async Task<Result> SendAsync<T>(T command, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageTypeName = typeof(T).Name;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("messaging send", ActivityKind.Producer);
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);

        // Per-type route override first; otherwise the convention-based resolver.
        var endpointUri = _routeMap.TryGetValue(typeof(T), out var route)
            ? new Uri($"queue:{route}")
            : _resolver.Resolve<T>();

        // Propagators run before dispatch, identical precedence to PublishAsync. SendAsync exposes no configure
        // callback, so propagator output alone shapes the context.
        var context = BuildContext(configure: null);

        var outcome = WellKnownOutcomes.Success;
        try
        {
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(endpointUri).ConfigureAwait(false);
            await endpoint.Send(command, pipe => ApplyContext(pipe, context), ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (EndpointNotFoundException ex)
        {
            var error = MassTransitErrors.EndpointNotFound(messageTypeName, endpointUri.ToString());
            activity.AddExceptionEvents(ex);
            outcome = activity.RecordFailure(error);
            MessagingLog.DispatchFailed(_logger, messageTypeName, "send", error.Code, ex);
            return Result.Failure(error);
        }
        catch (OperationCanceledException ex)
        {
            outcome = activity.RecordException(ex);
            throw;
        }
        catch (Exception ex)
        {
            if (MessagingExceptionClassifier.TryClassify(ex, messageTypeName, "send") is { } error)
            {
                activity.AddExceptionEvents(ex);
                outcome = activity.RecordFailure(error);
                MessagingLog.DispatchFailed(_logger, messageTypeName, "send", error.Code, ex);
                return Result.Failure(error);
            }

            outcome = activity.RecordException(ex);
            throw;
        }
        finally
        {
            MessagingDiagnostics.SendCounter.Add(1, MessagingDiagnostics.OutcomeTags(MessagingTagKeys.MessageType, messageTypeName, outcome));
        }
    }

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
    private void ApplyContext(SendContext pipe, MessagingPublishContext context)
        => PublishContextPipe.Apply(pipe, context, context.CorrelationId, transport: _transport);
}
