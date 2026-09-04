using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Options;

namespace SharedKernel.Integration.Notifications.Abstractions.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return services;
    }

    [Fact]
    public void AddSharedKernelNotifications_RegistersValidatedOptions()
    {
        var services = NewServices();
        services.AddSharedKernelNotifications(o => o.MaxAttempts = 7);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NotificationDeliveryOptions>>().Value;

        options.MaxAttempts.Should().Be(7);
    }

    [Fact]
    public void AddSharedKernelNotifications_DoesNotRegisterSenderIdentityResolver()
    {
        var services = NewServices();
        services.AddSharedKernelNotifications();

        using var provider = services.BuildServiceProvider();

        provider.GetService<INotificationSenderIdentityResolver>().Should().BeNull(
            "the consuming service must register its own resolver — this package ships no default");
    }

    private sealed class FirstObserver : INotificationDeliveryObserver
    {
        public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SecondObserver : INotificationDeliveryObserver
    {
        public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void WithNotificationDeliveryObserver_MultipleCalls_AreAdditive()
    {
        var services = NewServices();
        services.AddSharedKernelNotifications();
        services.WithNotificationDeliveryObserver<FirstObserver>();
        services.WithNotificationDeliveryObserver<SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var observers = provider.GetServices<INotificationDeliveryObserver>().ToList();

        observers.Should().HaveCount(2);
        observers.Should().ContainSingle(o => o is FirstObserver);
        observers.Should().ContainSingle(o => o is SecondObserver);
    }
}
