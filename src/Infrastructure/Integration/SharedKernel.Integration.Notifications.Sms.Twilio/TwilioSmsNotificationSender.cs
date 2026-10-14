using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Sms.Twilio.Diagnostics;
using SharedKernel.Integration.Notifications.Sms.Twilio.Options;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Notifications.Sms.Twilio;

/// <summary>
/// <see cref="INotificationSender"/> implementation delivering SMS via Twilio's Content API
/// (templated messaging), called directly through <see cref="IHttpClientFactory"/> — no vendor SDK.
/// </summary>
public sealed class TwilioSmsNotificationSender : INotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<INotificationDeliveryObserver> _observers;
    private readonly TwilioNotificationOptions _options;
    private readonly NotificationMetrics _metrics;
    private readonly ILogger<TwilioSmsNotificationSender> _logger;

    /// <summary>Initializes a new instance of <see cref="TwilioSmsNotificationSender"/>.</summary>
    public TwilioSmsNotificationSender(
        IHttpClientFactory httpClientFactory,
        IEnumerable<INotificationDeliveryObserver> observers,
        IOptions<TwilioNotificationOptions> options,
        NotificationMetrics metrics,
        ILogger<TwilioSmsNotificationSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _observers = observers;
        _options = options.Value;
        _metrics = metrics;
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

        var started = Stopwatch.GetTimestamp();
        using var activity = NotificationIntegrationActivitySource.StartSend(NotificationChannel.Sms, message.NotificationDeliveryId);
        var context = new NotificationDeliveryContext(message.NotificationDeliveryId, message.Channel, message.Recipient, message.TemplateId);

        await NotifyAttemptAsync(context, 1, ct).ConfigureAwait(false);

        SendOutcome outcome;
        try
        {
            outcome = await SendCoreAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Caller cancellation is never an error; anything else is an unexpected fault that propagates after it
            // is recorded in full on the span.
            var faultOutcome = activity.RecordException(ex);
            activity?.SetTag(WellKnownTagKeys.Outcome, faultOutcome);
            _metrics.RecordSend(NotificationChannel.Sms, faultOutcome, started);
            throw;
        }

        var result = outcome.Result;
        var outcomeName = result.Error is { } error ? activity.RecordFailure(error) : WellKnownOutcomes.Success;
        _metrics.RecordSend(NotificationChannel.Sms, outcomeName, started);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(WellKnownTagKeys.Outcome, outcomeName);
            activity.SetTag(NotificationTagKeys.AttemptCount, 1);
        }

        LogDeliveryOutcome(outcome);

        await NotifyCompletedAsync(context, result, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// A send outcome: the result plus the status code and the caught exception, which travel to the failure log
    /// only — never into the result.
    /// </summary>
    private readonly record struct SendOutcome(
        NotificationDeliveryResult Result,
        int? StatusCode = null,
        Exception? Exception = null);

    private static Error ClassifyTransportFault(Exception exception) =>
        exception switch
        {
            Polly.Timeout.TimeoutRejectedException => NotificationErrors.ProviderTimeout(),
            Polly.ExecutionRejectedException => NotificationErrors.ProviderUnavailable(),
            TimeoutException or TaskCanceledException => NotificationErrors.ProviderTimeout(),
            _ => NotificationErrors.ProviderUnavailable(),
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
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new SendOutcome(
                    NotificationDeliveryResult.Failed(message.NotificationDeliveryId, NotificationErrors.ForStatusCode(statusCode)),
                    statusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new SendOutcome(NotificationDeliveryResult.Succeeded(message.NotificationDeliveryId, TryExtractSid(body)), statusCode);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TimeoutException or Polly.ExecutionRejectedException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Only the KIND of the transport fault reaches the result: the exception message names the
            // provider endpoint. The exception itself goes to the failure log. A TaskCanceledException raised by the
            // caller's own cancellation is not a failure and propagates; one raised by the client's timeout is a
            // Timeout error.
            return new SendOutcome(
                NotificationDeliveryResult.Failed(message.NotificationDeliveryId, ClassifyTransportFault(ex)),
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
                TwilioLog.ObserverException(_logger, observer.GetType().Name, ex);
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
                TwilioLog.ObserverException(_logger, observer.GetType().Name, ex);
            }
        }
    }

    private void LogDeliveryOutcome(SendOutcome outcome)
    {
        var result = outcome.Result;
        if (result.Error is not { } error)
        {
            TwilioLog.DeliverySucceeded(_logger, result.NotificationDeliveryId);
        }
        else
        {
            // NEVER include Recipient/TemplateModel here — only the error type and code, the status code and the
            // caught exception object (never its message as a placeholder).
            TwilioLog.DeliveryFailed(_logger, result.NotificationDeliveryId, error.Type, error.Code, outcome.StatusCode, outcome.Exception);
        }
    }
}
