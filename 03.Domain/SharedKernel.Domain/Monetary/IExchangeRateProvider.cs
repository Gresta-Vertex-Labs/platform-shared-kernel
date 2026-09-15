using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>
/// A port that supplies the exchange rate for converting an amount from one currency to another.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> The domain defines this port and ships no implementation: a service supplies one that calls its
/// rate source, and passes it to <see cref="MoneyExtensions.ConvertAsync"/>.
/// </para>
/// <para>
/// <b>Implementing.</b> Return the rate as a failed result, not an exception, when no rate is available.
/// <see cref="MoneyExtensions.ConvertAsync"/> does not validate the rate, so never return zero or a negative
/// value for a real conversion, and return 1 when <c>source</c> equals <c>target</c>: conversion calls the
/// provider even then.
/// </para>
/// </remarks>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Returns the multiplier that converts an amount in <paramref name="source"/> into <paramref name="target"/>:
    /// <c>targetAmount = sourceAmount × rate</c>.
    /// </summary>
    /// <param name="source">The currency converted from; never <see langword="null"/> during conversion.</param>
    /// <param name="target">The currency converted to; never <see langword="null"/> during conversion.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>
    /// A successful result holding the rate; or a failed result, which conversion returns to its caller
    /// unchanged, when no rate is available.
    /// </returns>
    Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken);
}
