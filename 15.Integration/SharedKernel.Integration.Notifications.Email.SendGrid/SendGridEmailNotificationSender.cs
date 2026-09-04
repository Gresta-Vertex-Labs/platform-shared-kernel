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
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.Integration.Notifications.Email.SendGrid;

/// <summary>
/// <see cref="INotificationSender"/> implementation delivering email via SendGrid's v3 Mail Send
/// REST API, called directly through <see cref="IHttpClientFactory"/> — no vendor SDK.
/// </summary>
public sealed partial class SendGridEmailNotificationSender : INotificationSender
{
    private const string MailSendEndpoint = "https://api.sendgrid.com/v3/mail/send";
    private const string NotificationDeliveryIdCustomArgKey = "notification_delivery_id";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileStorage _fileStorage;
    private readonly INotificationSenderIdentityResolver _senderIdentityResolver;
    private readonly IEnumerable<INotificationDeliveryObserver> _observers;
    private readonly SendGridNotificationOptions _options;
    private readonly ILogger<SendGridEmailNotificationSender> _logger;

    /// <summary>Initializes a new instance of <see cref="SendGridEmailNotificationSender"/>.</summary>
    public SendGridEmailNotificationSender(
        IHttpClientFactory httpClientFactory,
        IFileStorage fileStorage,
        INotificationSenderIdentityResolver senderIdentityResolver,
        IEnumerable<INotificationDeliveryObserver> observers,
        IOptions<SendGridNotificationOptions> options,
        ILogger<SendGridEmailNotificationSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(fileStorage);
        ArgumentNullException.ThrowIfNull(senderIdentityResolver);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _fileStorage = fileStorage;
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

        var result = await SendCoreAsync(message, ct).ConfigureAwait(false);

        LogDeliveryOutcome(result);

        if (activity is not null)
        {
            activity.SetTag(NotificationActivityTags.Outcome, result.IsSuccess ? "success" : "failure");
            activity.SetTag(NotificationActivityTags.AttemptCount, 1);
        }

        await NotifyCompletedAsync(context, result, ct).ConfigureAwait(false);

        return result;
    }

    private async Task<NotificationDeliveryResult> SendCoreAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        var attachmentsResult = await ResolveAttachmentsAsync(message.Attachments, ct).ConfigureAwait(false);
        if (attachmentsResult.IsFailure)
        {
            return new NotificationDeliveryResult(
                message.NotificationDeliveryId,
                false,
                null,
                $"Unable to resolve one or more attachments: {attachmentsResult.Error.Message}");
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
            Attachments = attachmentsResult.Value.Count > 0 ? attachmentsResult.Value : null,
        };

        using var httpClient = _httpClientFactory.CreateClient(SendGridHttpClientName.Name);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MailSendEndpoint)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(request, SendGridJsonContext.Default.SendGridMailRequest)),
        };
        httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MediaTypeNames.Application.Json);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
            var isSuccess = response.IsSuccessStatusCode;

            return new NotificationDeliveryResult(
                message.NotificationDeliveryId,
                isSuccess,
                isSuccess ? response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null : null,
                isSuccess ? null : $"SendGrid responded with status code {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Polly.ExecutionRejectedException)
        {
            return new NotificationDeliveryResult(message.NotificationDeliveryId, false, null, ex.Message);
        }
    }

    private async Task<Result<IReadOnlyList<SendGridAttachment>>> ResolveAttachmentsAsync(
        IReadOnlyList<NotificationAttachment>? attachments,
        CancellationToken ct)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return Result<IReadOnlyList<SendGridAttachment>>.Success([]);
        }

        var resolved = new List<SendGridAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            var downloadResult = await _fileStorage
                .DownloadAsync(attachment.FileReference.Bucket, attachment.FileReference.Key, ct)
                .ConfigureAwait(false);

            if (downloadResult.IsFailure)
            {
                return Result<IReadOnlyList<SendGridAttachment>>.Failure(downloadResult.Error);
            }

            await using var download = downloadResult.Value;
            var base64Content = await ToBase64Async(download.Content, ct).ConfigureAwait(false);

            resolved.Add(new SendGridAttachment
            {
                Content = base64Content,
                Filename = attachment.FileName,
                Type = attachment.ContentType ?? download.ContentType,
            });
        }

        return Result<IReadOnlyList<SendGridAttachment>>.Success(resolved);
    }

    /// <summary>
    /// Base64-encodes <paramref name="content"/> by piping it through a <see cref="CryptoStream"/>
    /// (<see cref="ToBase64Transform"/>) rather than reading the entire attachment into one
    /// <c>byte[]</c> first. <see cref="StreamReader"/> reads the transformed stream in bounded
    /// (default 8KB) chunks, so peak managed memory is bounded by buffer size, not attachment size —
    /// see 15.Integration/CLAUDE.md's N-12 design note. This still materializes the final base64
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

    private void LogDeliveryOutcome(NotificationDeliveryResult result)
    {
        if (result.IsSuccess)
        {
            Log.DeliverySucceeded(_logger);
        }
        else
        {
            // NEVER include Recipient/TemplateModel here — only the provider-side error text.
            Log.DeliveryFailed(_logger, result.Error);
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
            Message = "SendGrid email delivery failed: {Error}")]
        public static partial void DeliveryFailed(ILogger logger, string? error);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Integration + 202,
            Level = LogLevel.Warning,
            Message = "Notification delivery observer {ObserverType} threw an exception; delivery outcome is unaffected.")]
        public static partial void ObserverException(ILogger logger, Exception ex, string observerType);
    }
}
