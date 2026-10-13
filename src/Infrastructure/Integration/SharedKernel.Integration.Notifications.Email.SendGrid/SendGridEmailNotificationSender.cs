using System.Diagnostics;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Email.SendGrid.Diagnostics;
using SharedKernel.Integration.Notifications.Email.SendGrid.Options;
using SharedKernel.Integration.Notifications.Email.SendGrid.Serialization;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Storage;

namespace SharedKernel.Integration.Notifications.Email.SendGrid;

/// <summary>
/// <see cref="INotificationSender"/> implementation delivering email via SendGrid's v3 Mail Send
/// REST API, called directly through <see cref="IHttpClientFactory"/> — no vendor SDK.
/// </summary>
public sealed class SendGridEmailNotificationSender : INotificationSender
{
    private const string MailSendPath = "v3/mail/send";
    private const string NotificationDeliveryIdCustomArgKey = "notification_delivery_id";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileStorageFactory? _storageFactory;
    private readonly INotificationSenderIdentityResolver _senderIdentityResolver;
    private readonly IEnumerable<INotificationDeliveryObserver> _observers;
    private readonly SendGridNotificationOptions _options;
    private readonly NotificationMetrics _metrics;
    private readonly ILogger<SendGridEmailNotificationSender> _logger;

    /// <summary>Initializes a new instance of <see cref="SendGridEmailNotificationSender"/>.</summary>
    /// <param name="httpClientFactory">Creates the named SendGrid client.</param>
    /// <param name="storageFactory">
    /// Opens the stores attachments are read from; <see langword="null"/> when the host registers no
    /// <c>SharedKernel.Storage</c>. Then every message without attachments is still sent, and a message with
    /// attachments fails with <see cref="NotificationErrorCodes.AttachmentUnresolvable"/>.
    /// </param>
    /// <param name="senderIdentityResolver">Supplies the <c>From</c> address.</param>
    /// <param name="observers">Told of every delivery outcome.</param>
    /// <param name="options">The SendGrid options.</param>
    /// <param name="metrics">Records the send counter and duration.</param>
    /// <param name="logger">The sender's logger.</param>
    public SendGridEmailNotificationSender(
        IHttpClientFactory httpClientFactory,
        IFileStorageFactory? storageFactory,
        INotificationSenderIdentityResolver senderIdentityResolver,
        IEnumerable<INotificationDeliveryObserver> observers,
        IOptions<SendGridNotificationOptions> options,
        NotificationMetrics metrics,
        ILogger<SendGridEmailNotificationSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(senderIdentityResolver);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _storageFactory = storageFactory;
        _senderIdentityResolver = senderIdentityResolver;
        _observers = observers;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public NotificationChannel SupportedChannel => NotificationChannel.Email;

    /// <inheritdoc />
    public async Task<NotificationDeliveryResult> SendAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var started = Stopwatch.GetTimestamp();
        using var activity = NotificationIntegrationActivitySource.StartSend(NotificationChannel.Email, message.NotificationDeliveryId);
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
            _metrics.RecordSend(NotificationChannel.Email, faultOutcome, started);
            throw;
        }

        var result = outcome.Result;
        var outcomeName = result.Error is { } error ? activity.RecordFailure(error) : WellKnownOutcomes.Success;
        _metrics.RecordSend(NotificationChannel.Email, outcomeName, started);

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

    private async Task<SendOutcome> SendCoreAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        var attachmentsResult = await ResolveAttachmentsAsync(message.Attachments, ct).ConfigureAwait(false);
        if (attachmentsResult.Failed)
        {
            if (attachmentsResult.StorageErrorCode is { } storageErrorCode)
            {
                SendGridLog.AttachmentUnresolvable(_logger, message.NotificationDeliveryId, storageErrorCode);
            }

            return new SendOutcome(
                NotificationDeliveryResult.Failed(message.NotificationDeliveryId, NotificationErrors.AttachmentUnresolvable()),
                Exception: attachmentsResult.Exception);
        }

        var senderIdentity = await _senderIdentityResolver.ResolveAsync(NotificationChannel.Email, ct).ConfigureAwait(false);

        var request = new SendGridMailRequest
        {
            Personalizations =
            [
                new SendGridPersonalization
                {
                    To = [new SendGridEmailAddress { Email = message.Recipient }],
                    DynamicTemplateData = JsonSerializer.SerializeToElement(message.TemplateModel, typeof(TTemplateModel)),
                },
            ],
            From = new SendGridEmailAddress { Email = senderIdentity.FromAddress, Name = senderIdentity.DisplayName },
            ReplyTo = (message.ReplyTo ?? senderIdentity.ReplyTo) is { Length: > 0 } replyTo
                ? new SendGridEmailAddress { Email = replyTo }
                : null,
            TemplateId = message.TemplateId,
            CustomArgs = new Dictionary<string, string> { [NotificationDeliveryIdCustomArgKey] = message.NotificationDeliveryId.ToString() },
            Attachments = attachmentsResult.Attachments.Count > 0 ? attachmentsResult.Attachments : null,
        };

