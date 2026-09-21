using System.Data;
using System.Globalization;
using Dapper;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Dapper type handler for a <see cref="SmartEnum{TEnum, TValue}"/>: writes the member's underlying value
/// and reads it back with <see cref="SmartEnum{TEnum, TValue}.TryFromValue"/>.
/// </summary>
/// <typeparam name="TEnum">The SmartEnum type.</typeparam>
/// <typeparam name="TValue">Its underlying value type.</typeparam>
/// <remarks>Register with <c>DapperConfigurationBuilder.AddSmartEnum&lt;TEnum, TValue&gt;()</c>.</remarks>
internal sealed class SmartEnumTypeHandler<TEnum, TValue> : SqlMapper.TypeHandler<TEnum>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>
{
    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, TEnum? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No member has the stored value.</exception>
    public override TEnum Parse(object value)
    {
        var typedValue = value is TValue direct
            ? direct
            : (TValue)Convert.ChangeType(value, typeof(TValue), CultureInfo.InvariantCulture);

        if (!SmartEnum<TEnum, TValue>.TryFromValue(typedValue, out var result) || result is null)
            throw new InvalidOperationException($"No {typeof(TEnum).Name} member found for value '{typedValue}'.");

        return result;
    }
}
