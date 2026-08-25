using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Domain;

/// <summary>
/// A monetary amount in a given currency.
/// <para>
/// Note the primary-constructor form: <see cref="ValueObject"/>'s base constructor runs
/// <see cref="Validate"/>, and field initializers execute <i>before</i> the base constructor
/// while a constructor body executes <i>after</i> it. Assigning these properties in a body
/// would let validation observe unset values.
/// </para>
/// </summary>
public sealed class Money(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;
    public string Currency { get; } = (currency ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Result-returning factory — the platform's preferred creation path.</summary>
    public static Result<Money> Create(decimal amount, string currency)
        => TryCreate(() => new Money(amount, currency));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    protected override IEnumerable<Error>? Validate()
    {
        if (Amount < 0)
            yield return Error.Validation("money.negative", "Amount must not be negative.");

        if (Currency.Length != 3)
            yield return Error.Validation("money.currency", "Currency must be a 3-letter ISO code.");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
