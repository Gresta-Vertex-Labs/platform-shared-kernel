using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// EF Core value converter for <see cref="Money"/>, packing <see cref="Money.Amount"/> and
/// <see cref="Money.Currency"/> into a single delimited <see langword="string"/> column
/// (<c>"{amount}:{currencyCode}"</c>).
/// </summary>
/// <remarks>
/// <para>
/// WO-066/P-440/D-106. <see cref="Money"/>'s real, shipped shape has a <see langword="private"/>
/// three-argument constructor (<c>amount, currency, roundingPolicy</c>), but only
/// <see cref="Money.Amount"/>/<see cref="Money.Currency"/> are ever persisted —
/// <c>RoundingPolicy</c> has no column and cannot be recovered from stored data. D-106's
/// PREFERRED path (an EF Core owned type with two independently queryable columns, materialized
/// via a reflection-located private constructor mirroring
/// <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/>) was evaluated against
/// the real EF Core 10 assembly before being adopted, and rejected:
/// </para>
/// <list type="bullet">
/// <item>
/// EF Core's automatic constructor-parameter binding cannot supply a literal
/// <c>RoundingPolicy</c> value for an owned type — every constructor parameter must bind to a
/// mapped property, navigation, or a small set of known service-parameter types, and there is
/// no supported way to inject a constant.
/// </item>
/// <item>
/// The lower-level fix — directly assigning a custom <c>InstantiationBinding</c> to the owned
/// type's <c>ConstructorBinding</c> — is <strong>not reachable through any public EF Core 10
/// API</strong>: <c>ITypeBase.ConstructorBinding</c> is read-only on every public
/// (<c>IMutableEntityType</c>/<c>IConventionEntityType</c>) surface; a setter exists only on the
/// internal <c>Metadata.Internal.TypeBase</c> type. Using it would mean reflecting into EF
/// Core's own internals — a materially different, far more fragile risk than the
/// already-accepted "reflect over our own domain type's public/private constructor" precedent
/// <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/> sets, and one that can
/// silently break on any EF Core point release.
/// </item>
/// <item>
/// The alternative public extension point, <c>IMaterializationInterceptor</c>, is architecturally
/// awkward here: interceptors are registered once at <c>DbContextOptions</c> configuration time,
/// while <c>.OwnsMoney(...)</c> is called per-property inside <c>OnModelCreating</c> — there is
/// no clean way for a model-time call to conditionally wire a context-level interceptor, and
/// registering it unconditionally on every <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext"/>
/// would impose a global cost on services that never use <see cref="Money"/>.
/// </item>
/// </list>
/// <para>
/// This converter therefore implements D-106's documented FALLBACK: a single packed-string
/// column, reconstructed via the PUBLIC <see cref="Money.Create(decimal,Currency,RoundingPolicy)"/>
/// factory — zero reflection, zero EF Core internals, reusing the exact
/// <see cref="ValueConverter{TModel,TProvider}"/> mechanism this package already relies on for
/// <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/> and
/// <c>EncryptedValueConverter</c>. The documented cost, per D-106: <see cref="Money.Amount"/> and
/// <see cref="Money.Currency"/> are NOT independently queryable/filterable in SQL — a service
/// needing <c>WHERE Currency = 'USD' AND Amount &gt; ...</c>-style queries must project/filter
/// in application code, or maintain its own separate shadow columns.
/// </para>
/// <para>
/// Reconstruction always applies <see cref="RoundingPolicy.BankersRounding"/> regardless of the
/// policy originally used to write the value — safe and idempotent, since a stored
/// <see cref="Money.Amount"/> is already rounded to its <see cref="Currency"/>'s minor-unit
/// precision at write time; re-rounding an already-rounded value to the same precision never
/// changes it, under either <see cref="RoundingPolicy"/>.
/// </para>
/// </remarks>
public sealed class MoneyValueConverter : ValueConverter<Money, string>
{
    private const char Separator = ':';

    /// <summary>
    /// Initialises a new instance of the converter.
    /// </summary>
    public MoneyValueConverter()
        : base(
            money => Pack(money),
            stored => Unpack(stored))
    {
    }

    private static string Pack(Money money) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{money.Amount}{Separator}{money.Currency.Code}");

    private static Money Unpack(string stored)
    {
        var separatorIndex = stored.IndexOf(Separator);
        if (separatorIndex < 0)
        {
            throw new InvalidOperationException(
                $"Stored Money value '{stored}' is not in the expected 'amount{Separator}currencyCode' format.");
        }

        var amountSpan = stored.AsSpan(0, separatorIndex);
        var currencyCode = stored[(separatorIndex + 1)..];

        if (!decimal.TryParse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidOperationException(
                $"Stored Money value '{stored}' has an unparsable amount component.");
        }

        var currencyResult = Currency.Create(currencyCode);
        if (currencyResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Stored Money value '{stored}' has an invalid currency component: {currencyResult.Error?.Message}");
        }

        var moneyResult = Money.Create(amount, currencyResult.Value!);
        if (moneyResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Stored Money value '{stored}' could not be reconstructed: {moneyResult.Error?.Message}");
        }

        return moneyResult.Value!;
    }
}
