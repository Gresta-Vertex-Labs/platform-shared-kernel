using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Options;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;
using SharedKernel.Integration.Notifications.Email.SendGrid.Options;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;

/// <summary>
/// Wires a full DI container for <see cref="SendGridEmailNotificationSender"/> with the named
/// <see cref="HttpClient"/>'s transport replaced by a <see cref="StubHttpMessageHandler"/> — no real
/// network call is ever made. Uses <see cref="InMemoryFileStorage"/> (<c>16.Testing</c>) for
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
        services.AddSingleton<IFileStorage>(FileStorage);
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

    public InMemoryFileStorage FileStorage { get; } = new();

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
