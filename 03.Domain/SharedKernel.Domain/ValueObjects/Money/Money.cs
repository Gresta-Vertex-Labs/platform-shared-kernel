using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// A currency-aware monetary amount — an ISO 4217 minor-unit-correct <see cref="decimal"/>
/// amount paired with a validated <see cref="ValueObjects.Money.Currency"/> identity.
/// </summary>
/// <remarks>
/// <para>
/// WO-066/P-439. Extends <see cref="ValueObject"/> directly (not <see cref="SingleValueObject{TValue}"/>)
/// because a <see cref="Money"/> has two independent components rather than one.
/// </para>
/// <para>
/// <strong>Unconditional rounding:</strong> <see cref="Amount"/> is always rounded to
/// <see cref="ValueObjects.Money.Currency.MinorUnitDigits"/> at construction, per the supplied
/// <see cref="RoundingPolicy"/>. There is no separate "reject excess precision" validation path —
/// <c>Money.Create(10.005m, Currency.Usd)</c> does not throw; it silently rounds. Callers that need
/// to detect precision loss must compare their input against the constructed <see cref="Amount"/>
/// themselves before calling <see cref="Create"/>.
/// </para>
/// <para>
/// <strong>Construction and the currency null-guard:</strong> the constructor is private and
/// guards <c>currency</c> for <see langword="null"/> via <see cref="Guard.Throw"/> — mirroring the
/// <c>AggregateRoot&lt;TId&gt;</c> clock-parameter precedent (WO-051/P-311) — directly in the
/// constructor body, before assignment. Unlike <see cref="SingleValueObject{TValue}"/>, this
/// class does not rely on the "field initializer runs before <c>base()</c>" primary-constructor
/// technique for this guard: that technique requires a primary constructor, and a primary
/// constructor on a <see langword="sealed"/>, non-abstract class carries the class's own
/// (public) accessibility with no C# language mechanism to further restrict it — incompatible
/// with keeping this constructor genuinely <see langword="private"/>. <see cref="Validate"/> is
/// therefore a no-op (there is no invariant left to check once the constructor's own guard has
/// run) — functionally identical to the design's intent, since <see cref="Guard.Throw"/> already
/// throws a <see cref="SharedKernel.Core.Exceptions.DomainException"/> immediately, exactly as the
/// referenced <c>AggregateRoot&lt;TId&gt;</c> precedent does.
/// </para>
/// </remarks>
public sealed class Money : ValueObject, IComparable<Money>
{
    private Money(decimal amount, Currency currency, RoundingPolicy roundingPolicy)
    {
        Guard.Throw.Null(currency, nameof(currency));
        Currency = currency;
        Amount = Round(amount, currency.MinorUnitDigits, roundingPolicy);
    }

    /// <summary>
    /// Gets the monetary amount. Always already rounded to <see cref="Currency"/>'s
    /// <see cref="ValueObjects.Money.Currency.MinorUnitDigits"/>.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>Gets the currency this amount is denominated in.</summary>
    public Currency Currency { get; }

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op — the sole invariant (a non-null <see cref="Currency"/>) is already enforced by the
    /// constructor's own guard, which runs before this method could otherwise observe the value.
    /// See the type-level remarks for why.
    /// </remarks>
    protected override IEnumerable<Error>? Validate() => null;

    /// <summary>
    /// Creates a <see cref="Money"/> for <paramref name="amount"/> in <paramref name="currency"/>,
    /// rounding unconditionally to <paramref name="currency"/>'s minor-unit precision.
    /// </summary>
    /// <param name="amount">The monetary amount, in major units (e.g. dollars, not cents).</param>
    /// <param name="currency">The currency this amount is denominated in.</param>
    /// <param name="roundingPolicy">The rounding policy to apply. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    public static Result<Money> Create(
        decimal amount, Currency currency, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        TryCreate(() => new Money(amount, currency, roundingPolicy));

    /// <summary>Creates a zero-amount <see cref="Money"/> in <paramref name="currency"/>.</summary>
    /// <remarks>Cannot fail — no <see cref="Result{T}"/> wrapper is needed.</remarks>
    public static Money Zero(Currency currency) => new(0m, currency, RoundingPolicy.BankersRounding);

