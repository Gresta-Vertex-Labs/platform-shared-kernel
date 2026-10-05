using System.Text.Json.Serialization;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Serialization;

/// <summary>The outbound request envelope for SendGrid's v3 Mail Send API (<c>POST /v3/mail/send</c>).</summary>
internal sealed record SendGridMailRequest
{
    [JsonPropertyName("personalizations")]
    public required IReadOnlyList<SendGridPersonalization> Personalizations { get; init; }

    [JsonPropertyName("from")]
    public required SendGridEmailAddress From { get; init; }

    [JsonPropertyName("reply_to")]
    public SendGridEmailAddress? ReplyTo { get; init; }

    [JsonPropertyName("template_id")]
    public required string TemplateId { get; init; }

    /// <summary>
    /// Carries <c>NotificationDeliveryId</c> for after-the-fact correlation with SendGrid's own
    /// event webhooks — SendGrid's Mail Send API treats this field as opaque metadata, not a
    /// request-level idempotency key, so it is correlation-only (see
    /// <see cref="SendGridEmailNotificationSender"/>'s remarks).
    /// </summary>
    [JsonPropertyName("custom_args")]
    public IReadOnlyDictionary<string, string>? CustomArgs { get; init; }

    [JsonPropertyName("attachments")]
    public IReadOnlyList<SendGridAttachment>? Attachments { get; init; }
}

/// <summary>One recipient block, carrying the per-recipient template-model binding.</summary>
internal sealed record SendGridPersonalization
{
    [JsonPropertyName("to")]
    public required IReadOnlyList<SendGridEmailAddress> To { get; init; }

    /// <summary>
    /// The caller's <c>TTemplateModel</c>, pre-serialized to a <see cref="System.Text.Json.JsonElement"/>
    /// via the runtime (non-source-generated) <see cref="System.Text.Json.JsonSerializer"/> overload —
    /// the model's concrete type is not known at this package's compile time, mirroring
    /// <c>WebhookDispatcher</c>'s identical <c>JsonSerializer.Serialize(integrationEvent,
    /// integrationEvent.GetType())</c> precedent for the same reason. Declaring it as
    /// <see cref="System.Text.Json.JsonElement"/> here lets the surrounding envelope stay fully
    /// source-generated (<see cref="SendGridJsonContext"/>) while still embedding an arbitrary
    /// caller-supplied shape.
    /// </summary>
    [JsonPropertyName("dynamic_template_data")]
    public required System.Text.Json.JsonElement DynamicTemplateData { get; init; }
}

internal sealed record SendGridEmailAddress
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed record SendGridAttachment
{
    /// <summary>The attachment content, base64-encoded.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
