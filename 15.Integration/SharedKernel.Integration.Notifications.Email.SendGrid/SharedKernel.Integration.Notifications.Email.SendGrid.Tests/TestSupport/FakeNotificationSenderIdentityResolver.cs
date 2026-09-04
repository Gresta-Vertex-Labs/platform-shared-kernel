using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;

internal sealed class FakeNotificationSenderIdentityResolver(NotificationSenderIdentity identity) : INotificationSenderIdentityResolver
{
    public FakeNotificationSenderIdentityResolver()
        : this(new NotificationSenderIdentity("no-reply@example.test", "Example Co", "support@example.test"))
    {
    }

    public Task<NotificationSenderIdentity> ResolveAsync(NotificationChannel channel, CancellationToken ct) =>
        Task.FromResult(identity);
}
