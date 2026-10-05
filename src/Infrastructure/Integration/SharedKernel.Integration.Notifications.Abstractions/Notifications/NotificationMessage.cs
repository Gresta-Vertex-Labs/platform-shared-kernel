namespace SharedKernel.Integration.Notifications.Abstractions.Notifications;

/// <summary>
/// A single templated notification to send to one recipient over one <see cref="NotificationChannel"/>.
/// </summary>
/// <typeparam name="TTemplateModel">The strongly-typed model bound into the named template.</typeparam>
/// <remarks>Pure DTO, no behavior — mirrors <c>WebhookSubscription</c>'s shape discipline.</remarks>
public sealed record NotificationMessage<TTemplateModel>
{
    /// <summary>
    /// Caller-supplied, required. Doubles as the sending provider's own dedup/idempotency key.
    /// </summary>
    /// <remarks>
    /// Unlike <c>WebhookDeliveryResult.DeliveryId</c> — generated internally by
    /// <c>WebhookDispatcher</c> because an entire webhook delivery, retries included, happens inside
    /// one dispatcher call — this identifier must survive a caller-level crash-and-retry. A retry
    /// after a crash reuses the same id so the provider's own dedup mechanism (see
    /// <see cref="INotificationSender"/>) actually prevents a double-send. Mirrors
    /// <c>05.Application</c>'s <c>IIdempotentRequest</c> idempotency-key convention, one layer
    /// further out. NEVER generate this value inside a provider's <c>SendAsync</c> implementation.
    /// </remarks>
    public required Guid NotificationDeliveryId { get; init; }

    /// <summary>The delivery channel this message is sent through.</summary>
    public required NotificationChannel Channel { get; init; }

    /// <summary>
    /// The recipient's email address or E.164 phone number, depending on <see cref="Channel"/>.
    /// </summary>
    /// <remarks>
    /// PII. NEVER PASSED AS A <c>[LoggerMessage]</c> TEMPLATE PLACEHOLDER, AN EXCEPTION MESSAGE, OR
    /// AN <c>Activity</c> TAG ANYWHERE IN THIS PACKAGE OR EITHER PROVIDER PACKAGE — mirrors
    /// <c>10.Intelligence</c>'s "prompt/completion text is never a log-message parameter" precedent.
    /// </remarks>
    public required string Recipient { get; init; }

    /// <summary>The provider-side template identifier to render.</summary>
    public required string TemplateId { get; init; }

    /// <summary>
    /// The strongly-typed model bound into the named template — never string concatenation at the
    /// call site.
    /// </summary>
    /// <remarks>
    /// PII-bearing fields on this model MUST NOT be passed as a <c>[LoggerMessage]</c> placeholder
    /// either — the same discipline as <see cref="Recipient"/>.
    /// </remarks>
    public required TTemplateModel TemplateModel { get; init; }

    /// <summary>
    /// Forward-compatible seam only — no logic in this package or either shipped provider consumes
    /// this value yet. Reserved for future composition with <c>SharedKernel.Localization</c>.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Optional override; falls back to the per-tenant resolved
    /// <see cref="Observability.NotificationSenderIdentity.ReplyTo"/> when <see langword="null"/>.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>Optional object-storage-backed attachments. <see langword="null"/> or empty when none.</summary>
    public IReadOnlyList<NotificationAttachment>? Attachments { get; init; }
}
