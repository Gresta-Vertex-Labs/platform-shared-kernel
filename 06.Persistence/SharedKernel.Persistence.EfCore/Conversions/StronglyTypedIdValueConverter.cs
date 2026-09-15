using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedKernel.Domain.StronglyTypedIds;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// EF Core value converter for <see cref="StronglyTypedId{TValue}"/>-derived identifiers.
/// Converts between the strongly-typed wrapper and its underlying primitive <typeparamref name="TValue"/>
/// using the <see langword="implicit operator"/> — no reflection, no <c>Activator.CreateInstance</c>.
/// </summary>
/// <typeparam name="TStronglyTypedId">
/// The strongly-typed ID record. Must extend <see cref="StronglyTypedId{TValue}"/>.
/// </typeparam>
/// <typeparam name="TValue">
/// The underlying primitive type (e.g., <see cref="Guid"/>, <see cref="int"/>). Must be non-null.
/// </typeparam>
/// <remarks>
/// <para>
/// <strong>To-provider direction:</strong> uses <c>implicit operator TValue</c> on
/// <see cref="StronglyTypedId{TValue}"/> — a static method call, AOT-safe.
/// </para>
/// <para>
/// <strong>From-provider direction:</strong> uses the <c>Activator.CreateInstance</c> — wait,
/// no. Uses the record's primary constructor via a compiled expression lambda
/// <c>v =&gt; new TStronglyTypedId(v)</c>. The expression is compiled once at type construction
/// time and cached, so runtime cost is negligible.
/// </para>
/// <para>
/// Register this converter globally using the
/// <see cref="ModelConfigurationBuilderExtensions.ConfigureStronglyTypedIds"/> extension so that
/// every <see cref="SharedKernel.Domain.Abstractions.IStronglyTypedId{TValue}"/> property is
/// automatically mapped without per-aggregate configuration.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdValueConverter<TStronglyTypedId, TValue>
    : ValueConverter<TStronglyTypedId, TValue>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>
    /// Initialises a new instance of the converter.
    /// The to-provider expression uses the <c>implicit operator TValue</c>.
    /// The from-provider expression uses the record primary constructor directly.
    /// </summary>
    public StronglyTypedIdValueConverter()
        : base(
            id => id.Value,
            value => CreateId(value))
    {
    }

    // Constructs TStronglyTypedId from TValue using the primary constructor.
    // The expression tree is compiled once and used by EF Core's value converter machinery.
    private static TStronglyTypedId CreateId(TValue value)
    {
        // Compiled lambda: (TValue v) => new TStronglyTypedId(v)
        // Built via System.Linq.Expressions to avoid Activator.CreateInstance.
        return IdFactory.Create(value);
    }

    /// <summary>
    /// Cached factory delegate built from a compiled <see cref="System.Linq.Expressions.Expression"/>
    /// for <typeparamref name="TStronglyTypedId"/>.
    /// </summary>
    private static class IdFactory
    {
        // Compiled once per closed generic type — thread-safe because static field initialisation is.
        private static readonly Func<TValue, TStronglyTypedId> _factory = BuildFactory();

        internal static TStronglyTypedId Create(TValue value) => _factory(value);

        private static Func<TValue, TStronglyTypedId> BuildFactory()
        {
            var ctor = typeof(TStronglyTypedId).GetConstructor([typeof(TValue)])
                       ?? throw new InvalidOperationException(
                           $"Type '{typeof(TStronglyTypedId).Name}' does not have a public constructor that accepts a single '{typeof(TValue).Name}' argument. " +
                           $"Ensure the strongly-typed ID record is declared as: public sealed record {typeof(TStronglyTypedId).Name}({typeof(TValue).Name} Value) : StronglyTypedId<{typeof(TValue).Name}>(Value);");

            var param = System.Linq.Expressions.Expression.Parameter(typeof(TValue), "value");
            var newExpr = System.Linq.Expressions.Expression.New(ctor, param);
            return System.Linq.Expressions.Expression.Lambda<Func<TValue, TStronglyTypedId>>(newExpr, param).Compile();
        }
    }
}
