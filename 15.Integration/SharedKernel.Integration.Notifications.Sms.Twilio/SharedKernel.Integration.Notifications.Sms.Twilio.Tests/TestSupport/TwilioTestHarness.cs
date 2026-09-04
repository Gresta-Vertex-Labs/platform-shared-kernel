using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Options;
using SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Tests.TestSupport;

/// <summary>
/// Wires a full DI container for <see cref="TwilioSmsNotificationSender"/> with the named
/// <see cref="HttpClient"/>'s transport replaced by a <see cref="StubHttpMessageHandler"/> — no real
/// network call is ever made.
/// </summary>
internal sealed class TwilioTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public TwilioTestHarness(
        HttpMessageHandler handler,
        Action<NotificationDeliveryOptions>? configureDelivery = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInMemoryLoggerFactory();

        services.AddSharedKernelNotifications(configureDelivery);
        services.AddTwilioSmsNotifications(o =>
        {
            o.AccountSid = "ACtest";
            o.AuthToken = "test-auth-token";
            o.From = "+15005550006";
        });

        services
            .AddHttpClient(TwilioHttpClientName.Name)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        configureServices?.Invoke(services);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public INotificationSender Sender =>
        _scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Sms);

    public InMemoryLoggerFactory LoggerFactory => (InMemoryLoggerFactory)_scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
