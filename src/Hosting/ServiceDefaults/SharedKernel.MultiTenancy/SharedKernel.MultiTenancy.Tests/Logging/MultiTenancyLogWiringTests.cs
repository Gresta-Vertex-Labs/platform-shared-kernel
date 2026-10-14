using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Testing.Logging;

namespace SharedKernel.MultiTenancy.Tests.Logging;

/// <summary>
/// Covers <c>MultiTenancyLog</c>'s call-site wiring: this package's first-ever
/// production logging. Proves the log calls genuinely fire from
/// <see cref="TenantResolutionMiddleware.InvokeAsync"/> — not merely that
/// <c>MultiTenancyLog</c>'s <c>[LoggerMessage]</c> methods compile — via <c>16.Testing</c>'s
/// <see cref="InMemoryLoggerFactory"/> test double.
/// </summary>
public sealed class MultiTenancyLogWiringTests
{
    private static IOptions<TenantResolutionOptions> Options(params string[] order) =>
        Microsoft.Extensions.Options.Options.Create(new TenantResolutionOptions { StrategyOrder = order });

    private static (InMemoryLoggerFactory Factory, ILogger<TenantResolutionMiddleware> Logger) CreateLogging()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        var provider = services.BuildServiceProvider();
        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<TenantResolutionMiddleware>>();
        return (factory, logger);
    }

    [Fact]
    public async Task InvokeAsync_TenantResolves_LogsTenantResolvedWithStrategyNameAndNoTenantId()
    {
        var (factory, logger) = CreateLogging();
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Options(TenantResolutionStrategyNames.Header), logger);

        await middleware.InvokeAsync(context, [new HeaderTenantResolutionStrategy()]);

        var inMemoryLogger = factory.GetLogger(typeof(TenantResolutionMiddleware).FullName!);
        var record = inMemoryLogger.Records.ShouldHaveLogged(13100, LogLevel.Debug);
        record.TryGetProperty("StrategyName", out var strategyName).Should().BeTrue();
        strategyName.Should().Be(TenantResolutionStrategyNames.Header);
        record.TryGetProperty("TenantId", out _).Should().BeFalse("the tenant reaches the record ambiently, never as a placeholder");
        inMemoryLogger.Records.ShouldNotLeak(expectedTenantId.ToString(), expectedTenantId.ToString("N"));
        inMemoryLogger.Records.ShouldHaveLoggedTemplate(13100, "Resolved the tenant with the strategy '{StrategyName}'.");
        inMemoryLogger.Records.ShouldFollowLogConvention();
        inMemoryLogger.Records.ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.ServiceDefaults, 1);
        factory.ShouldHaveCategory(typeof(TenantResolutionMiddleware));
    }

    [Fact]
    public async Task InvokeAsync_NoStrategyResolves_LogsTenantNotResolved()
    {
        var (factory, logger) = CreateLogging();
        var context = new DefaultHttpContext();

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Options(TenantResolutionStrategyNames.Header), logger);

        await middleware.InvokeAsync(context, [new HeaderTenantResolutionStrategy()]);

        var inMemoryLogger = factory.GetLogger(typeof(TenantResolutionMiddleware).FullName!);
        inMemoryLogger.Records.ShouldHaveLogged(13101, LogLevel.Trace);
        inMemoryLogger.Records.ShouldNotHaveLogged(13100);
        inMemoryLogger.Records.ShouldHaveLoggedTemplate(13101, "No tenant resolved for the current request.");
        inMemoryLogger.Records.ShouldFollowLogConvention();
        inMemoryLogger.Records.ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.ServiceDefaults, 1);
    }

    [Fact]
    public async Task InvokeAsync_TenantStatusValidatorRejects_LogsTenantNotResolved_NotTenantResolved()
    {
        // An inactive/suspended tenant reuses TenantNotResolved rather than a
        // distinct message — deliberately indistinguishable from an absent one in logs.
        var (factory, logger) = CreateLogging();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStatusValidator>(new RejectingTenantStatusValidator());
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Options(TenantResolutionStrategyNames.Header), logger);

        await middleware.InvokeAsync(context, [new HeaderTenantResolutionStrategy()]);

        var inMemoryLogger = factory.GetLogger(typeof(TenantResolutionMiddleware).FullName!);
        inMemoryLogger.Records.ShouldHaveLogged(13101, LogLevel.Trace);
        inMemoryLogger.Records.ShouldNotHaveLogged(13100);
    }

    private sealed class RejectingTenantStatusValidator : ITenantStatusValidator
    {
        public Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct) => Task.FromResult(false);
    }
}
