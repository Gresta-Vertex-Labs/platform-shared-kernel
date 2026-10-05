using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>Extension methods for <see cref="Money"/>: currency conversion and summing sequences of amounts.</summary>
public static class MoneyExtensions
{
    /// <summary>
    /// Converts <paramref name="money"/> into <paramref name="targetCurrency"/> at the rate from
    /// <paramref name="rateProvider"/>, rounded to the target currency's minor unit.
    /// </summary>
    /// <param name="money">The amount to convert. Must not be <see langword="null"/>.</param>
    /// <param name="targetCurrency">The currency to convert into. Must not be <see langword="null"/>.</param>
    /// <param name="rateProvider">The source of the exchange rate. Must not be <see langword="null"/>.</param>
    /// <param name="roundingPolicy">
    /// How to round the converted amount to the minor unit of <paramref name="targetCurrency"/>. Defaults to
    /// <see cref="RoundingPolicy.BankersRounding"/>. Must be a defined value.
    /// </param>
    /// <param name="cancellationToken">A token passed to the rate lookup.</param>
    /// <returns>
    /// A successful result holding <c>money.Amount * rate</c> in <paramref name="targetCurrency"/>, rounded with
    /// <paramref name="roundingPolicy"/>; or the provider's failed result, unchanged, when no rate is available.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="money"/>, <paramref name="targetCurrency"/> or <paramref name="rateProvider"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="SharedKernel.Core.Exceptions.DomainException">
    /// <paramref name="roundingPolicy"/> is not a defined value. Thrown after the rate lookup, not returned as a
    /// failed result.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The converted amount is outside the range of <see cref="decimal"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Rate.</b> The rate is used as returned, with no check for zero or a negative value. Exceptions thrown by
    /// <paramref name="rateProvider"/>, including cancellation, propagate to the caller.
    /// </para>
    /// <para>
    /// <b>Same currency.</b> Converting into the amount's own currency still calls the provider, which should
    /// return a rate of 1.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var converted = await price.ConvertAsync(Currency.Eur, rateProvider, cancellationToken: ct);
    /// if (converted.IsFailure)
    ///     return Result&lt;Invoice&gt;.Failure(converted.Error);
    /// </code>
    /// </example>
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

    /// <summary>Returns the exact sum of a non-empty sequence of amounts that share one currency.</summary>
    /// <param name="amounts">The amounts to add. Must not be empty or contain <see langword="null"/>.</param>
    /// <returns>The total, in the currency of the first amount.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="amounts"/> is <see langword="null"/> or contains <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="amounts"/> is empty. An empty sequence has no currency; use
    /// <see cref="Sum(IEnumerable{Money}, Currency)"/> to get zero instead.
    /// </exception>
    /// <exception cref="BusinessRuleViolationException">
    /// An amount is not in the currency of the first amount; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The total is outside the range of <see cref="decimal"/>.</exception>
    /// <remarks>The sequence is enumerated once.</remarks>
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

    /// <summary>
    /// Returns the exact sum of <paramref name="amounts"/>, all of which must be in <paramref name="currency"/>.
    /// </summary>
    /// <param name="amounts">The amounts to add. May be empty; must not contain <see langword="null"/>.</param>
    /// <param name="currency">The currency of the total. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// The exact total, or zero in <paramref name="currency"/> when <paramref name="amounts"/> is empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="amounts"/> or <paramref name="currency"/> is <see langword="null"/>, or
    /// <paramref name="amounts"/> contains <see langword="null"/>.
    /// </exception>
    /// <exception cref="BusinessRuleViolationException">
    /// An amount is not in <paramref name="currency"/>; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The total is outside the range of <see cref="decimal"/>.</exception>
    /// <remarks>Equivalent to <see cref="Money.Sum"/>.</remarks>
    public static Money Sum(this IEnumerable<Money> amounts, Currency currency) => Money.Sum(amounts, currency);
}
