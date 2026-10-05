using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// EF Core value converter for <see cref="Currency"/>, mapping to/from its ISO 4217 alpha-3
/// <see langword="string"/> code column.
/// </summary>
/// <remarks>
/// <para>
/// This is a DELIBERATE REFINEMENT of the pattern used by
/// <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/>: <see cref="Currency"/>
/// derives from <see cref="SharedKernel.Domain.ValueObjects.SingleValueObject{TValue}"/>, which
/// already exposes a public <see cref="Currency.Create"/> factory, so
/// reconstruction never needs the reflection-located-constructor + compiled
/// <c>Expression.New</c> technique <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/>
/// relies on — that technique exists only because <c>StronglyTypedId&lt;TValue&gt;</c> has no
/// such factory. Zero reflection either direction.
/// </para>
/// <para>
/// <strong>To-provider direction:</strong> uses the inherited
/// <c>implicit operator string(SingleValueObject&lt;string&gt;)</c> — a plain property read,
/// AOT-safe.
/// </para>
/// <para>
/// <strong>From-provider direction:</strong> calls the public <see cref="Currency.Create"/>
/// factory and unwraps <c>.Value</c>. A stored value is assumed already-validated at write
/// time (every value ever written passed through <see cref="Currency.Create"/> itself), so a
/// reconstruction failure here indicates either data corruption or an out-of-band write and
/// throws a clear <see cref="InvalidOperationException"/> rather than silently returning a
/// half-formed value.
/// </para>
/// </remarks>
internal sealed class CurrencyValueConverter : ValueConverter<Currency, string>
{
    /// <summary>
    /// Initialises a new instance of the converter.
    /// </summary>
    public CurrencyValueConverter()
        : base(
            currency => currency.Code,
            code => FromProvider(code))
    {
    }

    private static Currency FromProvider(string code)
    {
        var result = Currency.Create(code);

        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException(
                $"Stored currency code '{code}' could not be reconstructed as a valid " +
                $"{nameof(Currency)}: {string.Join(" ", result.Errors.Select(e => e.Message))}. This indicates data corruption " +
                "or a write performed outside of Currency.Create's validation.");
    }
}
