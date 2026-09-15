using System.Globalization;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>An amount of money in one currency, always rounded to that currency's minor unit.</summary>
/// <remarks>
/// <para>
/// <b>Always rounded.</b> <see cref="Amount"/> is rounded to <see cref="Monetary.Currency.MinorUnitDigits"/> when
/// the value is created, using the chosen <see cref="RoundingPolicy"/>. <c>Money.Create(10.005m, Currency.Usd)</c>
/// holds 10.00 under banker's rounding; compare the input with <see cref="Amount"/> to detect precision loss.
/// </para>
/// <para>
/// <b>One currency per operation.</b> Adding, subtracting, comparing or taking the minimum or maximum of amounts
/// in different currencies throws <see cref="BusinessRuleViolationException"/> with the code
/// <see cref="CurrencyMismatchRule.ErrorCode"/>. Convert first with <see cref="MoneyExtensions.ConvertAsync"/>.
/// </para>
/// <para>
/// <b>Splitting never loses a minor unit.</b> Use <see cref="Allocate(int)"/> or
/// <see cref="Allocate(IReadOnlyList{int})"/> instead of dividing: $10.00 split three ways is 3.33, 3.33 and 3.34.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var price = Money.Create(19.99m, Currency.Usd).Value;
/// var total = price * 3;                       // 59.97 USD
/// var shares = total.Allocate(2);              // 29.99 USD, 29.98 USD
/// Console.WriteLine(total);                    // "59.97 USD"
/// </code>
/// </example>
public sealed class Money : ValueObject, IComparable<Money>, IFormattable
{
    private Money(decimal amount, Currency currency, RoundingPolicy roundingPolicy)
    {
        Guard.Throw.Null(currency);
        Guard.Throw.InvalidEnumValue(roundingPolicy);
        Currency = currency;
        Amount = Round(amount, currency.MinorUnitDigits, roundingPolicy);
    }

    /// <summary>Gets the amount in major units (dollars, not cents), rounded to the currency's minor unit.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the currency of the amount.</summary>
    public Currency Currency { get; }

    /// <summary>Gets a value indicating whether the amount is zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Gets a value indicating whether the amount is greater than zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Gets a value indicating whether the amount is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates an amount in <paramref name="currency"/>, rounded to its minor unit.</summary>
    /// <param name="amount">The amount in major units.</param>
    /// <param name="currency">The currency.</param>
    /// <param name="roundingPolicy">How to round to the minor unit. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    /// <returns>The money, or a failed result when <paramref name="currency"/> is null or <paramref name="roundingPolicy"/> is undefined.</returns>
    public static ValidationResult<Money> Create(
        decimal amount,
        Currency currency,
        RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        TryCreate(() => new Money(amount, currency, roundingPolicy));

    /// <summary>Returns zero in <paramref name="currency"/>.</summary>
    /// <param name="currency">The currency.</param>
    /// <returns>A zero amount.</returns>
    /// <exception cref="DomainException"><paramref name="currency"/> is <see langword="null"/>.</exception>
    public static Money Zero(Currency currency) => new(0m, currency, RoundingPolicy.BankersRounding);

    /// <summary>Returns the sum of <paramref name="amounts"/>, all of which must be in <paramref name="currency"/>.</summary>
    /// <param name="amounts">The amounts to add.</param>
    /// <param name="currency">The currency of the total, returned as zero when <paramref name="amounts"/> is empty.</param>
    /// <returns>The total.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="amounts"/> or <paramref name="currency"/> is null, or <paramref name="amounts"/> contains null.</exception>
    /// <exception cref="BusinessRuleViolationException">An amount is not in <paramref name="currency"/>.</exception>
    public static Money Sum(IEnumerable<Money> amounts, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        ArgumentNullException.ThrowIfNull(currency);

        var total = Zero(currency);
        foreach (var amount in amounts)
            total = total.Add(amount);
        return total;
    }

    /// <summary>Returns the smaller of two amounts in the same currency.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><paramref name="left"/> when the amounts are equal; otherwise the smaller one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static Money Min(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) <= 0 ? left : right;
    }

