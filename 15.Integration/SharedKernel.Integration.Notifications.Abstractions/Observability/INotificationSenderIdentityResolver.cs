using SharedKernel.Integration.Notifications.Abstractions.Notifications;

namespace SharedKernel.Integration.Notifications.Abstractions.Observability;

/// <summary>
/// Per-tenant/per-channel sender-identity ("from" address, display name, reply-to) bridge seam.
/// </summary>
/// <remarks>
/// Implemented by the consuming service — there is no default implementation in this package, and
/// <c>AddSharedKernelNotifications</c> deliberately does not register one. Bridged at the consuming
/// service's own composition root against its own tenant catalog/config — never a direct
/// persistence/<c>13.ServiceDefaults</c> reference from this package, mirroring
/// <c>05.Application</c>'s <c>IRequestContext</c> bridge pattern.
/// </remarks>
public interface INotificationSenderIdentityResolver
{
    /// <summary>Resolves the sender identity to use for a send over <paramref name="channel"/>.</summary>
    /// <param name="channel">The channel the identity is being resolved for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<NotificationSenderIdentity> ResolveAsync(NotificationChannel channel, CancellationToken ct);
}

/// <summary>The resolved sender identity for a given channel.</summary>
/// <param name="FromAddress">The "from" email address, or the SMS sender number/short-code/alphanumeric ID.</param>
/// <param name="DisplayName">The sender's display name, when applicable to the channel.</param>
/// <param name="ReplyTo">
/// The default reply-to address, used when a <c>NotificationMessage.ReplyTo</c> override is not
/// supplied.
/// </param>
public sealed record NotificationSenderIdentity(string FromAddress, string? DisplayName = null, string? ReplyTo = null);
