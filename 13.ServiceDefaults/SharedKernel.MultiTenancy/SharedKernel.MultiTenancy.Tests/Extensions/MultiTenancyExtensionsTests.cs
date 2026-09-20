using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.MultiTenancy.Tests.Extensions;

public sealed class MultiTenancyExtensionsTests
{
    [Fact]
    public void AddSharedKernelMultiTenancy_RegistersAmbientTenantProviderAsScopedTenantProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        Assert.IsType<AmbientTenantProvider>(tenantProvider);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_RegistersAllThreeStrategies()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var strategies = scope.ServiceProvider.GetServices<ITenantResolutionStrategy>().ToList();

        Assert.Contains(strategies, s => s is HeaderTenantResolutionStrategy);
        Assert.Contains(strategies, s => s is ClaimTenantResolutionStrategy);
        Assert.Contains(strategies, s => s is DatabaseTenantResolutionStrategy);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_ConfiguresOptions_WhenConfigureDelegateProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = ["Header"]);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value;

        Assert.Equal(["Header"], options.StrategyOrder);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_DoesNotRegisterMiddleware()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy();

        Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(TenantResolutionMiddleware));
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_DefaultStrategyOrder_ResolvesOptionsWithoutThrowing()
    {
        // The default StrategyOrder ([Claim, Header, Database]) must pass its own
        // startup validation against the three strategies this method itself
        // registers — resolving IOptions<T>.Value forces the same IValidateOptions pipeline
        // .ValidateOnStart() runs eagerly at real host startup.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy();

        using var provider = services.BuildServiceProvider();
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value);

        Assert.Null(exception);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_StrategyOrderNamesUnregisteredStrategy_ResolvingOptionsThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = ["TotallyBogusStrategyName"]);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_EmptyStrategyOrder_ResolvesToDefaultOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = []);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value;

        Assert.Equal(TenantResolutionOptions.DefaultStrategyOrder, options.EffectiveStrategyOrder);
    }

    [Fact]
    public void AddSharedKernelMultiTenancy_DuplicateStrategyOrderEntry_ResolvingOptionsThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());

        services.AddSharedKernelMultiTenancy(o => o.StrategyOrder = ["Claim", "Header", "Claim"]);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value);
        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }
}