        using var httpClient = _httpClientFactory.CreateClient(SendGridHttpClientName.Name);
        // A trailing slash keeps a base address with a path prefix (a stand-in server's mount point) in the result.
        var baseAddress = _options.BaseAddress.AbsoluteUri.EndsWith('/') ? _options.BaseAddress : new Uri(_options.BaseAddress.AbsoluteUri + "/");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, MailSendPath))
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(request, SendGridJsonContext.Default.SendGridMailRequest)),
        };
        httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MediaTypeNames.Application.Json);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

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

            var providerMessageId = response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null;
            return new SendOutcome(NotificationDeliveryResult.Succeeded(message.NotificationDeliveryId, providerMessageId), statusCode);
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

    private static SharedKernel.Primitives.Errors.Error ClassifyTransportFault(Exception exception) =>
        exception switch
        {
            Polly.Timeout.TimeoutRejectedException => NotificationErrors.ProviderTimeout(),
            Polly.ExecutionRejectedException => NotificationErrors.ProviderUnavailable(),
            TimeoutException or TaskCanceledException => NotificationErrors.ProviderTimeout(),
            _ => NotificationErrors.ProviderUnavailable(),
        };

    /// <summary>
    /// The outcome of resolving attachments: the base64 payloads, or a failure with the storage error code and the
    /// exception that caused it (both logged, never copied into the result).
    /// </summary>
    private readonly record struct AttachmentResolution(
        IReadOnlyList<SendGridAttachment> Attachments,
        bool Failed = false,
        string? StorageErrorCode = null,
        Exception? Exception = null);

    private async Task<AttachmentResolution> ResolveAttachmentsAsync(
        IReadOnlyList<NotificationAttachment>? attachments,
        CancellationToken ct)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return new AttachmentResolution([]);
        }

        if (_storageFactory is null)
        {
            // Attachments live in object storage; a host without SharedKernel.Storage cannot read them.
            return new AttachmentResolution([], Failed: true);
        }

        var resolved = new List<SendGridAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            var reference = attachment.FileReference;
            IFileStorage store;
            try
            {
                store = _storageFactory.Open(reference);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // An attachment naming an unregistered store, or a tenancy its store does not have, is
                // an unresolvable reference: a failed delivery, never an exception (INotificationSender).
                // The exception message names the store, so only the fixed error reaches the result.
                return new AttachmentResolution([], Failed: true, Exception: ex);
            }

            var downloadResult = await store
                .DownloadAsync(reference.Key, new FileDownloadOptions { VersionId = reference.VersionId }, ct)
                .ConfigureAwait(false);

            if (downloadResult.IsFailure)
            {
                // The storage Error.Message is never copied; only its code is logged.
                return new AttachmentResolution([], Failed: true, StorageErrorCode: downloadResult.Error.Code);
            }

            await using var download = downloadResult.Value;
            var base64Content = await ToBase64Async(download.Content, ct).ConfigureAwait(false);

            resolved.Add(new SendGridAttachment
            {
                Content = base64Content,
                Filename = attachment.FileName,
                Type = attachment.ContentType ?? download.Properties.ContentType,
            });
        }

        return new AttachmentResolution(resolved);
    }

    /// <summary>
    /// Base64-encodes <paramref name="content"/> by piping it through a <see cref="CryptoStream"/>
    /// (<see cref="ToBase64Transform"/>) rather than reading the entire attachment into one
    /// <c>byte[]</c> first. <see cref="StreamReader"/> reads the transformed stream in bounded
    /// (default 8KB) chunks, so peak managed memory is bounded by buffer size, not attachment size.
    /// This still materializes the final base64 text as one <see cref="string"/> (SendGrid's Mail Send API has no
    /// true streaming-upload path and requires the full base64 body in one JSON request), but the RAW attachment
    /// bytes are never held as a single contiguous <c>byte[]</c> at any point.
    /// </summary>
    private static async Task<string> ToBase64Async(Stream content, CancellationToken ct)
    {
        await using var base64Stream = new CryptoStream(content, new ToBase64Transform(), CryptoStreamMode.Read);
        using var reader = new StreamReader(base64Stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
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
                SendGridLog.ObserverException(_logger, observer.GetType().Name, ex);
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
                SendGridLog.ObserverException(_logger, observer.GetType().Name, ex);
            }
        }
    }

    private void LogDeliveryOutcome(SendOutcome outcome)
    {
        var result = outcome.Result;
        if (result.Error is not { } error)
        {
            SendGridLog.DeliverySucceeded(_logger, result.NotificationDeliveryId);
        }
        else
        {
            // NEVER include Recipient/TemplateModel here — only the error type and code, the status code and the
            // caught exception object (never its message as a placeholder).
            SendGridLog.DeliveryFailed(_logger, result.NotificationDeliveryId, error.Type, error.Code, outcome.StatusCode, outcome.Exception);
        }
    }
}
