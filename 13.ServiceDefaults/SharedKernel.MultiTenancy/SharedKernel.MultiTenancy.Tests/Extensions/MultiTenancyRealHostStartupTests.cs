using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Tests.Extensions;

/// <summary>
/// Covers WO-061/P-396's GATING acceptance criterion for
/// <see cref="TenantResolutionOptionsValidator"/>: a misconfigured <see cref="TenantResolutionOptions.StrategyOrder"/>
/// must fail a genuine <see cref="IHost.StartAsync"/> call — not merely resolving
/// <see cref="IOptions{TOptions}"/> against a bare <see cref="ServiceProvider"/> in isolation. This is
/// the stronger acceptance-criterion form <see cref="MultiTenancyExtensionsTests"/>'s
/// resolve-IOptions-directly tests do not, by themselves, prove: that a real host built the ordinary
/// way (<see cref="Host.CreateApplicationBuilder()"/> → <c>.Build()</c> → <c>.StartAsync()</c>) is the
/// thing that actually throws.
/// </summary>
public sealed class MultiTenancyRealHostStartupTests
{
    private static HostApplicationBuilder CreateBuilderWithConnectionFactory()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(Substitute.For<IDbConnectionFactory>());
        return builder;
    }

    [Fact]
    public async Task RealHost_EmptyStrategyOrder_StartAsyncThrowsOptionsValidationExceptionNamingStrategyOrder()
    {
        var builder = CreateBuilderWithConnectionFactory();
        builder.Services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = []);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(TenantResolutionOptions.StrategyOrder), exception.Message);
    }

    [Fact]
    public async Task RealHost_StrategyOrderNamesUnregisteredStrategy_StartAsyncThrowsOptionsValidationExceptionNamingOffendingEntry()
    {
        var builder = CreateBuilderWithConnectionFactory();
        builder.Services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = ["TotallyBogusStrategyName"]);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("TotallyBogusStrategyName", exception.Message);
    }

    [Fact]
    public async Task RealHost_DefaultStrategyOrder_StartsAsyncCleanly()
    {
        var builder = CreateBuilderWithConnectionFactory();
        builder.Services.AddSharedKernelMultiTenancy();
        using var host = builder.Build();

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(exception);
        await host.StopAsync();
    }

    [Fact]
    public async Task RealHost_StrategyOrderOmittingDatabase_StartsAsyncCleanly()
    {
        // The existing documented minimal-strategy-set pattern: a service with no tenant directory
        // omits "Database" from StrategyOrder. IDbConnectionFactory must still be registered (even
        // as a test double) because AddSharedKernelMultiTenancy unconditionally registers
        // DatabaseTenantResolutionStrategy regardless of StrategyOrder's contents — the validator
        // enumerates every registered ITenantResolutionStrategy, not just the configured ones.
        var builder = CreateBuilderWithConnectionFactory();
        builder.Services.AddSharedKernelMultiTenancy(o =>
            o.StrategyOrder = [TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Claim]);
        using var host = builder.Build();

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(exception);
        await host.StopAsync();
    }
}
