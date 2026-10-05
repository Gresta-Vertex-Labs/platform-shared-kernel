using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Messaging;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Wires a full DI container for <see cref="WebhookDispatcher"/> with the named
/// <see cref="HttpClient"/>'s transport replaced by a <see cref="StubHttpMessageHandler"/> — no real
/// network call is ever made. Installs <see cref="AlwaysAllowWebhookUrlValidator"/> as the default
/// <c>IWebhookUrlValidator</c> so no test is affected by real DNS resolution unless it opts into a
/// different validator via <paramref name="configureServices"/>.
/// </summary>
internal sealed class WebhookTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public WebhookTestHarness(
        HttpMessageHandler handler,
        IWebhookSubscriptionStore subscriptionStore,
        Action<WebhookDeliveryOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(subscriptionStore);
        services.AddInMemoryEventPublisher();
        services.AddInMemoryLoggerFactory();

        services.AddSharedKernelWebhooks(configure);
        services.WithUrlValidator<AlwaysAllowWebhookUrlValidator>();

        services
            .AddHttpClient(WebhookHttpClientName.Name)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        configureServices?.Invoke(services);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public IWebhookDispatcher Dispatcher => _scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

    public InMemoryEventPublisher EventPublisher => (InMemoryEventPublisher)_scope.ServiceProvider.GetRequiredService<IEventPublisher>();

    public InMemoryLoggerFactory LoggerFactory => (InMemoryLoggerFactory)_scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