    /// <summary>Returns the larger of two amounts in the same currency.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><paramref name="left"/> when the amounts are equal; otherwise the larger one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static Money Max(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) >= 0 ? left : right;
    }

    /// <summary>Adds <paramref name="other"/>, which must be in the same currency.</summary>
    /// <param name="other">The amount to add.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>Subtracts <paramref name="other"/>, which must be in the same currency.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>Returns the amount with its sign reversed.</summary>
    /// <returns>The negated amount.</returns>
    public Money Negate() => new(-Amount, Currency, RoundingPolicy.BankersRounding);

    /// <summary>Returns the absolute amount.</summary>
    /// <returns>The amount without its sign.</returns>
    public Money Abs() => IsNegative ? Negate() : this;

    /// <summary>Multiplies the amount by <paramref name="factor"/> and rounds the product to the minor unit.</summary>
    /// <param name="factor">The multiplier, such as a quantity or a tax rate.</param>
    /// <param name="roundingPolicy">How to round the product. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    /// <returns>The product.</returns>
    /// <exception cref="DomainException"><paramref name="roundingPolicy"/> is undefined.</exception>
    /// <exception cref="OverflowException">The product is outside the range of <see cref="decimal"/>.</exception>
    public Money Multiply(decimal factor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        new(Amount * factor, Currency, roundingPolicy);

    /// <summary>Divides the amount by <paramref name="divisor"/> and rounds the quotient to the minor unit.</summary>
    /// <param name="divisor">The divisor, such as an exchange factor. To split an amount into parts, use <see cref="Allocate(int)"/>.</param>
    /// <param name="roundingPolicy">How to round the quotient. Defaults to <see cref="RoundingPolicy.BankersRounding"/>.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    /// <exception cref="DomainException"><paramref name="roundingPolicy"/> is undefined.</exception>
    /// <exception cref="OverflowException">The quotient is outside the range of <see cref="decimal"/>.</exception>
    public Money Divide(decimal divisor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        new(Amount / divisor, Currency, roundingPolicy);

    /// <summary>Compares with <paramref name="other"/>, which must be in the same currency.</summary>
    /// <param name="other">The amount to compare with.</param>
    /// <returns>Negative, zero or positive as this amount is less than, equal to or greater than <paramref name="other"/>; positive when <paramref name="other"/> is null.</returns>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public int CompareTo(Money? other)
    {
        if (other is null)
            return 1;

        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>Splits the amount into <paramref name="numberOfParts"/> parts that differ by at most one minor unit and add up to exactly the original amount.</summary>
    /// <param name="numberOfParts">The number of parts. Must be at least 1.</param>
    /// <returns>The parts, largest remainders first receiving the leftover minor units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numberOfParts"/> is less than 1.</exception>
    public IReadOnlyList<Money> Allocate(int numberOfParts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numberOfParts, 1);

        var ratios = new int[numberOfParts];
        Array.Fill(ratios, 1);
        return AllocateByRatios(ratios);
    }

    /// <summary>
    /// Splits the amount in proportion to <paramref name="ratios"/>, so the parts add up to exactly the original
    /// amount.
    /// </summary>
    /// <param name="ratios">The relative weight of each part. Must be non-empty, non-negative, and not all zero.</param>
    /// <returns>The parts, in the order of <paramref name="ratios"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ratios"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="ratios"/> is empty, contains a negative value, or is all zero.</exception>
    /// <exception cref="OverflowException">The amount in minor units exceeds the range of <see cref="long"/>.</exception>
    /// <remarks>
    /// Uses the largest-remainder method: each part receives the floor of its exact share in minor units, and the
    /// leftover minor units go one at a time to the parts with the largest remainders, ties to the later part.
    /// </remarks>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<int> ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        if (ratios.Count == 0)
            throw new ArgumentException("At least one ratio is required.", nameof(ratios));
        if (ratios.Any(r => r < 0))
            throw new ArgumentException("Ratios must not be negative.", nameof(ratios));
        if (ratios.All(r => r == 0))
            throw new ArgumentException("At least one ratio must be greater than zero.", nameof(ratios));

        return AllocateByRatios(ratios);
    }

    /// <summary>Formats the amount with the currency's minor-unit precision and the code, using the invariant culture: <c>1234.50 USD</c>.</summary>
    /// <returns>The formatted amount.</returns>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <summary>Formats the amount and appends the currency code: <c>1,234.50 USD</c> for format <c>N</c>.</summary>
    /// <param name="format">
    /// A standard or custom numeric format string for the amount. <see langword="null"/> or empty formats with exactly
    /// the currency's minor-unit digits. The currency code is always appended, so do not use a currency format such
    /// as <c>C</c>, whose symbol comes from the culture rather than from <see cref="Currency"/>.
    /// </param>
    /// <param name="formatProvider">The culture for the amount; <see langword="null"/> uses the current culture.</param>
    /// <returns>The formatted amount followed by a space and the currency code.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var amountFormat = string.IsNullOrEmpty(format) ? $"F{Currency.MinorUnitDigits}" : format;
        return $"{Amount.ToString(amountFormat, formatProvider)} {Currency.Code}";
    }

    /// <summary>Adds two amounts in the same currency.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Add(right);
    }

    /// <summary>Subtracts one amount from another in the same currency.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Subtract(right);
    }

    /// <summary>Reverses the sign of an amount.</summary>
    /// <param name="money">The amount.</param>
    /// <returns>The negated amount.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    public static Money operator -(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Negate();
    }

    /// <summary>Multiplies an amount by a factor, with banker's rounding.</summary>
    /// <param name="money">The amount.</param>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The product.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    public static Money operator *(Money money, decimal factor)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Multiply(factor);
    }

    /// <summary>Divides an amount by a divisor, with banker's rounding.</summary>
    /// <param name="money">The amount.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    public static Money operator /(Money money, decimal divisor)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Divide(divisor);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">The currencies differ.</exception>
    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    /// <summary>Creates an amount from values already known to be valid; used by conversions within this package.</summary>
    internal static Money FromTrusted(decimal amount, Currency currency, RoundingPolicy roundingPolicy) =>
        new(amount, currency, roundingPolicy);

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <inheritdoc/>
    /// <remarks>Money has no rules beyond those its constructor guards, so there is nothing to report.</remarks>
    protected override IEnumerable<Error> Validate() => [];

    private static int Compare(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right);
    }

    private static decimal Round(decimal amount, int digits, RoundingPolicy policy) => policy switch
    {
        RoundingPolicy.AwayFromZero => Math.Round(amount, digits, MidpointRounding.AwayFromZero),
        RoundingPolicy.ToZero => Math.Round(amount, digits, MidpointRounding.ToZero),
        RoundingPolicy.Ceiling => Math.Round(amount, digits, MidpointRounding.ToPositiveInfinity),
        RoundingPolicy.Floor => Math.Round(amount, digits, MidpointRounding.ToNegativeInfinity),
        _ => Math.Round(amount, digits, MidpointRounding.ToEven),
    };

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        CheckRule(new CurrencyMismatchRule(Currency, other.Currency));
    }

    private IReadOnlyList<Money> AllocateByRatios(IReadOnlyList<int> ratios)
    {
        var scale = MinorUnitScale(Currency.MinorUnitDigits);
        var totalMinorUnits = checked((long)(Amount * scale));

        long totalRatio = 0;
        foreach (var ratio in ratios)
            totalRatio += ratio;

        var shares = new long[ratios.Count];
        var remainders = new decimal[ratios.Count];
        long allocated = 0;

        for (var i = 0; i < ratios.Count; i++)
        {
            var exactShare = totalMinorUnits * (decimal)ratios[i] / totalRatio;
            var floored = (long)decimal.Floor(exactShare);
            shares[i] = floored;
            remainders[i] = exactShare - floored;
            allocated += floored;
        }

        var leftover = totalMinorUnits - allocated;
        if (leftover > 0)
        {
            var order = Enumerable.Range(0, ratios.Count)
                .OrderByDescending(i => remainders[i])
                .ThenByDescending(i => i)
                .ToArray();

            for (var i = 0; i < leftover; i++)
                shares[order[i]]++;
        }

        var parts = new Money[ratios.Count];
        for (var i = 0; i < ratios.Count; i++)
            parts[i] = new Money(shares[i] / scale, Currency, RoundingPolicy.BankersRounding);
        return parts;
    }

    private static decimal MinorUnitScale(int digits)
    {
        var scale = 1m;
        for (var i = 0; i < digits; i++)
            scale *= 10m;
        return scale;
    }
}
