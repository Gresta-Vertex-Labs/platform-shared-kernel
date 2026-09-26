using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFeature;
using OpenFeature.Hosting;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>Builds a real service provider over the package, initialized as a host would.</summary>
internal static class FeatureTestHost
{
    public static IConfigurationRoot Json(string json) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

    public static IConfigurationRoot InMemory(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    public static async Task<ServiceProvider> StartAsync(
        IConfiguration configuration,
        Action<FeatureFlagOptions>? configure = null,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        before?.Invoke(services);
        services.AddSharedKernelFeatureManagement(configuration, configure);
        after?.Invoke(services);

        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.GetRequiredService<IFeatureLifecycleManager>().EnsureInitializedAsync();
        return provider;
    }

    /// <summary>A client from a new scope, as a request handler would get one.</summary>
    public static IFeatureClient NewScopeClient(this IServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IFeatureClient>();
}

/// <summary>A fixed accessor for the current caller.</summary>
internal sealed class StaticTargetingAccessor(FeatureTargetingContext? context) : IFeatureTargetingContextAccessor
{
    public FeatureTargetingContext? GetTargetingContext() => context;
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Captures log entries with their EventId.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(EventId EventId, LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((eventId, logLevel, formatter(state, exception), exception));
    }
}

public sealed record CheckoutSettings(int Steps, bool ExpressPay, string Title, IReadOnlyList<string> Providers, string Code);

[JsonSerializable(typeof(CheckoutSettings))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>
/// Tenants used across the tests. The <c>*Text</c> constants are <see cref="TenantId.ToString()"/> of each tenant,
/// which is how a tenant appears as a group name in flag configuration.
/// </summary>
internal static class TestTenants
{
    public const string AcmeText = "0f8fad5b-d9cb-469f-a165-70867728950e";
    public const string OtherText = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    public static readonly TenantId Acme = TenantId.Parse(AcmeText);
    public static readonly TenantId Other = TenantId.Parse(OtherText);
}
