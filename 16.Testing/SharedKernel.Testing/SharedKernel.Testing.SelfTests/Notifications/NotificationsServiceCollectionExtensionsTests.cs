using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Testing.Notifications;

namespace SharedKernel.Testing.SelfTests.Notifications;

/// <summary>Proves <see cref="NotificationsServiceCollectionExtensions"/>'s DI registration shape.</summary>
public sealed class NotificationsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryNotificationSender_ResolvesKeyedInterfaceAndConcreteType_SameInstance()
    {
        var services = new ServiceCollection();
        services.AddInMemoryNotificationSender(NotificationChannel.Email);
        var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredKeyedService<InMemoryNotificationSender>(NotificationChannel.Email);
        var viaInterface = provider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);

        Assert.Same(concrete, viaInterface);
        Assert.Equal(NotificationChannel.Email, concrete.SupportedChannel);
    }

    [Fact]
    public void AddInMemoryNotificationSender_TwoChannels_ResolveIndependently()
    {
        var services = new ServiceCollection();
        services.AddInMemoryNotificationSender(NotificationChannel.Email);
        services.AddInMemoryNotificationSender(NotificationChannel.Sms);
        var provider = services.BuildServiceProvider();

        var email = provider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);
        var sms = provider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Sms);

        Assert.NotSame(email, sms);
        Assert.Equal(NotificationChannel.Email, email.SupportedChannel);
        Assert.Equal(NotificationChannel.Sms, sms.SupportedChannel);
    }

    [Fact]
    public void AddInMemoryNotificationDeliveryObserver_ResolvesInterfaceAndConcreteType_SameInstance()
    {
        var services = new ServiceCollection();
        services.AddInMemoryNotificationDeliveryObserver();
        var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<InMemoryNotificationDeliveryObserver>();
        var viaInterface = provider.GetRequiredService<INotificationDeliveryObserver>();

        Assert.Same(concrete, viaInterface);
    }

    [Fact]
    public void AddInMemoryNotificationSender_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => NotificationsServiceCollectionExtensions.AddInMemoryNotificationSender(null!, NotificationChannel.Email));
}
