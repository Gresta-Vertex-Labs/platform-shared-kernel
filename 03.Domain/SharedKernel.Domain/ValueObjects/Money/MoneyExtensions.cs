using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// Currency-conversion extension methods for <see cref="Money"/>.
/// </summary>
/// <remarks>
/// WO-066/P-439. An extension method, not an instance member on <see cref="Money"/> — keeps the
/// synchronous, pure <see cref="ValueObject"/> free of async members. Composes an
/// <see cref="IExchangeRateProvider"/> rate lookup with <see cref="Money.Create"/> as the
/// ergonomic conversion entry point consuming services use.
/// </remarks>
public static class MoneyExtensions
{
    /// <summary>
    /// Converts <paramref name="money"/> into <paramref name="targetCurrency"/> using the
    /// exchange rate resolved from <paramref name="rateProvider"/>.
    /// </summary>
    /// <param name="money">The amount to convert.</param>
    /// <param name="targetCurrency">The currency to convert into.</param>
    /// <param name="rateProvider">The rate lookup port. <c>SharedKernel.Domain</c> ships no implementation.</param>
    /// <param name="roundingPolicy">The rounding policy applied to the converted amount. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the rate lookup.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> wrapping the converted <see cref="Money"/> when the rate
    /// lookup and conversion both succeed. <see cref="Result{T}.Failure"/> carrying the
    /// provider's error when the rate lookup fails — no <see cref="Money"/> is constructed in
    /// that case.
    /// </returns>
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

        var rateResult = await rateProvider
            .GetExchangeRateAsync(money.Currency, targetCurrency, cancellationToken)
            .ConfigureAwait(false);

        if (rateResult.IsFailure)
            return Result<Money>.Failure(rateResult.Error);

        var convertedAmount = money.Amount * rateResult.Value;
        return Money.Create(convertedAmount, targetCurrency, roundingPolicy);
    }
}
