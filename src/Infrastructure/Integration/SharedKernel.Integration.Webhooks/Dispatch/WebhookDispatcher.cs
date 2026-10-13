using System.Diagnostics;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Contracts.Events;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Execution.Context;
using SharedKernel.Integration.Webhooks.Diagnostics;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Default <see cref="IWebhookDispatcher"/> implementation — looks up active subscriptions, signs
/// each delivery, and sends it through the named, resilience-wrapped <see cref="HttpClient"/>.
/// </summary>
public sealed class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IWebhookSubscriptionStore _subscriptionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookSignatureProvider _signatureProvider;
    private readonly IWebhookUrlValidator _urlValidator;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEnumerable<IWebhookDeliveryObserver> _observers;
    private readonly WebhookDeliveryOptions _options;
    private readonly WebhookMetrics _metrics;
    private readonly IClock _clock;

    private readonly ISymmetricEncryptionService? _encryptionService;
    private readonly ILogger<WebhookDispatcher> _logger;

    /// <summary>Initializes a new instance of <see cref="WebhookDispatcher"/>.</summary>
    public WebhookDispatcher(
        IWebhookSubscriptionStore subscriptionStore,
        IHttpClientFactory httpClientFactory,
        WebhookSignatureProvider signatureProvider,
        IWebhookUrlValidator urlValidator,
        IEventPublisher eventPublisher,
        IEnumerable<IWebhookDeliveryObserver> observers,
        IOptions<WebhookDeliveryOptions> options,
        IServiceProvider serviceProvider,
        WebhookMetrics metrics,
        IClock clock,
        ILogger<WebhookDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(subscriptionStore);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(signatureProvider);
        ArgumentNullException.ThrowIfNull(urlValidator);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _subscriptionStore = subscriptionStore;
        _httpClientFactory = httpClientFactory;
        _signatureProvider = signatureProvider;
        _urlValidator = urlValidator;
        _eventPublisher = eventPublisher;
        _observers = observers;
        _options = options.Value;
        _metrics = metrics;
        _clock = clock;
        _logger = logger;

        // Host misconfiguration, not a per-subscription fault: checked at resolution (and at startup by
        // WebhookEncryptionRegistrationValidator) so it can never be swallowed by the fault boundary of a single
        // delivery or surface in the middle of a fan-out.
        if (_options.EncryptPayload)
        {
            _encryptionService = serviceProvider.GetService<ISymmetricEncryptionService>()
                ?? throw new InvalidOperationException(WebhookEncryptionRegistrationValidator.MissingServiceMessage);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryResult>> DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = ResolveEventType(integrationEvent);
        var subscriptions = await _subscriptionStore.GetActiveSubscriptionsAsync(eventType, ct).ConfigureAwait(false);

        using var dispatchActivity = WebhookIntegrationActivitySource.StartDispatch(subscriptions.Count, eventType);

        if (subscriptions.Count == 0)
        {
            return [];
        }

        using var concurrencyGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentDeliveries));

        var deliveryTasks = subscriptions.Select(async subscription =>
        {
            await concurrencyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await DispatchToSubscriptionAsync(subscription, integrationEvent, ct).ConfigureAwait(false);
            }
            finally
            {
                concurrencyGate.Release();
            }
        });

        return await Task.WhenAll(deliveryTasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WebhookDeliveryResult> DispatchToSubscriptionAsync<TEvent>(
        WebhookSubscription subscription,
        TEvent integrationEvent,
        CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = ResolveEventType(integrationEvent);
        var deliveryId = Guid.NewGuid();
        var payloadJson = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
        var started = Stopwatch.GetTimestamp();

        using var activity = WebhookIntegrationActivitySource.StartDeliver(subscription.SubscriptionId, deliveryId, eventType);

        await NotifyAttemptAsync(subscription, 1, ct).ConfigureAwait(false);

        // Fault boundary (rule 8): one subscription's unexpected exception becomes a failed result and never
        // aborts the fan-out. Caller cancellation is the one exception that propagates.
        var tracker = new WebhookAttemptTracker();
        SendOutcome outcome;
        try
        {
            outcome = await SendAsync(subscription, deliveryId, payloadJson, tracker, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(ex));
            _metrics.RecordDelivery(eventType, WellKnownOutcomes.Canceled, started);
            throw;
        }
        catch (Exception ex)
        {
            outcome = new SendOutcome(
                WebhookDeliveryResult.Failed(subscription.SubscriptionId, deliveryId, WebhookErrors.DeliveryFaulted(), null, tracker.Attempts),
                ex,
                IsFault: true);
        }

        var result = outcome.Result;

        string outcomeName;
        if (outcome.IsFault)
        {
            outcomeName = activity.RecordException(outcome.Exception!);
        }
        else if (result.Error is { } error)
        {
            outcomeName = activity.RecordFailure(error);
        }
        else
        {
            outcomeName = WellKnownOutcomes.Success;
        }

        _metrics.RecordDelivery(eventType, outcomeName, started);

        if (outcome.IsFault)
        {
            WebhooksLog.DeliveryFaulted(_logger, deliveryId, subscription.SubscriptionId, eventType, outcome.Exception!);
        }
        else
        {
            LogDeliveryOutcome(subscription, eventType, outcome);

            if (!result.IsSuccess && result.Attempts >= _options.MaxAttempts)
            {
                await PublishExhaustionAsync(subscription, eventType, result, ct).ConfigureAwait(false);
            }
        }

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(WellKnownTagKeys.Outcome, outcomeName);
            activity.SetTag(WebhookTagKeys.AttemptCount, result.Attempts);
        }

        await NotifyCompletedAsync(subscription, result, ct).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public Task<WebhookDeliveryResult> SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var pingEvent = new WebhookPingEvent(Guid.NewGuid(), _clock.UtcNow);
        return DispatchToSubscriptionAsync(subscription, pingEvent, ct);
    }

    /// <summary>
    /// Resolves the routing key: the <see cref="IntegrationEventAttribute"/> name of the event's runtime type,
    /// identical to the CloudEvents <c>type</c> of its <see cref="EventEnvelope{TEvent}"/>.
    /// </summary>
    /// <remarks>
    /// Resolved from the runtime type rather than <c>TEvent</c>, which may be an interface or base type, so the
    /// routing key always describes the same type the payload is serialized as.
    /// </remarks>
    private static string ResolveEventType(IIntegrationEvent integrationEvent) =>
        IntegrationEventDescriptor.For(integrationEvent.GetType()).Name;

    private async Task<SendOutcome> SendAsync(
        WebhookSubscription subscription,
        Guid deliveryId,
        string plainPayloadJson,
        WebhookAttemptTracker tracker,
        CancellationToken ct)
    {
        // P-426: a reserved-header-name collision is rejected before any I/O — cheapest check first.
        if (TryFindReservedHeaderCollision(subscription.Headers))
        {
            return Rejected(subscription, deliveryId, WebhookErrors.HeaderCollision());
        }

        // A subscription without a signing secret is one subscription's data defect: a failed result, not an exception.
        if (subscription.Secrets is not { Count: > 0 })
        {
            return Rejected(subscription, deliveryId, WebhookErrors.SecretsMissing());
        }

        // P-422: re-validated immediately before every send — never cached from registration time.
        var isTargetAllowed = await _urlValidator.ValidateAsync(subscription.Url, ct).ConfigureAwait(false);
        if (!isTargetAllowed)
        {
            return Rejected(subscription, deliveryId, WebhookErrors.TargetRejected());
        }

        // P-427: opt-in encrypt-then-sign — the HMAC signature always covers the transmitted bytes.
        // The wire body is EncryptedPayload's canonical Base64Url encoding (EncryptToStringAsync).
        var wireBody = plainPayloadJson;
        if (_encryptionService is not null)
        {
            wireBody = await _encryptionService.EncryptToStringAsync(
                plainPayloadJson,
                WebhookPayloadAssociatedData.Build(subscription.SubscriptionId, deliveryId),
                ct).ConfigureAwait(false);
        }

        var timestamp = _clock.UtcNow;
        var signature = _signatureProvider.Sign(wireBody, subscription.Secrets[0], timestamp);

        using var httpClient = _httpClientFactory.CreateClient(WebhookHttpClientName.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(wireBody, Encoding.UTF8, MediaTypeNames.Application.Json),
        };
        request.Headers.Add(WebhookSignatureHeaders.SignatureHeaderName, signature);
        request.Headers.Add(WebhookSignatureHeaders.TimestampHeaderName, timestamp.ToUnixTimeSeconds().ToString());
        request.Headers.Add(WebhookSignatureHeaders.DeliveryIdHeaderName, deliveryId.ToString());

        if (subscription.Headers is not null)
        {
            foreach (var (headerName, headerValue) in subscription.Headers)
            {
                request.Headers.TryAddWithoutValidation(headerName, headerValue);
            }
        }

        // The caller's correlation id, so a subscriber's support request can be traced back to the operation that
        // caused the delivery (P-566), written by the shared propagation helper. The context is deliberately null:
        // the helper then writes the correlation id only, so the tenant, actor kind, actor id and client are
        // internal attribution that cannot leave the platform — a webhook endpoint is outside its trust boundary.
        // A subscription header of the same name already on the request wins.
        RequestContextPropagation.WriteHeaders<HttpRequestMessage>(
            context: null,
            request,
            static (carrier, name, value) =>
            {
                if (!carrier.Headers.Contains(name))
                {
                    carrier.Headers.TryAddWithoutValidation(name, value);
                }
            },
            CorrelationIds.Current(RequestContextScope.Current));

        request.Options.Set(WebhookAttemptTracker.Key, tracker);
        tracker.MarkSendStarted();

        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            return new SendOutcome(
                response.IsSuccessStatusCode
                    ? WebhookDeliveryResult.Succeeded(subscription.SubscriptionId, deliveryId, statusCode, tracker.Attempts)
                    : WebhookDeliveryResult.Failed(subscription.SubscriptionId, deliveryId, WebhookErrors.EndpointResponded(statusCode), statusCode, tracker.Attempts),
                Exception: null);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TimeoutException or ExecutionRejectedException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // ExecutionRejectedException (Polly.Timeout.TimeoutRejectedException,
            // Polly.CircuitBreaker.BrokenCircuitException, etc.) is the standard resilience
            // handler's own rejection surface — e.g. AttemptTimeout aborting a slow attempt.
            // Treated identically to any other transport-level failure: a non-throwing,
            // failed WebhookDeliveryResult, never a propagated exception. Only its KIND reaches the
            // result (the message names the subscriber host); the exception itself goes to the log.
            // A TaskCanceledException raised by the caller's own cancellation is not a failure: it propagates,
            // while one raised by the client's own timeout is a Timeout error.
            return new SendOutcome(
                WebhookDeliveryResult.Failed(subscription.SubscriptionId, deliveryId, WebhookErrors.ForTransportFault(ex), null, tracker.Attempts),
                ex);
        }
    }

    private static SendOutcome Rejected(WebhookSubscription subscription, Guid deliveryId, Error error) =>
        new(WebhookDeliveryResult.Failed(subscription.SubscriptionId, deliveryId, error), Exception: null);

    /// <summary>
    /// The result of one send, with the caught transport exception (or fault) travelling beside it to the
    /// logging call and the span only — it is never stored in the result or the exhaustion event.
    /// </summary>
    private readonly record struct SendOutcome(WebhookDeliveryResult Result, Exception? Exception, bool IsFault = false);

    /// <summary>
    /// Checks <paramref name="headers"/> for a name colliding case-insensitively with any of the
    /// three platform signature header names.
    /// </summary>
    private static bool TryFindReservedHeaderCollision(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return false;
        }

        foreach (var headerName in headers.Keys)
        {
            if (string.Equals(headerName, WebhookSignatureHeaders.SignatureHeaderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, WebhookSignatureHeaders.TimestampHeaderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, WebhookSignatureHeaders.DeliveryIdHeaderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void LogDeliveryOutcome(WebhookSubscription subscription, string eventType, SendOutcome outcome)
    {
        var result = outcome.Result;
        if (result.Error is not { } error)
        {
            WebhooksLog.DeliverySucceeded(_logger, result.DeliveryId, subscription.SubscriptionId, eventType, result.Attempts, result.StatusCode ?? 0);
        }
        else if (result.Attempts >= _options.MaxAttempts)
        {
            WebhooksLog.DeliveryExhausted(_logger, result.DeliveryId, subscription.SubscriptionId, eventType, result.Attempts, error.Type, error.Code, outcome.Exception);
        }
        else
        {
            WebhooksLog.DeliveryFailed(_logger, result.DeliveryId, subscription.SubscriptionId, eventType, result.Attempts, error.Type, error.Code, outcome.Exception);
        }
    }

    private async Task PublishExhaustionAsync(
        WebhookSubscription subscription,
        string eventType,
        WebhookDeliveryResult result,
        CancellationToken ct)
    {
        // P-560: IEventPublisher.PublishAsync returns Result, so this outcome has to be observed
        // rather than discarded (SK0030). It must not throw: exhaustion notification is secondary to
        // the delivery attempt that produced it, and propagating a broker failure from here would
        // mask the webhook failure the caller actually needs to see. A publisher that throws is
        // isolated the same way; only caller cancellation propagates.
        try
        {
            var exhaustedEvent = new WebhookDeliveryExhaustedEvent(
                Guid.NewGuid(),
                _clock.UtcNow,
                subscription.SubscriptionId,
                eventType,
                result.Attempts,
                result.Error?.Code);

            var publishResult = await _eventPublisher.PublishAsync(exhaustedEvent, ct).ConfigureAwait(false);

            if (publishResult.IsFailure)
            {
                WebhooksLog.ExhaustionEventNotPublished(
                    _logger,
                    result.DeliveryId,
                    subscription.SubscriptionId,
                    publishResult.Error.Type,
                    publishResult.Error.Code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WebhooksLog.ExhaustionPublisherFaulted(_logger, result.DeliveryId, subscription.SubscriptionId, ex);
        }
    }

    private async Task NotifyAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnAttemptAsync(subscription, attemptNumber, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WebhooksLog.ObserverException(_logger, observer.GetType().Name, ex);
            }
        }
    }

    private async Task NotifyCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnCompletedAsync(subscription, result, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WebhooksLog.ObserverException(_logger, observer.GetType().Name, ex);
            }
        }
    }
}
