using System.Globalization;
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
using SharedKernel.Integration.Notifications.Email.SendGrid.Options;
using SharedKernel.Integration.Notifications.Email.SendGrid.Serialization;
using SharedKernel.Primitives.Logging;
using SharedKernel.Storage;

namespace SharedKernel.Integration.Notifications.Email.SendGrid;

/// <summary>
/// <see cref="INotificationSender"/> implementation delivering email via SendGrid's v3 Mail Send
/// REST API, called directly through <see cref="IHttpClientFactory"/> — no vendor SDK.
/// </summary>
public sealed partial class SendGridEmailNotificationSender : INotificationSender
{
    private const string MailSendPath = "v3/mail/send";
    private const string NotificationDeliveryIdCustomArgKey = "notification_delivery_id";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileStorageFactory? _storageFactory;
    private readonly INotificationSenderIdentityResolver _senderIdentityResolver;
    private readonly IEnumerable<INotificationDeliveryObserver> _observers;
    private readonly SendGridNotificationOptions _options;
    private readonly ILogger<SendGridEmailNotificationSender> _logger;

    /// <summary>Initializes a new instance of <see cref="SendGridEmailNotificationSender"/>.</summary>
    /// <param name="httpClientFactory">Creates the named SendGrid client.</param>
    /// <param name="storageFactory">
    /// Opens the stores attachments are read from; <see langword="null"/> when the host registers no
    /// <c>SharedKernel.Storage</c>. Then every message without attachments is still sent, and a message with
    /// attachments fails with <c>notifications.attachment_unresolvable</c>.
    /// </param>
    /// <param name="senderIdentityResolver">Supplies the <c>From</c> address.</param>
    /// <param name="observers">Told of every delivery outcome.</param>
    /// <param name="options">The SendGrid options.</param>
    /// <param name="logger">The sender's logger.</param>
    public SendGridEmailNotificationSender(
        IHttpClientFactory httpClientFactory,
        IFileStorageFactory? storageFactory,
        INotificationSenderIdentityResolver senderIdentityResolver,
        IEnumerable<INotificationDeliveryObserver> observers,
        IOptions<SendGridNotificationOptions> options,
        ILogger<SendGridEmailNotificationSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(senderIdentityResolver);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _storageFactory = storageFactory;
        _senderIdentityResolver = senderIdentityResolver;
        _observers = observers;
        _options = options.Value;
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

        using var activity = NotificationIntegrationActivitySource.StartSend(NotificationChannel.Email);
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
    /// A send outcome: the result plus the closed failure kind and the caught exception, which travel to the
    /// failure log only — never into the result.
    /// </summary>
    private readonly record struct SendOutcome(
        NotificationDeliveryResult Result,
        string? FailureKind = null,
        int? StatusCode = null,
        Exception? Exception = null);

    private async Task<SendOutcome> SendCoreAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        var attachmentsResult = await ResolveAttachmentsAsync(message.Attachments, ct).ConfigureAwait(false);
        if (attachmentsResult.FailureReason is not null)
        {
            return new SendOutcome(
                new NotificationDeliveryResult(message.NotificationDeliveryId, false, null, attachmentsResult.FailureReason),
                FailureKinds.AttachmentsUnresolvable,
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
            var isSuccess = response.IsSuccessStatusCode;

            var statusCode = (int)response.StatusCode;
            return new SendOutcome(
                new NotificationDeliveryResult(
                    message.NotificationDeliveryId,
                    isSuccess,
                    isSuccess ? response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null : null,
                    isSuccess ? null : string.Create(CultureInfo.InvariantCulture, $"SendGrid responded with status code {statusCode}.")),
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

    private static (string Kind, string Reason) ClassifyTransportFault(Exception exception) =>
        exception switch
        {
            Polly.Timeout.TimeoutRejectedException => (FailureKinds.Timeout, "SendGrid delivery timed out."),
            Polly.ExecutionRejectedException => (FailureKinds.RejectedByPipeline, "SendGrid delivery was rejected by the resilience pipeline."),
            TimeoutException or TaskCanceledException => (FailureKinds.Timeout, "SendGrid delivery timed out."),
            _ => (FailureKinds.Unreachable, "SendGrid could not be reached."),
        };

    /// <summary>The closed set of failure kinds written to the failure log.</summary>
    private static class FailureKinds
    {
        public const string ProviderRejected = "provider_rejected";
        public const string Unreachable = "unreachable";
        public const string Timeout = "timeout";
        public const string RejectedByPipeline = "rejected_by_pipeline";
        public const string AttachmentsUnresolvable = "attachments_unresolvable";
    }

    /// <summary>
    /// The outcome of resolving attachments: the base64 payloads, or a fixed failure sentence and the exception
    /// that caused it (logged, never copied into the result).
    /// </summary>
    private readonly record struct AttachmentResolution(
        IReadOnlyList<SendGridAttachment> Attachments,
        string? FailureReason = null,
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
            return new AttachmentResolution(
                [],
                "Unable to resolve one or more attachments: the message has attachments, but no SharedKernel.Storage is registered (AddSharedKernelStorage).");
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
                // The exception message names the store, so only the fixed sentence reaches the result.
                return new AttachmentResolution(
                    [],
                    "Unable to resolve one or more attachments: an attachment's store could not be opened.",
                    ex);
            }

            var downloadResult = await store
                .DownloadAsync(reference.Key, new FileDownloadOptions { VersionId = reference.VersionId }, ct)
                .ConfigureAwait(false);

            if (downloadResult.IsFailure)
            {
                // The storage Error.Message is never copied; the result carries a fixed sentence.
                return new AttachmentResolution(
                    [],
                    "Unable to resolve one or more attachments: an attachment could not be downloaded from storage.");
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
    /// (default 8KB) chunks, so peak managed memory is bounded by buffer size, not attachment size —
    /// see src/Infrastructure/Integration/CLAUDE.md's N-12 design note. This still materializes the final base64
    /// text as one <see cref="string"/> (SendGrid's Mail Send API has no true streaming-upload path
    /// and requires the full base64 body in one JSON request), but the RAW attachment bytes are
    /// never held as a single contiguous <c>byte[]</c> at any point.
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

    /// <summary>Source-generated <see cref="LoggerMessage"/> definitions for <see cref="SendGridEmailNotificationSender"/>.</summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 200,
            Level = LogLevel.Information,
            Message = "SendGrid email delivery succeeded.")]
        public static partial void DeliverySucceeded(ILogger logger);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 201,
            Level = LogLevel.Warning,
            Message = "SendGrid email delivery failed with failure kind {FailureKind} and status code {StatusCode}.")]
        public static partial void DeliveryFailed(ILogger logger, string failureKind, int? statusCode, Exception? exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 202,
            Level = LogLevel.Warning,
            Message = "Notification delivery observer {ObserverType} threw an exception; delivery outcome is unaffected.")]
        public static partial void ObserverException(ILogger logger, Exception ex, string observerType);
    }
}
