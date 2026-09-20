using System.Globalization;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>A currency-aware monetary amount, always rounded to its currency's minor unit.</summary>
/// <remarks>
/// <para>
/// <b>Rounding.</b> <see cref="Amount"/> never has more decimal places than
/// <see cref="Monetary.Currency.MinorUnitDigits"/>. <see cref="Create"/>, <see cref="Multiply"/>,
/// <see cref="Divide"/> and <see cref="MoneyExtensions.ConvertAsync"/> round with the
/// <see cref="RoundingPolicy"/> they are given (default <see cref="RoundingPolicy.BankersRounding"/>); the
/// <c>*</c> and <c>/</c> operators always use banker's rounding. Addition, subtraction, negation and allocation
/// are exact and never need to round. <c>Money.Create(10.005m, Currency.Usd)</c> holds 10.00; compare the input
/// with <see cref="Amount"/> to detect precision loss. The policy is not stored on the amount.
/// </para>
/// <para>
/// <b>One currency per operation.</b> <see cref="Add"/>, <see cref="Subtract"/>, <see cref="CompareTo"/>,
/// <see cref="Min"/>, <see cref="Max"/>, <see cref="Sum"/> and the arithmetic and comparison operators throw
/// <see cref="BusinessRuleViolationException"/> with the code <c>money.currency_mismatch</c>
/// (<see cref="CurrencyMismatchRule.ErrorCode"/>) when the currencies differ. Convert first with
/// <see cref="MoneyExtensions.ConvertAsync"/>.
/// </para>
/// <para>
/// <b>Equality.</b> Two amounts are equal when both <see cref="Amount"/> and <see cref="Currency"/> are equal.
/// <c>==</c> and <see cref="ValueObject.Equals(object)"/> return <see langword="false"/> across currencies
/// rather than throwing; only ordering throws.
/// </para>
/// <para>
/// <b>Splitting.</b> Use <see cref="Allocate(int)"/> or <see cref="Allocate(IReadOnlyList{int})"/> instead of
/// <see cref="Divide"/>: the parts always add up to exactly the original amount. $10.00 split three ways is
/// 3.33, 3.33 and 3.34.
/// </para>
/// <para>
/// <b>Formatting.</b> <see cref="ToString()"/> uses the invariant culture (<c>1234.50 USD</c>), but string
/// interpolation and composite formatting call <see cref="ToString(string, IFormatProvider)"/> with no provider,
/// which uses the current culture. Pass <see cref="CultureInfo.InvariantCulture"/> explicitly for machine-readable
/// output.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var price = Money.Create(19.99m, Currency.Usd).Value;
/// var total = price * 3; // 59.97 USD
/// var shares = total.Allocate(2); // 29.98 USD, 29.99 USD
/// Console.WriteLine(total); // "59.97 USD"
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

    // EF Core complex-type constructor binding (06.Persistence.EfCore): the two parameters match
    // Amount and Currency exactly, so EF Core's constructor-binding discovery selects this constructor over
    // the three-parameter one above, whose unmatched roundingPolicy parameter makes it ineligible. A value
    // read back from storage is expected to already be rounded to the currency's minor unit; this constructor
    // deliberately does NOT re-round it. Silently re-rounding a corrupted or bypassed value would hide data
    // loss, so a stored amount with more decimal places than the currency allows fails loudly instead.
    private Money(decimal amount, Currency currency)
    {
        Guard.Throw.Null(currency);
        Currency = currency;
        CheckRule(new StoredPrecisionRule(amount, currency));
        Amount = amount;
    }

    /// <summary>
    /// Gets the amount in major units (dollars, not cents), with at most
    /// <see cref="Monetary.Currency.MinorUnitDigits"/> decimal places.
    /// </summary>
    /// <remarks>
    /// The <see cref="decimal"/> scale is not normalized: 10 USD may hold <c>10</c> or <c>10.00</c>; both are equal.
    /// Use <see cref="ToString()"/> for a representation with exactly the minor-unit digits.
    /// </remarks>
    public decimal Amount { get; }

    /// <summary>Gets the currency the amount is denominated in; never <see langword="null"/>.</summary>
    public Currency Currency { get; }

    /// <summary>Gets a value indicating whether <see cref="Amount"/> is zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Gets a value indicating whether <see cref="Amount"/> is greater than zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Gets a value indicating whether <see cref="Amount"/> is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates an amount in <paramref name="currency"/>, rounded to its minor unit.</summary>
    /// <param name="amount">The amount in major units; may be negative or zero.</param>
    /// <param name="currency">The currency. Must not be <see langword="null"/>.</param>
    /// <param name="roundingPolicy">
    /// How to round <paramref name="amount"/> to the minor unit. Defaults to
    /// <see cref="RoundingPolicy.BankersRounding"/>. Must be a defined value.
    /// </param>
    /// <returns>
    /// A successful result holding the rounded amount; or a failed result when <paramref name="currency"/> is
    /// <see langword="null"/> or <paramref name="roundingPolicy"/> is not a defined value. Any
    /// <see cref="decimal"/> value is otherwise accepted.
    /// </returns>
    public static ValidationResult<Money> Create(
        decimal amount,
        Currency currency,
        RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        TryCreate(() => new Money(amount, currency, roundingPolicy));

    /// <summary>Returns a zero amount in <paramref name="currency"/>.</summary>
    /// <param name="currency">The currency. Must not be <see langword="null"/>.</param>
    /// <returns>A new zero amount.</returns>
    /// <exception cref="DomainException"><paramref name="currency"/> is <see langword="null"/>.</exception>
    public static Money Zero(Currency currency) => new(0m, currency, RoundingPolicy.BankersRounding);

    /// <summary>
    /// Returns the sum of <paramref name="amounts"/>, all of which must be in <paramref name="currency"/>.
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
    /// <param name="left">The first amount. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second amount. Must not be <see langword="null"/>.</param>
    /// <returns><paramref name="left"/> when the amounts are equal; otherwise the smaller instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static Money Min(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) <= 0 ? left : right;
    }

    /// <summary>Returns the larger of two amounts in the same currency.</summary>
    /// <param name="left">The first amount. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second amount. Must not be <see langword="null"/>.</param>
    /// <returns><paramref name="left"/> when the amounts are equal; otherwise the larger instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static Money Max(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) >= 0 ? left : right;
    }

    /// <summary>
    /// Returns the exact sum of this amount and <paramref name="other"/>, which must be in the same currency.
    /// </summary>
    /// <param name="other">The amount to add. Must not be <see langword="null"/>.</param>
    /// <returns>A new amount in this currency.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The sum is outside the range of <see cref="decimal"/>.</exception>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>
    /// Returns the exact difference of this amount minus <paramref name="other"/>, which must be in the same currency.
    /// </summary>
    /// <param name="other">The amount to subtract. Must not be <see langword="null"/>.</param>
    /// <returns>A new amount in this currency; negative when <paramref name="other"/> is larger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The difference is outside the range of <see cref="decimal"/>.</exception>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency, RoundingPolicy.BankersRounding);
    }

    /// <summary>Returns a new amount in the same currency with the sign reversed.</summary>
    /// <returns>The negated amount; zero stays zero.</returns>
    public Money Negate() => new(-Amount, Currency, RoundingPolicy.BankersRounding);

    /// <summary>Returns the amount without its sign.</summary>
    /// <returns>This instance when the amount is zero or positive; otherwise its negation.</returns>
    public Money Abs() => IsNegative ? Negate() : this;

    /// <summary>Returns the amount multiplied by <paramref name="factor"/>, rounded to the minor unit.</summary>
    /// <param name="factor">The multiplier, such as a quantity or a tax rate; may be negative or zero.</param>
    /// <param name="roundingPolicy">
    /// How to round the product. Defaults to <see cref="RoundingPolicy.BankersRounding"/>. Must be a defined value.
    /// </param>
    /// <returns>A new amount in this currency.</returns>
    /// <exception cref="DomainException"><paramref name="roundingPolicy"/> is not a defined value.</exception>
    /// <exception cref="OverflowException">The product is outside the range of <see cref="decimal"/>.</exception>
    public Money Multiply(decimal factor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        new(Amount * factor, Currency, roundingPolicy);

    /// <summary>Returns the amount divided by <paramref name="divisor"/>, rounded to the minor unit.</summary>
    /// <param name="divisor">
    /// The divisor, such as a tax factor. Must not be zero. To split an amount into parts without losing a minor
    /// unit, use <see cref="Allocate(int)"/> instead.
    /// </param>
    /// <param name="roundingPolicy">
    /// How to round the quotient. Defaults to <see cref="RoundingPolicy.BankersRounding"/>. Must be a defined value.
    /// </param>
    /// <returns>A new amount in this currency.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    /// <exception cref="DomainException"><paramref name="roundingPolicy"/> is not a defined value.</exception>
    /// <exception cref="OverflowException">The quotient is outside the range of <see cref="decimal"/>.</exception>
    public Money Divide(decimal divisor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding) =>
        new(Amount / divisor, Currency, roundingPolicy);

    /// <summary>Compares this amount with <paramref name="other"/>, which must be in the same currency.</summary>
    /// <param name="other">The amount to compare with.</param>
    /// <returns>
    /// A negative number, zero or a positive number as this amount is less than, equal to or greater than
    /// <paramref name="other"/>; a positive number when <paramref name="other"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public int CompareTo(Money? other)
    {
        if (other is null)
            return 1;

        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>
    /// Splits the amount into <paramref name="numberOfParts"/> equal parts that add up to exactly the original amount.
    /// </summary>
    /// <param name="numberOfParts">The number of parts. Must be at least 1.</param>
    /// <returns>
    /// <paramref name="numberOfParts"/> amounts in this currency that differ by at most one minor unit, in
    /// non-decreasing order: the leftover minor units go to the last parts. $10.00 in three parts is 3.33, 3.33,
    /// 3.34; -$10.00 is -3.34, -3.33, -3.33.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numberOfParts"/> is less than 1.</exception>
    /// <exception cref="OverflowException">
    /// The amount in minor units is outside the range of <see cref="long"/>.
    /// </exception>
    /// <remarks>Equivalent to <see cref="Allocate(IReadOnlyList{int})"/> with every ratio set to 1.</remarks>
    public IReadOnlyList<Money> Allocate(int numberOfParts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numberOfParts, 1);

        var ratios = new int[numberOfParts];
        Array.Fill(ratios, 1);
        return AllocateByRatios(ratios);
    }

    /// <summary>
    /// Splits the amount in proportion to <paramref name="ratios"/> so the parts add up to exactly the original
    /// amount.
    /// </summary>
    /// <param name="ratios">
    /// The relative weight of each part, such as <c>[70, 30]</c>. Must not be <see langword="null"/> or empty,
    /// must not contain a negative value, and must contain at least one value greater than zero.
    /// </param>
    /// <returns>One amount in this currency per ratio, in the order of <paramref name="ratios"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ratios"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="ratios"/> is empty, contains a negative value, or contains only zeros.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The amount in minor units is outside the range of <see cref="long"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Largest remainder.</b> The amount is converted to whole minor units. Each part receives the floor of its
    /// exact share, and the leftover minor units, always fewer than the number of parts, go one at a time to the
    /// parts with the largest fractional remainders; ties go to the later part.
    /// </para>
    /// <para>
    /// <b>Guarantees.</b> The parts always sum to exactly <see cref="Amount"/>, never round, and each part is within
    /// one minor unit of its exact share. A part whose ratio is zero is always zero. Negative amounts are split the
    /// same way.
    /// </para>
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

    /// <summary>
    /// Returns the amount with exactly the currency's minor-unit digits and the currency code, in the invariant
    /// culture: <c>1234.50 USD</c>, <c>-5.00 USD</c>, <c>1500 JPY</c>.
    /// </summary>
    /// <returns>The amount, a space and the ISO 4217 code, without group separators and culture-independent.</returns>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the amount formatted with <paramref name="format"/> and <paramref name="formatProvider"/>, followed by
    /// a space and the currency code: <c>1,234.50 USD</c> for format <c>N2</c> in the invariant culture.
    /// </summary>
    /// <param name="format">
    /// A standard or custom numeric format string for <see cref="Amount"/>. <see langword="null"/> or empty uses
    /// <c>F</c> with exactly the currency's minor-unit digits. A specifier without precision, such as <c>N</c>,
    /// takes its digits from the culture, not the currency. Do not use <c>C</c>: its symbol comes from the culture,
    /// not from <see cref="Currency"/>, and the code is appended anyway.
    /// </param>
    /// <param name="formatProvider">
    /// The culture for the amount; <see langword="null"/> uses the current culture.
    /// </param>
    /// <returns>The formatted amount followed by a space and the ISO 4217 code.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not a valid numeric format string.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var amountFormat = string.IsNullOrEmpty(format) ? $"F{Currency.MinorUnitDigits}": format;
        return $"{Amount.ToString(amountFormat, formatProvider)} {Currency.Code}";
    }

    /// <summary>Adds two amounts in the same currency exactly.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum, in the operands' currency.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The sum is outside the range of <see cref="decimal"/>.</exception>
    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Add(right);
    }

    /// <summary>Subtracts one amount from another in the same currency exactly.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference, in the operands' currency.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    /// <exception cref="OverflowException">The difference is outside the range of <see cref="decimal"/>.</exception>
    public static Money operator -(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Subtract(right);
    }

    /// <summary>Reverses the sign of an amount.</summary>
    /// <param name="money">The amount.</param>
    /// <returns>The negated amount, in the same currency.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    public static Money operator -(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Negate();
    }

    /// <summary>
    /// Multiplies an amount by a factor and rounds the product with <see cref="RoundingPolicy.BankersRounding"/>.
    /// </summary>
    /// <param name="money">The amount.</param>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The rounded product, in the same currency.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The product is outside the range of <see cref="decimal"/>.</exception>
    /// <remarks>Call <see cref="Multiply"/> to choose a different rounding policy.</remarks>
    public static Money operator *(Money money, decimal factor)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Multiply(factor);
    }

    /// <summary>
    /// Divides an amount by a divisor and rounds the quotient with <see cref="RoundingPolicy.BankersRounding"/>.
    /// </summary>
    /// <param name="money">The amount.</param>
    /// <param name="divisor">The divisor. Must not be zero.</param>
    /// <returns>The rounded quotient, in the same currency.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    /// <exception cref="OverflowException">The quotient is outside the range of <see cref="decimal"/>.</exception>
    /// <remarks>
    /// Call <see cref="Divide"/> to choose a different rounding policy, or <see cref="Allocate(int)"/> to split an
    /// amount without losing a minor unit.
    /// </remarks>
    public static Money operator /(Money money, decimal divisor)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Divide(divisor);
    }

    /// <summary>Returns whether <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is smaller.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    /// <summary>Returns whether <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is smaller or equal.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    /// <summary>Returns whether <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is larger.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    /// <summary>Returns whether <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is larger or equal.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">
    /// The currencies differ; the error code is <c>money.currency_mismatch</c>.
    /// </exception>
    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    /// <summary>
    /// Creates an amount rounded with <paramref name="roundingPolicy"/>, throwing on invalid input instead of
    /// returning a failed result; used by conversions within this package.
    /// </summary>
    /// <param name="amount">The unrounded amount in major units.</param>
    /// <param name="currency">The currency. Must not be <see langword="null"/>.</param>
    /// <param name="roundingPolicy">How to round to the minor unit. Must be a defined value.</param>
    /// <returns>The rounded amount.</returns>
    /// <exception cref="DomainException">
    /// <paramref name="currency"/> is <see langword="null"/>, or <paramref name="roundingPolicy"/> is not a defined
    /// value.
    /// </exception>
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

    // Backs the EF Core materialization constructor's precision guard. Not IBusinessRule-public: it exists
    // solely to reuse CheckRule's existing throw shape and is never reachable outside this file.
    private sealed class StoredPrecisionRule(decimal amount, Currency currency) : IBusinessRule
    {
        public string Code => "money.stored_precision_exceeded";

        public string Message =>
            $"Stored amount {amount.ToString(CultureInfo.InvariantCulture)} has more decimal places than " +
            $"{currency.Code}'s {currency.MinorUnitDigits}-digit minor unit allows.";

        public bool IsBroken() => Math.Round(amount, currency.MinorUnitDigits, MidpointRounding.ToEven) != amount;
    }
}
