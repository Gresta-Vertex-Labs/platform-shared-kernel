using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Domain;

/// <summary>
/// A monetary amount in a given currency.
/// <para>
/// Demonstrates the value-object pattern: assign every member in the constructor, then call
/// <c>EnsureValid()</c> last so <see cref="Validate"/> sees the fully built object, and create
/// instances through a <see cref="ValidationResult{T}"/>-returning factory that reports every error.
/// A real service would use <c>SharedKernel.Domain.Monetary.Money</c>; this one stays deliberately small.
/// </para>
/// </summary>
public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = (currency ?? string.Empty).Trim().ToUpperInvariant();
        EnsureValid();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Validation-returning factory: the platform's preferred creation path.</summary>
    public static ValidationResult<Money> Create(decimal amount, string currency)
        => TryCreate(() => new Money(amount, currency));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    protected override IEnumerable<Error> Validate()
    {
        if (Amount < 0)
            yield return Error.Validation("money.negative", "Amount must not be negative.");

        if (Currency.Length != 3)
            yield return Error.Validation("money.currency", "Currency must be a 3-letter ISO code.");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
