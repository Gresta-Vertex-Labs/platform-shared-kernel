using System.Data;
using Dapper;
using SharedKernel.Domain.StronglyTypedIds;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Abstract Dapper type handler for strongly-typed ID types that wrap a primitive
/// <typeparamref name="TValue"/>.
/// </summary>
/// <typeparam name="TStronglyTypedId">
/// The strongly-typed ID type (e.g., <c>OrderId</c>). Must extend <see cref="StronglyTypedId{TValue}"/>.
/// </typeparam>
/// <typeparam name="TValue">
/// The underlying primitive type (e.g., <see cref="Guid"/>, <see cref="int"/>).
/// </typeparam>
/// <remarks>
/// <para>
/// Consuming services implement a concrete one-liner handler per strongly-typed ID type:
/// <code>
/// public sealed class OrderIdTypeHandler : StronglyTypedIdTypeHandler&lt;OrderId, Guid&gt;
/// {
///     protected override OrderId FromValue(Guid value) => new(value);
/// }
/// </code>
/// </para>
/// <para>
/// Register all handlers at startup via <see cref="DapperTypeHandlers.Register"/> and the
/// consuming service's own call to <see cref="SqlMapper.AddTypeHandler{T}"/>.
/// </para>
/// <para>
/// Zero reflection in the hot path — <see cref="SetValue"/> uses the <c>implicit operator TValue</c>
/// on <see cref="StronglyTypedId{TValue}"/> (a static method call); <see cref="Parse"/> delegates
/// to the abstract <see cref="FromValue"/> factory method implemented by the subclass.
/// </para>
/// </remarks>
public abstract class StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>
    : SqlMapper.TypeHandler<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>
    /// Writes the underlying <typeparamref name="TValue"/> to the database parameter.
    /// Uses the <c>implicit operator TValue</c> on <typeparamref name="TStronglyTypedId"/> —
    /// no reflection, AOT-safe.
    /// </summary>
    public override void SetValue(IDbDataParameter parameter, TStronglyTypedId? value)
    {
        parameter.Value = value is null ? DBNull.Value : (object)(TValue)value; // implicit operator TValue on StronglyTypedId<TValue>
    }

    /// <summary>
    /// Reads the underlying <typeparamref name="TValue"/> from the database result and
    /// constructs the <typeparamref name="TStronglyTypedId"/> via <see cref="FromValue"/>.
    /// </summary>
    public override TStronglyTypedId Parse(object value)
    {
        return FromValue((TValue)Convert.ChangeType(value, typeof(TValue)));
    }

    /// <summary>
    /// Creates a <typeparamref name="TStronglyTypedId"/> from the underlying <typeparamref name="TValue"/>.
    /// Subclasses implement this as a single-line constructor call, e.g. <c>new OrderId(value)</c>.
    /// </summary>
    /// <param name="value">The underlying primitive value read from the database.</param>
    /// <returns>The strongly-typed ID wrapping <paramref name="value"/>.</returns>
    protected abstract TStronglyTypedId FromValue(TValue value);
}
