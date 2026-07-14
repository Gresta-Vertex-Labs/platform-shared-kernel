using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Default <see cref="IWebhookDispatcher"/> implementation — looks up active subscriptions, signs
/// each delivery, and sends it through the named, resilience-wrapped <see cref="HttpClient"/>.
/// </summary>
public sealed partial class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IWebhookSubscriptionStore _subscriptionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookSignatureProvider _signatureProvider;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEnumerable<IWebhookDeliveryObserver> _observers;
    private readonly WebhookDeliveryOptions _options;
    private readonly ILogger<WebhookDispatcher> _logger;

    /// <summary>Initializes a new instance of <see cref="WebhookDispatcher"/>.</summary>
    public WebhookDispatcher(
        IWebhookSubscriptionStore subscriptionStore,
        IHttpClientFactory httpClientFactory,
        WebhookSignatureProvider signatureProvider,
        IEventPublisher eventPublisher,
        IEnumerable<IWebhookDeliveryObserver> observers,
        IOptions<WebhookDeliveryOptions> options,
        ILogger<WebhookDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(subscriptionStore);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(signatureProvider);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _subscriptionStore = subscriptionStore;
        _httpClientFactory = httpClientFactory;
        _signatureProvider = signatureProvider;
        _eventPublisher = eventPublisher;
        _observers = observers;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryResult>> DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = typeof(TEvent).Name;
        var subscriptions = await _subscriptionStore.GetActiveSubscriptionsAsync(eventType, ct).ConfigureAwait(false);

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

        var eventType = typeof(TEvent).Name;
        var payloadJson = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
        var timestamp = DateTimeOffset.UtcNow;
        var signature = _signatureProvider.Sign(payloadJson, subscription.Secret, timestamp);

        await NotifyAttemptAsync(subscription, 1, ct).ConfigureAwait(false);

        var result = await SendAsync(subscription, payloadJson, signature, timestamp, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            await PublishExhaustionAsync(subscription, eventType, result, ct).ConfigureAwait(false);
        }

        await NotifyCompletedAsync(subscription, result, ct).ConfigureAwait(false);

        return result;
    }

    private async Task<WebhookDeliveryResult> SendAsync(
        WebhookSubscription subscription,
        string payloadJson,
        string signature,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        using var httpClient = _httpClientFactory.CreateClient(WebhookHttpClientName.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, MediaTypeNames.Application.Json),
        };
        request.Headers.Add(WebhookSignatureHeaders.SignatureHeaderName, signature);
        request.Headers.Add(WebhookSignatureHeaders.TimestampHeaderName, timestamp.ToUnixTimeSeconds().ToString());

        var tracker = new WebhookAttemptTracker();
        request.Options.Set(WebhookAttemptTracker.Key, tracker);

        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

            var isSuccess = response.IsSuccessStatusCode;
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                isSuccess,
                (int)response.StatusCode,
                tracker.Attempts,
                isSuccess ? null : $"Webhook endpoint responded with status code {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                false,
                null,
                tracker.Attempts,
                ex.Message);
        }
    }

    private async Task PublishExhaustionAsync(
        WebhookSubscription subscription,
        string eventType,
        WebhookDeliveryResult result,
        CancellationToken ct)
    {
        var exhaustedEvent = new WebhookDeliveryExhaustedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            subscription.SubscriptionId,
            eventType,
            result.Attempts,
            result.Error);

        await _eventPublisher.PublishAsync(exhaustedEvent, ct).ConfigureAwait(false);
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
                LogObserverException(ex, observer.GetType().Name);
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
                LogObserverException(ex, observer.GetType().Name);
            }
        }
    }

    private void LogObserverException(Exception ex, string observerTypeName) =>
        Log.ObserverException(_logger, ex, observerTypeName);

    /// <summary>Source-generated <see cref="LoggerMessage"/> definitions for <see cref="WebhookDispatcher"/>.</summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 0,
            Level = LogLevel.Warning,
            Message = "Webhook delivery observer {ObserverType} threw an exception; delivery outcome is unaffected.")]
        public static partial void ObserverException(ILogger logger, Exception ex, string observerType);
    }
}
