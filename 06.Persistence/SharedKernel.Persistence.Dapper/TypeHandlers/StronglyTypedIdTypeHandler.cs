using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using Dapper;
using SharedKernel.Domain.StronglyTypedIds;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Dapper type handler for a <see cref="StronglyTypedId{TValue}"/>: writes the underlying value and
/// reads it back through a factory.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <typeparam name="TValue">Its underlying value type.</typeparam>
/// <remarks>
/// Register with <c>DapperConfigurationBuilder.AddStronglyTypedId&lt;TId, TValue&gt;()</c>. Without an
/// explicit factory the identifier's public <typeparamref name="TValue"/> constructor (the primary
/// constructor of <c>record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value)</c>) is compiled once
/// into a delegate at registration.
/// </remarks>
internal sealed class StronglyTypedIdTypeHandler<TId, TValue> : SqlMapper.TypeHandler<TId>
    where TId : StronglyTypedId<TValue>
    where TValue : notnull
{
    private readonly Func<TValue, TId> _factory;

    /// <summary>Initialises a new handler.</summary>
    /// <param name="factory">
    /// Creates the identifier from a stored value, or <see langword="null"/> to use the identifier's public
    /// single-<typeparamref name="TValue"/> constructor.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="factory"/> is <see langword="null"/> and <typeparamref name="TId"/> has no such constructor.
    /// </exception>
    public StronglyTypedIdTypeHandler(Func<TValue, TId>? factory = null)
    {
        _factory = factory ?? CreateConstructorFactory();
    }

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, TId? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    /// <inheritdoc />
    public override TId Parse(object value)
    {
        var typedValue = value is TValue direct
            ? direct
            : (TValue)Convert.ChangeType(value, typeof(TValue), CultureInfo.InvariantCulture);

        return _factory(typedValue);
    }

    private static Func<TValue, TId> CreateConstructorFactory()
    {
        var constructor = typeof(TId).GetConstructor([typeof(TValue)])
            ?? throw new InvalidOperationException(
                $"'{typeof(TId).Name}' has no public constructor taking a single '{typeof(TValue).Name}'. "
                    + "Pass a factory to AddStronglyTypedId.");

        var value = Expression.Parameter(typeof(TValue), "value");
        return Expression.Lambda<Func<TValue, TId>>(Expression.New(constructor, value), value).Compile();
    }
}
