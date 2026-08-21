using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Contracts.Events;
using SharedKernel.Cryptography.Symmetric;
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
    private readonly IWebhookUrlValidator _urlValidator;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEnumerable<IWebhookDeliveryObserver> _observers;
    private readonly WebhookDeliveryOptions _options;
    private readonly IServiceProvider _serviceProvider;
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
        ArgumentNullException.ThrowIfNull(logger);

        _subscriptionStore = subscriptionStore;
        _httpClientFactory = httpClientFactory;
        _signatureProvider = signatureProvider;
        _urlValidator = urlValidator;
        _eventPublisher = eventPublisher;
        _observers = observers;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryResult>> DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = typeof(TEvent).Name;
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

        var eventType = typeof(TEvent).Name;
        var deliveryId = Guid.NewGuid();
        var payloadJson = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());

        using var activity = WebhookIntegrationActivitySource.StartDispatchToSubscription(subscription.SubscriptionId, eventType);

        await NotifyAttemptAsync(subscription, 1, ct).ConfigureAwait(false);

        var result = await SendAsync(subscription, deliveryId, payloadJson, ct).ConfigureAwait(false);

        LogDeliveryOutcome(subscription, eventType, result);

        if (!result.IsSuccess && result.Attempts >= _options.MaxAttempts)
        {
            await PublishExhaustionAsync(subscription, eventType, result, ct).ConfigureAwait(false);
        }

        if (activity is not null)
        {
            activity.SetTag(WebhookActivityTags.Outcome, result.IsSuccess ? "success" : "failure");
            activity.SetTag(WebhookActivityTags.AttemptCount, result.Attempts);
        }

        await NotifyCompletedAsync(subscription, result, ct).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public Task<WebhookDeliveryResult> SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var pingEvent = new WebhookPingEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);
        return DispatchToSubscriptionAsync(subscription, pingEvent, ct);
    }

    private async Task<WebhookDeliveryResult> SendAsync(
        WebhookSubscription subscription,
        Guid deliveryId,
        string plainPayloadJson,
        CancellationToken ct)
    {
        // P-426: a reserved-header-name collision is rejected before any I/O — cheapest check first.
        if (TryFindReservedHeaderCollision(subscription.Headers, out var collidingHeaderName))
        {
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                deliveryId,
                false,
                null,
                0,
                $"Subscription header '{collidingHeaderName}' collides with a reserved webhook signature header and was rejected.");
        }

        // P-422: re-validated immediately before every send — never cached from registration time.
        var isTargetAllowed = await _urlValidator.ValidateAsync(subscription.Url, ct).ConfigureAwait(false);
        if (!isTargetAllowed)
        {
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                deliveryId,
                false,
                null,
                0,
                $"Webhook delivery target for subscription {subscription.SubscriptionId} was rejected by the configured IWebhookUrlValidator.");
        }

        // P-427: opt-in encrypt-then-sign — the HMAC signature always covers the transmitted bytes.
        var wireBody = plainPayloadJson;
        if (_options.EncryptPayload)
        {
            var encryptionService = _serviceProvider.GetService<ISymmetricEncryptionService>()
                ?? throw new InvalidOperationException(
                    "WebhookDeliveryOptions.EncryptPayload is enabled but no ISymmetricEncryptionService is " +
                    "registered. Call SharedKernel.Cryptography's AddSharedKernelCryptography() and register " +
                    "an IEncryptionKeyProvider before resolving IWebhookDispatcher.");
            wireBody = encryptionService.EncryptToString(plainPayloadJson);
        }

        if (subscription.Secrets is not { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"WebhookSubscription {subscription.SubscriptionId} has no signing secrets configured.");
        }

        var timestamp = DateTimeOffset.UtcNow;
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

        var tracker = new WebhookAttemptTracker();
        request.Options.Set(WebhookAttemptTracker.Key, tracker);

        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

            var isSuccess = response.IsSuccessStatusCode;
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                deliveryId,
                isSuccess,
                (int)response.StatusCode,
                tracker.Attempts,
                isSuccess ? null : $"Webhook endpoint responded with status code {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or ExecutionRejectedException)
        {
            // ExecutionRejectedException (Polly.Timeout.TimeoutRejectedException,
            // Polly.CircuitBreaker.BrokenCircuitException, etc.) is the standard resilience
            // handler's own rejection surface — e.g. AttemptTimeout aborting a slow attempt.
            // Treated identically to any other transport-level failure: a non-throwing,
            // failed WebhookDeliveryResult, never a propagated exception.
            return new WebhookDeliveryResult(
                subscription.SubscriptionId,
                deliveryId,
                false,
                null,
                tracker.Attempts,
                ex.Message);
        }
    }

    /// <summary>
    /// Checks <paramref name="headers"/> for a name colliding case-insensitively with any of the
    /// three platform signature header names.
    /// </summary>
    private static bool TryFindReservedHeaderCollision(
        IReadOnlyDictionary<string, string>? headers,
        out string collidingHeaderName)
    {
        collidingHeaderName = string.Empty;

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
                collidingHeaderName = headerName;
                return true;
            }
        }

        return false;
    }

    private void LogDeliveryOutcome(WebhookSubscription subscription, string eventType, WebhookDeliveryResult result)
    {
        if (result.IsSuccess)
        {
            Log.DeliverySucceeded(_logger, subscription.SubscriptionId, eventType, result.Attempts, result.StatusCode ?? 0);
        }
        else if (result.Attempts >= _options.MaxAttempts)
        {
            Log.DeliveryExhausted(_logger, subscription.SubscriptionId, eventType, result.Attempts);
        }
        else
        {
            Log.DeliveryFailed(_logger, subscription.SubscriptionId, eventType, result.Attempts, result.StatusCode, result.Error);
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

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 1,
            Level = LogLevel.Information,
            Message = "Webhook delivery to subscription {SubscriptionId} for event {EventType} succeeded after {Attempts} attempt(s) with status code {StatusCode}.")]
        public static partial void DeliverySucceeded(ILogger logger, Guid subscriptionId, string eventType, int attempts, int statusCode);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 2,
            Level = LogLevel.Warning,
            Message = "Webhook delivery to subscription {SubscriptionId} for event {EventType} failed after {Attempts} attempt(s) with status code {StatusCode}: {Error}")]
        public static partial void DeliveryFailed(ILogger logger, Guid subscriptionId, string eventType, int attempts, int? statusCode, string? error);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 3,
            Level = LogLevel.Warning,
            Message = "Webhook delivery to subscription {SubscriptionId} for event {EventType} was exhausted after {Attempts} attempt(s) without a successful response.")]
        public static partial void DeliveryExhausted(ILogger logger, Guid subscriptionId, string eventType, int attempts);
    }
}
