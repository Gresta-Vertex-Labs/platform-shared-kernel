using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-46: P-439g/WO-066 — <see cref="IExchangeRateProvider"/> / <see cref="MoneyExtensions.ConvertAsync"/> tests.
/// </summary>
public class MoneyConversionTests
{
    private sealed class FixedRateProvider(decimal rate) : IExchangeRateProvider
    {
        public Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken) =>
            Task.FromResult(Result<decimal>.Success(rate));
    }

    private sealed class FailingRateProvider(Error error) : IExchangeRateProvider
    {
        public Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken) =>
            Task.FromResult(Result<decimal>.Failure(error));
    }

    [Fact]
    public async Task ConvertAsync_ComposesRateWithMoneyCreate()
    {
        var usd10 = Money.Create(10.00m, Currency.Usd).Value;
        var provider = new FixedRateProvider(0.92m);

        var result = await usd10.ConvertAsync(Currency.Eur, provider);

        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Should().Be(Currency.Eur);
        result.Value.Amount.Should().Be(9.20m);
    }

    [Fact]
    public async Task ConvertAsync_QueriesProviderWithSourceAndTargetCurrency()
    {
        Currency? seenSource = null;
        Currency? seenTarget = null;

        var probe = new DelegateRateProvider((source, target) =>
        {
            seenSource = source;
            seenTarget = target;
            return Result<decimal>.Success(1m);
        });

        var usd = Money.Create(10.00m, Currency.Usd).Value;
        await usd.ConvertAsync(Currency.Gbp, probe);

        seenSource.Should().Be(Currency.Usd);
        seenTarget.Should().Be(Currency.Gbp);
    }

    private sealed class DelegateRateProvider(Func<Currency, Currency, Result<decimal>> resolve) : IExchangeRateProvider
    {
        public Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken) =>
            Task.FromResult(resolve(source, target));
    }

    [Fact]
    public async Task ConvertAsync_ProviderFailure_ShortCircuitsToFailure_WithoutConstructingMoney()
    {
        var error = Error.Unexpected("rate.unavailable", "Rate feed is down.");
        var usd10 = Money.Create(10.00m, Currency.Usd).Value;
        var provider = new FailingRateProvider(error);

        var result = await usd10.ConvertAsync(Currency.Eur, provider);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task ConvertAsync_RespectsSuppliedRoundingPolicy()
    {
        var usd = Money.Create(1.00m, Currency.Usd).Value;
        var provider = new FixedRateProvider(1.125m); // produces an exact 2-decimal midpoint

        var bankers = await usd.ConvertAsync(Currency.Eur, provider, RoundingPolicy.BankersRounding);
        var awayFromZero = await usd.ConvertAsync(Currency.Eur, provider, RoundingPolicy.AwayFromZero);

        bankers.Value.Amount.Should().Be(1.12m);
        awayFromZero.Value.Amount.Should().Be(1.13m);
    }
}
