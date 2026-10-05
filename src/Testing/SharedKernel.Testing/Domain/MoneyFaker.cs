using Bogus;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// A Bogus-integrated builder for generating deterministic <see cref="Money"/> test values.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Money"/> has no public parameterless constructor — it is created only via
/// <see cref="Money.Create"/>/<see cref="Money.Zero"/> — so this faker is a thin generator over
/// that factory surface rather than a <c>Faker&lt;Money&gt;</c> subclass (mirrors
/// <c>SingleValueObjectFaker{TValueObject,TValue}</c>'s "wraps the real factory" precedent, not
/// <c>EntityFaker{TEntity,TId}</c>'s <c>RuleFor</c>-declaring shape, since <see cref="Money"/> has
/// no settable properties to declare rules against).
/// </para>
/// <para>
/// The default currency pool deliberately spans three minor-unit shapes so a test that omits an
/// explicit <see cref="Currency"/> still exercises rounding-precision edge cases: <see cref="Currency.Usd"/>
/// (2-decimal), <see cref="Jpy"/> (0-decimal, zero-decimal), and <see cref="Bhd"/> (3-decimal).
/// </para>
/// <para>
/// Uses a seeded, package-local <see cref="Faker"/> instance for determinism — independent of
/// whether the consuming test assembly has called <c>FakerSeeding.Apply()</c>, mirroring
/// <c>SingleValueObjectFaker{TValueObject,TValue}</c>'s own self-contained determinism.
/// </para>
/// </remarks>
public sealed class MoneyFaker
{
    /// <summary>Japanese Yen — the default zero-decimal pool member (not exposed by <see cref="Currency"/> itself).</summary>
    private static readonly Currency Jpy = Currency.Create("JPY").Value;

    /// <summary>Bahraini Dinar — the default three-decimal pool member.</summary>
    private static readonly Currency Bhd = Currency.Create("BHD").Value;

    private static readonly Currency[] DefaultCurrencyPool = [Currency.Usd, Currency.Eur, Jpy, Bhd];

    // Seeded explicitly on this instance (never relying on ambient FakerSeeding.Apply() having
    // been called) so this faker is deterministic regardless of what a consuming test assembly
    // has or hasn't configured — mirrors ValidationSampleGenerator's identical self-contained
    // determinism approach.
    private readonly Faker _faker = new() { Random = new Randomizer(8675309) };

    /// <summary>
    /// Generates a single deterministic <see cref="Money"/> value.
    /// </summary>
    /// <param name="currency">
    /// The currency to use. When omitted, one is drawn from a default pool spanning a 2-decimal,
    /// a 0-decimal, and a 3-decimal currency.
    /// </param>
    /// <param name="amount">
    /// The amount to use, in major units. When omitted, a random amount between <c>0.01</c> and
    /// <c>10_000</c> is drawn.
    /// </param>
    /// <returns>The generated <see cref="Money"/> value.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="amount"/>/<paramref name="currency"/> combination failed <see cref="Money.Create"/>'s
    /// validation — should not occur for any value this faker itself produces.
    /// </exception>
    public Money Generate(Currency? currency = null, decimal? amount = null)
    {
        var resolvedCurrency = currency ?? _faker.PickRandom(DefaultCurrencyPool);
        var resolvedAmount = amount ?? _faker.Random.Decimal(0.01m, 10_000m);

        var result = Money.Create(resolvedAmount, resolvedCurrency);
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException(
                $"MoneyFaker produced an invalid Money value: {string.Join(" ", result.Errors.Select(e => e.Message))}");
    }

    /// <summary>Generates <paramref name="count"/> deterministic <see cref="Money"/> values.</summary>
    /// <param name="count">The number of values to generate. Must be at least 0.</param>
    /// <param name="currency">
    /// The currency to use for every generated value. When omitted, each value independently
    /// draws from the default currency pool.
    /// </param>
    public IReadOnlyList<Money> GenerateMany(int count, Currency? currency = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var results = new List<Money>(count);
        for (var i = 0; i < count; i++)
        {
            results.Add(Generate(currency));
        }

        return results;
    }
}
