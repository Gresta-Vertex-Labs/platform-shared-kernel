using System.Data;
using System.Globalization;
using Dapper;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Abstract Dapper type handler for <see cref="SmartEnum{TEnum, TValue}"/> types.
/// </summary>
/// <typeparam name="TEnum">
/// The concrete SmartEnum type (e.g., <c>OrderStatus</c>).
/// Must extend <see cref="SmartEnum{TEnum, TValue}"/>.
/// </typeparam>
/// <typeparam name="TValue">
/// The underlying value type of the SmartEnum (e.g., <see cref="int"/>, <see cref="string"/>).
/// </typeparam>
/// <remarks>
/// <para>
/// <see cref="SetValue"/> writes the underlying <typeparamref name="TValue"/> via
/// <see cref="SmartEnum{TEnum, TValue}.Value"/> — no reflection.
/// </para>
/// <para>
/// <see cref="Parse"/> uses <see cref="SmartEnum{TEnum, TValue}.TryFromValue"/> — no reflection.
/// Throws <see cref="InvalidOperationException"/> when the value from the database does not match
/// any known member — no silent fallback.
/// </para>
/// <para>
/// Consuming services register a concrete one-liner subclass per SmartEnum type:
/// <code>
/// public sealed class OrderStatusTypeHandler: SmartEnumTypeHandler&lt;OrderStatus, int&gt; { }
/// </code>
/// </para>
/// </remarks>
public abstract class SmartEnumTypeHandler<TEnum, TValue>
    : SqlMapper.TypeHandler<TEnum>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>
{
    /// <summary>
    /// Writes the underlying <typeparamref name="TValue"/> to the database parameter.
    /// </summary>
    public override void SetValue(IDbDataParameter parameter, TEnum? value)
    {
        parameter.Value = value is null ? DBNull.Value : (object)value.Value!;
    }

    /// <summary>
    /// Reads the <typeparamref name="TValue"/> from the database result and looks up the
    /// corresponding <typeparamref name="TEnum"/> member via
    /// <see cref="SmartEnum{TEnum, TValue}.TryFromValue"/>. No reflection used.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <typeparamref name="TEnum"/> member matches the database value.
    /// </exception>
    /// <remarks>
    /// No longer force-runs <typeparamref name="TEnum"/>'s static constructor before
    /// looking up the value: <c>SmartEnum&lt;TEnum,TValue&gt;</c> itself already guarantees every
    /// member is registered before <see cref="SmartEnum{TEnum, TValue}.TryFromValue"/> can observe
    /// an empty list (see that type's own <c>ForceEnumStaticConstructor</c> remarks) — calling
    /// <c>RuntimeHelpers.RunClassConstructor</c> here a second time, on every single row, was
    /// redundant reflection with no correctness benefit.
    /// </remarks>
    public override TEnum Parse(object value)
    {
        var typedValue = (TValue)Convert.ChangeType(value, typeof(TValue), CultureInfo.InvariantCulture);

        if (!SmartEnum<TEnum, TValue>.TryFromValue(typedValue, out var result) || result is null)
        {
            throw new InvalidOperationException(
                $"No {typeof(TEnum).Name} member found for value '{typedValue}'.");
        }

        return result;
    }
}
