using SharedKernel.Domain.Monetary;
using SharedKernel.Testing.Domain;

namespace SharedKernel.Testing.SelfTests.Domain;

/// <summary>
/// Proves <see cref="FakeExchangeRateProvider"/> against <c>IExchangeRateProvider</c>'s documented
/// contract — no consuming domain has adopted this fake yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeExchangeRateProviderTests
{
    [Fact]
    public async Task GetExchangeRateAsync_SeededPair_ReturnsSuccessWithRate()
    {
        var provider = new FakeExchangeRateProvider();
        provider.SeedRate(Currency.Usd, Currency.Eur, 0.92m);

        var result = await provider.GetExchangeRateAsync(Currency.Usd, Currency.Eur, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.92m, result.Value);
    }

    [Fact]
    public async Task GetExchangeRateAsync_UnseededPair_ReturnsFailure_NeverThrows()
    {
        var provider = new FakeExchangeRateProvider();

        var result = await provider.GetExchangeRateAsync(Currency.Usd, Currency.Eur, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetExchangeRateAsync_SimulateFailure_AlwaysFails_EvenWhenSeeded()
    {
        var provider = new FakeExchangeRateProvider { SimulateFailure = true };
        provider.SeedRate(Currency.Usd, Currency.Eur, 0.92m);

        var result = await provider.GetExchangeRateAsync(Currency.Usd, Currency.Eur, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetExchangeRateAsync_DirectionMatters_ReverseNotAutoDerived()
    {
        var provider = new FakeExchangeRateProvider();
        provider.SeedRate(Currency.Usd, Currency.Eur, 0.92m);

        var reverse = await provider.GetExchangeRateAsync(Currency.Eur, Currency.Usd, CancellationToken.None);

        Assert.True(reverse.IsFailure);
    }

    [Fact]
    public void SeedRate_NullSource_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeExchangeRateProvider().SeedRate(null!, Currency.Usd, 1m));

    [Fact]
    public async Task ConvertAsync_ComposesWithMoneyExtensions_ProducesConvertedAmount()
    {
        var provider = new FakeExchangeRateProvider();
        provider.SeedRate(Currency.Usd, Currency.Eur, 0.5m);
        var tenDollars = Money.Create(10m, Currency.Usd).Value;

        var result = await tenDollars.ConvertAsync(Currency.Eur, provider);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value.Amount);
        Assert.Equal(Currency.Eur, result.Value.Currency);
    }
}
