using SharedKernel.Integration.Notifications.Abstractions.Notifications;

namespace SharedKernel.Integration.Notifications.Abstractions.Observability;

/// <summary>
/// A non-generic view of an in-flight notification send, passed to
/// <see cref="INotificationDeliveryObserver"/> so the observer contract does not need to be generic
/// over <c>TTemplateModel</c>.
/// </summary>
/// <param name="NotificationDeliveryId">The originating message's caller-supplied delivery id.</param>
/// <param name="Channel">The channel the message is being sent over.</param>
/// <param name="Recipient">
/// The recipient's address. Passed to observer CODE, not logged by this package — a consuming
/// observer implementation that logs it is that service's own responsibility/violation, not this
/// package's.
/// </param>
/// <param name="TemplateId">The template identifier being rendered.</param>
public sealed record NotificationDeliveryContext(
    Guid NotificationDeliveryId,
    NotificationChannel Channel,
    string Recipient,
    string TemplateId);