    // ── Arithmetic / comparison ─────────────────────────────────────────────

    /// <summary>Adds <paramref name="other"/> to this amount. Both operands must share the same <see cref="Currency"/>.</summary>
    /// <exception cref="SharedKernel.Domain.Exceptions.BusinessRuleViolationException">
    /// Thrown when <paramref name="other"/>'s currency does not match this instance's currency.
    /// </exception>
    public Money Add(Money other)
    {
        CheckRule(new CurrencyMismatchRule(Currency, other.Currency));
        return new Money(Amount + other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>Subtracts <paramref name="other"/> from this amount. Both operands must share the same <see cref="Currency"/>.</summary>
    /// <exception cref="SharedKernel.Domain.Exceptions.BusinessRuleViolationException">
    /// Thrown when <paramref name="other"/>'s currency does not match this instance's currency.
    /// </exception>
    public Money Subtract(Money other)
    {
        CheckRule(new CurrencyMismatchRule(Currency, other.Currency));
        return new Money(Amount - other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>Returns the additive inverse of this amount, in the same currency.</summary>
    public Money Negate() => new(-Amount, Currency, RoundingPolicy.BankersRounding);

    /// <summary>
    /// Multiplies this amount by a scalar <paramref name="factor"/>, re-rounding the product to
    /// <see cref="Currency"/>'s minor-unit precision.
    /// </summary>
    /// <param name="factor">The scalar multiplier. Has no currency of its own, so cannot mismatch.</param>
    /// <param name="roundingPolicy">The rounding policy applied to the product. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    public Money Multiply(decimal factor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        new(Amount * factor, Currency, roundingPolicy);

    /// <summary>
    /// Compares this amount to <paramref name="other"/>. Both must share the same <see cref="Currency"/>
    /// — cross-currency comparison is exactly as invalid as cross-currency addition.
    /// </summary>
    /// <exception cref="SharedKernel.Domain.Exceptions.BusinessRuleViolationException">
    /// Thrown when <paramref name="other"/> is non-null and its currency does not match this
    /// instance's currency.
    /// </exception>
    public int CompareTo(Money? other)
    {
        if (other is null) return 1;
        CheckRule(new CurrencyMismatchRule(Currency, other.Currency));
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>Adds two same-currency amounts.</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtracts one same-currency amount from another.</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Returns the additive inverse of <paramref name="money"/>.</summary>
    public static Money operator -(Money money) => money.Negate();

    /// <summary>Multiplies <paramref name="money"/> by a scalar <paramref name="factor"/>.</summary>
    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    // ── Allocation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Splits this amount into <paramref name="numberOfParts"/> equal (or as-equal-as-possible)
    /// shares, conserving the total exactly.
    /// </summary>
    /// <param name="numberOfParts">The number of shares to split into. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="numberOfParts"/> is less than 1.</exception>
    /// <remarks>Equivalent to <see cref="Allocate(IReadOnlyList{int})"/> with <paramref name="numberOfParts"/> equal ratios of <c>1</c>.</remarks>
    public IReadOnlyList<Money> Allocate(int numberOfParts)
    {
        if (numberOfParts < 1)
            throw new ArgumentOutOfRangeException(nameof(numberOfParts), numberOfParts, "Number of parts must be at least 1.");

        var equalRatios = new int[numberOfParts];
        Array.Fill(equalRatios, 1);
        return AllocateByRatios(equalRatios);
    }

    /// <summary>
    /// Splits this amount proportionally to <paramref name="ratios"/>, conserving the total exactly.
    /// </summary>
    /// <param name="ratios">
    /// The relative weight of each resulting share. Must be non-empty, contain no negative
    /// values, and not be entirely zero.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ratios"/> is <see langword="null"/>, empty, contains a
    /// negative value, or is entirely zero.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Algorithm: largest-remainder / Hare–Niemeyer method. Converts <see cref="Amount"/> to an
    /// exact integer count of minor units, integer-divides proportionally to each ratio's share
    /// (floor division), then distributes the leftover minor units one-by-one,
    /// largest-remainder-first (ties broken by descending original index, for determinism).
    /// </para>
    /// <para>
    /// Guarantees <c>Sum(result) == this</c> exactly, and no part differs from another by more
    /// than one minor unit. For example, splitting $10.00 three ways produces
    /// <c>[3.33, 3.33, 3.34]</c> — never <c>[3.33, 3.33, 3.33]</c> (loses a cent) or
    /// <c>[3.34, 3.34, 3.34]</c> (invents two cents).
    /// </para>
    /// </remarks>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<int> ratios)
    {
        if (ratios is null || ratios.Count == 0)
            throw new ArgumentException("Ratios must not be null or empty.", nameof(ratios));

        foreach (var ratio in ratios)
        {
            if (ratio < 0)
                throw new ArgumentException("Ratios must not contain negative values.", nameof(ratios));
        }

        var hasNonZeroRatio = false;
        foreach (var ratio in ratios)
        {
            if (ratio != 0)
            {
                hasNonZeroRatio = true;
                break;
            }
        }

        if (!hasNonZeroRatio)
            throw new ArgumentException("Ratios must not be entirely zero.", nameof(ratios));

        return AllocateByRatios(ratios);
    }

    private IReadOnlyList<Money> AllocateByRatios(IReadOnlyList<int> ratios)
    {
        var scale = MinorUnitScale(Currency.MinorUnitDigits);
        var totalMinorUnits = (long)decimal.Round(Amount * scale, 0, MidpointRounding.ToEven);

        var totalRatio = 0L;
        foreach (var ratio in ratios)
            totalRatio += ratio;

        var shares = new long[ratios.Count];
        var remainders = new decimal[ratios.Count];
        var allocated = 0L;

        for (var i = 0; i < ratios.Count; i++)
        {
            var exactShare = totalMinorUnits * ratios[i] / (decimal)totalRatio;
            var flooredShare = (long)Math.Floor(exactShare);
            shares[i] = flooredShare;
            remainders[i] = exactShare - flooredShare;
            allocated += flooredShare;
        }

        var leftover = totalMinorUnits - allocated;
        if (leftover > 0)
        {
            var order = new int[ratios.Count];
            for (var i = 0; i < ratios.Count; i++)
                order[i] = i;

            // Largest-remainder-first; ties broken by descending original index for determinism
            // (matches the documented worked example: splitting $10.00 three ways yields
            // [3.33, 3.33, 3.34] — the last equally-tied share receives the leftover cent first).
            Array.Sort(order, (a, b) =>
            {
                var byRemainder = remainders[b].CompareTo(remainders[a]);
                return byRemainder != 0 ? byRemainder : b.CompareTo(a);
            });

            for (var i = 0; i < leftover; i++)
                shares[order[i]] += 1;
        }

        var results = new Money[ratios.Count];
        for (var i = 0; i < ratios.Count; i++)
            results[i] = new Money(shares[i] / scale, Currency, RoundingPolicy.BankersRounding);

        return results;
    }

    private static decimal MinorUnitScale(int minorUnitDigits) => minorUnitDigits switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1000m,
        4 => 10000m,
        _ => (decimal)Math.Pow(10, minorUnitDigits),
    };

    // ── Rounding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rounds <paramref name="amount"/> to <paramref name="minorUnitDigits"/> decimal places per
    /// <paramref name="policy"/>.
    /// </summary>
    /// <remarks>
    /// WO-066/P-439/P-439a (C-47). Maps <see cref="RoundingPolicy.BankersRounding"/> to
    /// <see cref="MidpointRounding.ToEven"/> and <see cref="RoundingPolicy.AwayFromZero"/> to
    /// <see cref="MidpointRounding.AwayFromZero"/>.
    /// </remarks>
    private static decimal Round(decimal amount, int minorUnitDigits, RoundingPolicy policy)
    {
        var midpointRounding = policy == RoundingPolicy.AwayFromZero
            ? MidpointRounding.AwayFromZero
            : MidpointRounding.ToEven;

        return Math.Round(amount, minorUnitDigits, midpointRounding);
    }
}
