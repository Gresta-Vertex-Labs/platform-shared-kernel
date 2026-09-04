using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.Localization;

namespace SharedKernel.ServiceDefaults.Tests.Localization;

/// <summary>
/// Proves <c>AddSharedKernelLocalization</c> configures the real
/// <see cref="RequestLocalizationOptions"/>/<c>RequestLocalizationMiddleware</c> — never a
/// hand-rolled reimplementation — and that a host which never calls it is unaffected.
/// </summary>
public sealed class SharedKernelLocalizationWrapsRequestLocalizationMiddlewareTests
{
    [Fact]
    public void CustomProviders_ArePresent_AheadOfTheBclAcceptLanguageHeaderProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddSharedKernelLocalization();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        var providers = options.RequestCultureProviders;
        Assert.Equal(3, providers.Count);
        Assert.IsType<UserPreferenceRequestCultureProvider>(providers[0]);
        Assert.IsType<TenantDefaultRequestCultureProvider>(providers[1]);
        Assert.IsType<AcceptLanguageHeaderRequestCultureProvider>(providers[2]);

        var acceptLanguageIndex = providers.ToList().FindIndex(p => p is AcceptLanguageHeaderRequestCultureProvider);
        var userPreferenceIndex = providers.ToList().FindIndex(p => p is UserPreferenceRequestCultureProvider);
        var tenantDefaultIndex = providers.ToList().FindIndex(p => p is TenantDefaultRequestCultureProvider);

        Assert.True(userPreferenceIndex < acceptLanguageIndex);
        Assert.True(tenantDefaultIndex < acceptLanguageIndex);
    }

    [Fact]
    public async Task HostThatNeverCallsAddSharedKernelLocalization_IsByteIdenticalToToday_NoOpRegression()
    {
        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(exception);
        await host.StopAsync();

        // The BCL's own untouched default — this platform never registered against
        // RequestLocalizationOptions at all in this scenario.
        var options = host.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        Assert.DoesNotContain(options.RequestCultureProviders, p => p is UserPreferenceRequestCultureProvider);
        Assert.DoesNotContain(options.RequestCultureProviders, p => p is TenantDefaultRequestCultureProvider);
    }
}
