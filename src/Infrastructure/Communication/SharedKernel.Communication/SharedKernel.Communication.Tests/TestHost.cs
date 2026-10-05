using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Tests;

/// <summary>Builds service collections the way a host would, from in-memory configuration.</summary>
internal static class TestHost
{
    public static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    public static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>Runs every <c>ValidateOnStart</c> registration, as <c>IHost.StartAsync</c> does.</summary>
    public static void ValidateOnStart(this IServiceProvider provider) =>
        provider.GetRequiredService<IStartupValidator>().Validate();
}
