using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Messaging.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddMessagingReadinessCheck_DefaultName_MatchesHealthCheckNamesMessaging()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageBusProbe>());

        services.AddHealthChecks().AddMessagingReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Messaging);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
