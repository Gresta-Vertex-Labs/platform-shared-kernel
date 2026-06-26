using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Testing.Messaging;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Wires a full DI container for <see cref="WebhookDispatcher"/> with the named
/// <see cref="HttpClient"/>'s transport replaced by a <see cref="StubHttpMessageHandler"/> — no real
/// network call is ever made.
/// </summary>
internal sealed class WebhookTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public WebhookTestHarness(
        StubHttpMessageHandler handler,
        IWebhookSubscriptionStore subscriptionStore,
        Action<WebhookDeliveryOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(subscriptionStore);
        services.AddInMemoryEventPublisher();

        services.AddSharedKernelWebhooks(configure);

        services
            .AddHttpClient(WebhookHttpClientName.Name)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        configureServices?.Invoke(services);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public IWebhookDispatcher Dispatcher => _scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

    public InMemoryEventPublisher EventPublisher => (InMemoryEventPublisher)_scope.ServiceProvider.GetRequiredService<IEventPublisher>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
