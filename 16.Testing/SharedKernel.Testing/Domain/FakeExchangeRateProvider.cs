using System.Collections.Concurrent;
using SharedKernel.Domain.ValueObjects.Money;
// NOTE: Result<T>/Error are deliberately NEVER `using`-imported bare in this file — this project's
// HotChocolate.Data reference pulls in a global `using GreenDonut;`, and GreenDonut.Result<TValue>
// collides with SharedKernel.Primitives.Results.Result<T> (CS0104). Every use below is fully
// qualified instead, mirroring the established Cryptography/FakeEnvelopeEncryptionProvider.cs
// precedent for this same project-wide ambiguity.

namespace SharedKernel.Testing.Domain;

/// <summary>
/// In-memory test double for <see cref="IExchangeRateProvider"/>.
/// </summary>
/// <remarks>
/// A dictionary-backed rate table, seeded explicitly per <c>(source, target)</c> pair via
/// <see cref="SeedRate"/> — this fake performs no real I/O and holds no built-in rate data.
/// Mirrors the established <c>Fake*</c> convention: never throws for an unconfigured lookup (the
/// port's own contract is <c>Result&lt;T&gt;</c>-returning, so a failure is expressed as
/// <c>Result&lt;T&gt;.Failure</c>, never a thrown exception), and exposes a
/// <see cref="SimulateFailure"/> toggle so a test can force the failure path without needing to
/// construct an artificially-unseeded pair.
/// </remarks>
public sealed class FakeExchangeRateProvider : IExchangeRateProvider
{
    private readonly ConcurrentDictionary<(string Source, string Target), decimal> _rates = new();

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="GetExchangeRateAsync"/> should
    /// unconditionally fail, regardless of whether a rate has been seeded for the requested pair.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Seeds the exchange rate to use when converting an amount from <paramref name="source"/>
    /// into <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The currency being converted from.</param>
    /// <param name="target">The currency being converted to.</param>
    /// <param name="rate">The multiplicative rate — <c>targetAmount = sourceAmount * rate</c>.</param>
    public void SeedRate(Currency source, Currency target, decimal rate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        _rates[(source.Code, target.Code)] = rate;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <c>Result&lt;T&gt;.Success</c> wrapping the rate seeded via <see cref="SeedRate"/>
    /// for the exact <c>(source, target)</c> pair, or <c>Result&lt;T&gt;.Failure</c> when no rate
    /// was seeded for that pair (or <see cref="SimulateFailure"/> is set) — never throws.
    /// </remarks>
    public Task<SharedKernel.Primitives.Results.Result<decimal>> GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<decimal>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected("exchange_rate.simulated_failure", "FakeExchangeRateProvider was configured to simulate a failure.")));
        }

        return Task.FromResult(_rates.TryGetValue((source.Code, target.Code), out var rate)
            ? SharedKernel.Primitives.Results.Result<decimal>.Success(rate)
            : SharedKernel.Primitives.Results.Result<decimal>.Failure(SharedKernel.Primitives.Errors.Error.NotFound(
                "exchange_rate.not_found",
                $"No exchange rate seeded for '{source.Code}' -> '{target.Code}'.")));
    }
}
