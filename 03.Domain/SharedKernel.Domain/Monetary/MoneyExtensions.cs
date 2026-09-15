using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>Currency conversion and aggregation for <see cref="Money"/>.</summary>
public static class MoneyExtensions
{
    /// <summary>Converts <paramref name="money"/> into <paramref name="targetCurrency"/> at the rate from <paramref name="rateProvider"/>.</summary>
    /// <param name="money">The amount to convert.</param>
    /// <param name="targetCurrency">The currency to convert into.</param>
    /// <param name="rateProvider">Supplies the exchange rate.</param>
    /// <param name="roundingPolicy">How to round the converted amount. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    /// <param name="cancellationToken">Cancels the rate lookup.</param>
    /// <returns>The converted amount, or the provider's failure when no rate is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/>, <paramref name="targetCurrency"/> or <paramref name="rateProvider"/> is <see langword="null"/>.</exception>
    /// <remarks>Converting into the amount's own currency still asks the provider, which should return a rate of 1.</remarks>
    public static async Task<Result<Money>> ConvertAsync(
        this Money money,
        Currency targetCurrency,
        IExchangeRateProvider rateProvider,
        RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(targetCurrency);
        ArgumentNullException.ThrowIfNull(rateProvider);

        var rate = await rateProvider
            .GetExchangeRateAsync(money.Currency, targetCurrency, cancellationToken)
            .ConfigureAwait(false);

        return rate.IsFailure
            ? Result<Money>.Failure(rate.Error)
            : Result<Money>.Success(Money.FromTrusted(money.Amount * rate.Value, targetCurrency, roundingPolicy));
    }

    /// <summary>Returns the sum of a non-empty sequence of amounts in one currency.</summary>
    /// <param name="amounts">The amounts to add.</param>
    /// <returns>The total, in the currency of the amounts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="amounts"/> is null or contains null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="amounts"/> is empty; an empty sequence has no currency, so use <see cref="Money.Sum"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The amounts are in different currencies.</exception>
    public static Money Sum(this IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        using var enumerator = amounts.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException(
                "Cannot sum an empty sequence of money: it has no currency. Use Money.Sum(amounts, currency).");
        }

        var total = enumerator.Current ?? throw new ArgumentNullException(nameof(amounts), "The sequence contains a null amount.");
        while (enumerator.MoveNext())
            total = total.Add(enumerator.Current);
        return total;
    }

    /// <summary>Returns the sum of <paramref name="amounts"/>, all in <paramref name="currency"/>; zero when the sequence is empty.</summary>
    /// <param name="amounts">The amounts to add.</param>
    /// <param name="currency">The currency of the total.</param>
    /// <returns>The total.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="amounts"/> or <paramref name="currency"/> is null, or <paramref name="amounts"/> contains null.</exception>
    /// <exception cref="BusinessRuleViolationException">An amount is not in <paramref name="currency"/>.</exception>
    public static Money Sum(this IEnumerable<Money> amounts, Currency currency) => Money.Sum(amounts, currency);
}
