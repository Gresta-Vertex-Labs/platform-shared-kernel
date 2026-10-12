using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Sms.Twilio.Options;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Notifications.Sms.Twilio;

/// <summary>
/// <see cref="INotificationSender"/> implementation delivering SMS via Twilio's Content API
/// (templated messaging), called directly through <see cref="IHttpClientFactory"/> — no vendor SDK.
/// </summary>
public sealed partial class TwilioSmsNotificationSender : INotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<INotificationDeliveryObserver> _observers;
    private readonly TwilioNotificationOptions _options;
    private readonly ILogger<TwilioSmsNotificationSender> _logger;

    /// <summary>Initializes a new instance of <see cref="TwilioSmsNotificationSender"/>.</summary>
    public TwilioSmsNotificationSender(
        IHttpClientFactory httpClientFactory,
        IEnumerable<INotificationDeliveryObserver> observers,
        IOptions<TwilioNotificationOptions> options,
        ILogger<TwilioSmsNotificationSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _observers = observers;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public NotificationChannel SupportedChannel => NotificationChannel.Sms;

    /// <inheritdoc />
    public async Task<NotificationDeliveryResult> SendAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var activity = NotificationIntegrationActivitySource.StartSend(NotificationChannel.Sms);
        var context = new NotificationDeliveryContext(message.NotificationDeliveryId, message.Channel, message.Recipient, message.TemplateId);

        await NotifyAttemptAsync(context, 1, ct).ConfigureAwait(false);

        var outcome = await SendCoreAsync(message, ct).ConfigureAwait(false);
        var result = outcome.Result;

        LogDeliveryOutcome(outcome);

        if (activity is not null)
        {
            activity.SetTag(NotificationActivityTags.Outcome, result.IsSuccess ? "success" : "failure");
            activity.SetTag(NotificationActivityTags.AttemptCount, 1);
        }

        await NotifyCompletedAsync(context, result, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// A send outcome: the result plus the closed failure kind, the status code and the caught exception, which
    /// travel to the failure log only — never into the result.
    /// </summary>
    private readonly record struct SendOutcome(
        NotificationDeliveryResult Result,
        string? FailureKind = null,
        int? StatusCode = null,
        Exception? Exception = null);

    /// <summary>The closed set of failure kinds written to the failure log.</summary>
    private static class FailureKinds
    {
        public const string ProviderRejected = "provider_rejected";
        public const string Unreachable = "unreachable";
        public const string Timeout = "timeout";
        public const string RejectedByPipeline = "rejected_by_pipeline";
    }

    private static (string Kind, string Reason) ClassifyTransportFault(Exception exception) =>
        exception switch
        {
            Polly.Timeout.TimeoutRejectedException => (FailureKinds.Timeout, "Twilio delivery timed out."),
            Polly.ExecutionRejectedException => (FailureKinds.RejectedByPipeline, "Twilio delivery was rejected by the resilience pipeline."),
            TimeoutException or TaskCanceledException => (FailureKinds.Timeout, "Twilio delivery timed out."),
            _ => (FailureKinds.Unreachable, "Twilio could not be reached."),
        };

    private async Task<SendOutcome> SendCoreAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        // Twilio's Content API template-variable substitution never produces markup for an SMS
        // body — the "plain-text rendering" guarantee is satisfied structurally by using this API
        // shape at all, never by an extra opt-out flag or a code path that renders HTML.
        var contentVariablesJson = JsonSerializer.Serialize(message.TemplateModel, typeof(TTemplateModel));

        var formFields = new List<KeyValuePair<string, string>>
        {
            new("To", message.Recipient),
            new("ContentSid", message.TemplateId),
            new("ContentVariables", contentVariablesJson),
        };

        if (!string.IsNullOrEmpty(_options.MessagingServiceSid))
        {
            formFields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _options.MessagingServiceSid));
        }
        else
        {
            formFields.Add(new KeyValuePair<string, string>("From", _options.From!));
        }

        // A trailing slash keeps a base address with a path prefix (a stand-in server's mount point) in the result.
        var baseAddress = _options.BaseAddress.AbsoluteUri.EndsWith('/') ? _options.BaseAddress : new Uri(_options.BaseAddress.AbsoluteUri + "/");
        var endpoint = new Uri(baseAddress, $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");

        using var httpClient = _httpClientFactory.CreateClient(TwilioHttpClientName.Name);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            // application/x-www-form-urlencoded — ContentVariables is the one field whose VALUE is
            // itself STJ-serialized JSON; the request envelope itself is never a JSON body.
            Content = new FormUrlEncodedContent(formFields),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        // A genuine, provider-enforced request-level dedup guarantee — stronger than SendGrid's
        // custom_args correlation-only approach.
        httpRequest.Headers.Add(TwilioHeaders.IdempotencyKeyHeaderName, message.NotificationDeliveryId.ToString());

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
            var isSuccess = response.IsSuccessStatusCode;

            string? providerMessageId = null;
            if (isSuccess)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                providerMessageId = TryExtractSid(body);
            }

            var statusCode = (int)response.StatusCode;
            return new SendOutcome(
                new NotificationDeliveryResult(
                    message.NotificationDeliveryId,
                    isSuccess,
                    providerMessageId,
                    isSuccess ? null : string.Create(CultureInfo.InvariantCulture, $"Twilio responded with status code {statusCode}.")),
                isSuccess ? null : FailureKinds.ProviderRejected,
                statusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Polly.ExecutionRejectedException)
        {
            // Only the KIND of the transport fault reaches the result: the exception message names the
            // provider endpoint. The exception itself goes to the failure log.
            var (kind, reason) = ClassifyTransportFault(ex);
            return new SendOutcome(
                new NotificationDeliveryResult(message.NotificationDeliveryId, false, null, reason),
                kind,
                Exception: ex);
        }
    }

    private static string? TryExtractSid(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task NotifyAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnAttemptAsync(context, attemptNumber, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.ObserverException(_logger, ex, observer.GetType().Name);
            }
        }
    }

    private async Task NotifyCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnCompletedAsync(context, result, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.ObserverException(_logger, ex, observer.GetType().Name);
            }
        }
    }

    private void LogDeliveryOutcome(SendOutcome outcome)
    {
        if (outcome.Result.IsSuccess)
        {
            Log.DeliverySucceeded(_logger);
        }
        else
        {
            // NEVER include Recipient/TemplateModel here — only the closed failure kind, the status code and
            // the caught exception object (never its message as a placeholder).
            Log.DeliveryFailed(_logger, outcome.FailureKind ?? FailureKinds.ProviderRejected, outcome.StatusCode, outcome.Exception);
        }
    }

    /// <summary>Source-generated <see cref="LoggerMessage"/> definitions for <see cref="TwilioSmsNotificationSender"/>.</summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 300,
            Level = LogLevel.Information,
            Message = "Twilio SMS delivery succeeded.")]
        public static partial void DeliverySucceeded(ILogger logger);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 301,
            Level = LogLevel.Warning,
            Message = "Twilio SMS delivery failed with failure kind {FailureKind} and status code {StatusCode}.")]
        public static partial void DeliveryFailed(ILogger logger, string failureKind, int? statusCode, Exception? exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 302,
            Level = LogLevel.Warning,
            Message = "Notification delivery observer {ObserverType} threw an exception; delivery outcome is unaffected.")]
        public static partial void ObserverException(ILogger logger, Exception ex, string observerType);
    }
}
