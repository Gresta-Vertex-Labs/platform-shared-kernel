using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Testing.Integration;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Integration;

public sealed class IntegrationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryWebhookDispatcher_ResolvesIWebhookDispatcher_AsInMemoryWebhookDispatcher()
    {
        var services = new ServiceCollection();
        services.AddInMemoryWebhookDispatcher();
        var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryWebhookDispatcher>(provider.GetRequiredService<IWebhookDispatcher>());
    }

    [Fact]
    public void AddInMemoryWebhookDispatcher_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddInMemoryWebhookDispatcher();
        var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IWebhookDispatcher>(), provider.GetRequiredService<IWebhookDispatcher>());
        Assert.Same(
            provider.GetRequiredService<IWebhookDispatcher>(),
            provider.GetRequiredService<InMemoryWebhookDispatcher>());
    }

    [Fact]
    public void AddInMemoryWebhookDispatcher_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryWebhookDispatcher());

    [Fact]
    public void AddInMemoryWebhookDeliveryObserver_ResolvesIWebhookDeliveryObserver_AsInMemoryWebhookDeliveryObserver()
    {
        var services = new ServiceCollection();
        services.AddInMemoryWebhookDeliveryObserver();
        var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryWebhookDeliveryObserver>(provider.GetRequiredService<IWebhookDeliveryObserver>());
    }

    [Fact]
    public void AddInMemoryWebhookDeliveryObserver_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddInMemoryWebhookDeliveryObserver();
        var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IWebhookDeliveryObserver>(),
            provider.GetRequiredService<IWebhookDeliveryObserver>());
        Assert.Same(
            provider.GetRequiredService<IWebhookDeliveryObserver>(),
            provider.GetRequiredService<InMemoryWebhookDeliveryObserver>());
    }

    [Fact]
    public void AddInMemoryWebhookDeliveryObserver_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryWebhookDeliveryObserver());
}
