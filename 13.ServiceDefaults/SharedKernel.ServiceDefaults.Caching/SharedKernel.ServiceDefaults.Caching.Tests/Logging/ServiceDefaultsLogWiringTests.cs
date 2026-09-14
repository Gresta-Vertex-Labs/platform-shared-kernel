using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Caching.Tests.Logging;

/// <summary>
/// Covers WO-061/P-395's <c>ServiceDefaultsLog</c> call-site wiring: this domain's first-ever
/// production logging. Proves the log calls genuinely fire from the real production code paths —
/// not merely that <c>ServiceDefaultsLog</c>'s <c>[LoggerMessage]</c> methods compile — via
/// <c>16.Testing</c>'s <see cref="InMemoryLoggerFactory"/> test double.
/// </summary>
public sealed class ServiceDefaultsLogWiringTests
{
    [Fact]
    public void AddCacheReadinessCheck_ResolvingHealthCheckOptions_LogsHealthCheckRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        var healthChecksBuilder = services.AddSharedKernelHealthChecks();
        healthChecksBuilder.AddCacheReadinessCheck();

        using var provider = services.BuildServiceProvider();

        // Forces IOptionsFactory<HealthCheckServiceOptions>.Create() to run — the same lazy
        // evaluation ValidateOnStart/HealthCheckService itself triggers, never eagerly forced by
        // this helper on its own.
        _ = provider.GetRequiredService<IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.HealthChecks.CacheReadinessHealthCheckExtensions");
        var record = inMemoryLogger.Records.ShouldHaveLogged(13002, LogLevel.Information);
        record.TryGetProperty("HealthCheckName", out var name).Should().BeTrue();
        name.Should().Be(HealthCheckNames.Cache);
    }

    [Fact]
    public void AddCacheReadinessCheck_ResolvingHealthCheckOptionsRepeatedly_LogsHealthCheckRegisteredExactlyOnce()
    {
        // "Fires once per registered check, never per probe invocation" — IOptions<T> caches its
        // computed value, so repeated resolution must not refire the PostConfigure log call.
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        var healthChecksBuilder = services.AddSharedKernelHealthChecks();
        healthChecksBuilder.AddCacheReadinessCheck();

        using var provider = services.BuildServiceProvider();
        var optionsAccessor = provider.GetRequiredService<IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>();
        _ = optionsAccessor.Value;
        _ = optionsAccessor.Value;
        _ = optionsAccessor.Value;

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.HealthChecks.CacheReadinessHealthCheckExtensions");
        inMemoryLogger.Records.ShouldHaveLoggedCount(13002, 1);
    }

    [Fact]
    public async Task AddCacheReadinessCheck_RealHealthCheckServiceInvokedRepeatedly_LogsHealthCheckRegisteredExactlyOnce()
    {
        // T-53: HealthCheckRegistered fires once per Add*Check registration call, never once per
        // probe invocation — proven against a real HealthCheckService.CheckHealthAsync() invoked
        // several times, not merely repeated IOptions<T> resolution (the weaker form the two tests
        // above already cover).
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton(Substitute.For<SharedKernel.Caching.Abstractions.ICacheService>());
        var healthChecksBuilder = services.AddSharedKernelHealthChecks();
        healthChecksBuilder.AddCacheReadinessCheck();

        using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();

        await healthCheckService.CheckHealthAsync();
        await healthCheckService.CheckHealthAsync();
        await healthCheckService.CheckHealthAsync();

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.HealthChecks.CacheReadinessHealthCheckExtensions");
        inMemoryLogger.Records.ShouldHaveLoggedCount(13002, 1);
    }
}
