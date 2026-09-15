using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>Looks up the rate for converting an amount from one currency to another.</summary>
/// <remarks>
/// The domain defines this port and ships no implementation: a service supplies one that calls its rate source.
/// Use it through <see cref="MoneyExtensions.ConvertAsync"/>.
/// </remarks>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Returns the multiplier that converts an amount in <paramref name="source"/> into <paramref name="target"/>:
    /// <c>targetAmount = sourceAmount × rate</c>.
    /// </summary>
    /// <param name="source">The currency converted from.</param>
    /// <param name="target">The currency converted to.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The rate, or a failed result when no rate is available.</returns>
    Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken);
}
