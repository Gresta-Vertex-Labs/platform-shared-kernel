using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.Localization;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Tests.Localization;

/// <summary>
/// Covers WO-078/P-483's <c>AddSharedKernelLocalization</c> precedence-ordered culture
/// resolution and its startup misconfiguration warning.
/// </summary>
public sealed class SharedKernelLocalizationExtensionsTests
{
    [Fact]
    public void AddSharedKernelLocalization_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddSharedKernelLocalization());
    }

    [Fact]
    public async Task ResolvedCulture_UserPreferenceClaim_WinsOverTenantDefaultAndAcceptLanguageHeader()
    {
        var tenantId = Guid.NewGuid();

        var userContext = Substitute.For<IUserContext>();
        userContext.Claims.Returns(new Dictionary<string, string> { ["culture"] = "de-DE" });

        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.TenantId.Returns(tenantId);

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(
            new TenantDescriptor(tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, "fr-FR", new Dictionary<string, string>()));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(userContext);
        builder.Services.AddSingleton(tenantProvider);
        builder.Services.AddSingleton(catalog);
        builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "culture");

        using var provider = builder.Services.BuildServiceProvider();
        var requestLocalizationOptions = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Headers["Accept-Language"] = "es-ES";

        var resolvedCulture = await ResolveAsync(requestLocalizationOptions, httpContext);

        Assert.NotNull(resolvedCulture);
        Assert.Equal("de-DE", resolvedCulture.Cultures[0].Value);
    }

    [Fact]
    public async Task ResolvedCulture_NoTenantCatalogRegistered_TenantDefaultStepSkipsCleanly_FallsThroughToAcceptLanguageHeader()
    {
        var builder = Host.CreateApplicationBuilder();
        // No ITenantCatalog, no IUserContext registered at all — both the UserPreference and
        // TenantDefault steps must skip cleanly without throwing.
        builder.AddSharedKernelLocalization();

        using var provider = builder.Services.BuildServiceProvider();
        var requestLocalizationOptions = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Headers["Accept-Language"] = "es-ES";

        var resolvedCulture = await ResolveAsync(requestLocalizationOptions, httpContext);

        Assert.NotNull(resolvedCulture);
        Assert.Equal("es-ES", resolvedCulture.Cultures[0].Value);
    }

    [Fact]
    public async Task StartupWarning_FiresExactlyOnce_WhenNeitherUserPreferenceClaimTypeNorTenantCatalogConfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        // Neither .UserPreferenceClaimType nor an ITenantCatalog registration.
        builder.AddSharedKernelLocalization();
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
            var inMemoryLogger = loggerFactory.GetLogger(
                "SharedKernel.ServiceDefaults.Localization.RequestCultureResolution");
            inMemoryLogger.Records.ShouldHaveLoggedCount(13004, 1);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StartupWarning_DoesNotFire_WhenUserPreferenceClaimTypeConfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "culture");
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
            var inMemoryLogger = loggerFactory.GetLogger(
                "SharedKernel.ServiceDefaults.Localization.RequestCultureResolution");
            inMemoryLogger.Records.ShouldHaveLoggedCount(13004, 0);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StartupWarning_DoesNotFire_WhenTenantCatalogRegistered()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        builder.Services.AddSingleton(Substitute.For<ITenantCatalog>());
        builder.AddSharedKernelLocalization();
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
            var inMemoryLogger = loggerFactory.GetLogger(
                "SharedKernel.ServiceDefaults.Localization.RequestCultureResolution");
            inMemoryLogger.Records.ShouldHaveLoggedCount(13004, 0);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<ProviderCultureResult?> ResolveAsync(
        RequestLocalizationOptions options,
        HttpContext httpContext)
    {
        foreach (var provider in options.RequestCultureProviders)
        {
            var result = await provider.DetermineProviderCultureResult(httpContext).ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
