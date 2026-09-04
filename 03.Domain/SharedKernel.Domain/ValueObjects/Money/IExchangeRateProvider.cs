using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// A zero-I/O domain port for resolving the exchange rate between two currencies.
/// </summary>
/// <remarks>
/// <para>
/// WO-066/P-439. <c>SharedKernel.Domain</c> ships this interface (and the
/// <see cref="MoneyExtensions.ConvertAsync"/> composition helper) only — no implementation, no
/// hardcoded rate table, and no embedded <c>HttpClient</c>/SDK call. A consuming service supplies
/// the real adapter (typically bridged via <c>11.Communication</c>) at its own composition root.
/// </para>
/// <para>
/// An async, <see cref="Task"/>-returning member on a pure-contract interface mirrors the
/// already-shipped <c>IDomainEventDispatcher.DispatchAsync</c> precedent — the interface itself
/// performs no I/O; only a consuming service's concrete implementation does.
/// </para>
/// </remarks>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Resolves the multiplicative exchange rate to convert an amount denominated in
    /// <paramref name="source"/> into <paramref name="target"/> (i.e. <c>targetAmount = sourceAmount * rate</c>).
    /// </summary>
    /// <param name="source">The currency being converted from.</param>
    /// <param name="target">The currency being converted to.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the lookup.</param>
    Task<Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken);
}
