using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Extensions;

public sealed class TenantResolutionOptionsValidatorTests
{
    private static TenantResolutionOptionsValidator CreateValidator(params ITenantResolutionStrategy[] strategies)
    {
        var services = new ServiceCollection();
        foreach (var strategy in strategies)
        {
            services.AddSingleton(strategy);
            services.AddSingleton<ITenantResolutionStrategy>(strategy);
        }

        return new TenantResolutionOptionsValidator(services.BuildServiceProvider());
    }

    [Fact]
    public void Validate_EmptyStrategyOrder_Fails()
    {
        var validator = CreateValidator(new HeaderTenantResolutionStrategy());
        var options = new TenantResolutionOptions { StrategyOrder = [] };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(nameof(TenantResolutionOptions.StrategyOrder), result.FailureMessage);
    }

    [Fact]
    public void Validate_StrategyOrderEntryWithNoMatchingRegisteredStrategy_FailsAndNamesOffendingEntry()
    {
        var validator = CreateValidator(new HeaderTenantResolutionStrategy());
        var options = new TenantResolutionOptions
        {
            StrategyOrder = [TenantResolutionStrategyNames.Header, "TotallyBogusStrategyName"],
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("TotallyBogusStrategyName", result.FailureMessage);
    }

    [Fact]
    public void Validate_EveryEntryMatchesARegisteredStrategy_Succeeds()
    {
        var validator = CreateValidator(
            new HeaderTenantResolutionStrategy(),
            new ClaimTenantResolutionStrategy());
        var options = new TenantResolutionOptions
        {
            StrategyOrder = [TenantResolutionStrategyNames.Claim, TenantResolutionStrategyNames.Header],
        };

        var result = validator.Validate(null, options);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_CustomStrategyNameRegisteredInDI_IsRecognized()
    {
        // A genuine DI-aware cross-check, not a static allowlist of the three platform strategy
        // names — a consumer's own custom ITenantResolutionStrategy must be correctly recognized.
        var validator = CreateValidator(new CustomStrategy("Gateway"));
        var options = new TenantResolutionOptions { StrategyOrder = ["Gateway"] };

        var result = validator.Validate(null, options);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var validator = CreateValidator(new HeaderTenantResolutionStrategy());

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }

    private sealed class CustomStrategy(string strategyName) : ITenantResolutionStrategy
    {
        public string StrategyName => strategyName;

        public Task<Guid?> TryResolveAsync(Microsoft.AspNetCore.Http.HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);
    }
}
