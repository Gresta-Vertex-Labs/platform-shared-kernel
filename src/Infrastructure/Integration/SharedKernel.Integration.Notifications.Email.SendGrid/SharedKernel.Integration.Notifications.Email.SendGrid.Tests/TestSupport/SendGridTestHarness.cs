using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Options;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;
using SharedKernel.Integration.Notifications.Email.SendGrid.Options;
using SharedKernel.Storage;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;

/// <summary>
/// Wires a full DI container for <see cref="SendGridEmailNotificationSender"/> with the named
/// <see cref="HttpClient"/>'s transport replaced by a <see cref="StubHttpMessageHandler"/> — no real
/// network call is ever made. Uses two <see cref="InMemoryFileStorage"/> stores (<c>16.Testing</c>) for
/// attachment resolution and a <see cref="FakeNotificationSenderIdentityResolver"/> for the "from"
/// address seam.
/// </summary>
internal sealed class SendGridTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public SendGridTestHarness(
        HttpMessageHandler handler,
        Action<NotificationDeliveryOptions>? configureDelivery = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSharedKernelStorage().AddInMemoryStore(FileStorage).AddInMemoryTenantStore(TenantFileStorage);
        services.AddSingleton<INotificationSenderIdentityResolver, FakeNotificationSenderIdentityResolver>();
        services.AddInMemoryLoggerFactory();

        services.AddSharedKernelNotifications(configureDelivery);
        services.AddSendGridEmailNotifications(o => o.ApiKey = "test-api-key");

        services
            .AddHttpClient(SendGridHttpClientName.Name)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        configureServices?.Invoke(services);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    /// <summary>The shared store <c>invoices</c>.</summary>
    public InMemoryFileStorage FileStorage { get; } = new("invoices");

    /// <summary>The tenant-scoped store <c>documents</c>; its keys are <c>tenants/{tenantId}/{key}</c>.</summary>
    public InMemoryFileStorage TenantFileStorage { get; } = new("documents");

    public INotificationSender Sender =>
        _scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);

    public InMemoryLoggerFactory LoggerFactory => (InMemoryLoggerFactory)_scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
