using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Testing.Messaging;

namespace SharedKernel.Integration.Webhooks.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedKernelWebhooks_WithSubscriptionStoreRegistered_ResolvesDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IWebhookSubscriptionStore>(new FakeWebhookSubscriptionStore([]));
        services.AddInMemoryEventPublisher();
        services.AddSharedKernelWebhooks();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddSharedKernelWebhooks_WithoutSubscriptionStore_DispatcherResolutionFailsClearly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInMemoryEventPublisher();
        services.AddSharedKernelWebhooks();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IWebhookSubscriptionStore*");
    }

    [Fact]
    public void WithDeliveryObserver_CalledMultipleTimes_AccumulatesObservers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IWebhookSubscriptionStore>(new FakeWebhookSubscriptionStore([]));
        services.AddInMemoryEventPublisher();
        services.AddSharedKernelWebhooks();
        services.WithDeliveryObserver<RecordingObserverA>();
        services.WithDeliveryObserver<RecordingObserverB>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var observers = scope.ServiceProvider.GetServices<IWebhookDeliveryObserver>().ToList();

        observers.Should().HaveCount(2);
        observers.OfType<RecordingObserverA>().Should().ContainSingle();
        observers.OfType<RecordingObserverB>().Should().ContainSingle();
    }

    private sealed class RecordingObserverA : IWebhookDeliveryObserver
    {
        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingObserverB : IWebhookDeliveryObserver
    {
        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }
}
