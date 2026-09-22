using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Dapper type handler that stores <typeparamref name="T"/> in a <c>jsonb</c> column, serialized with a
/// source-generated <see cref="JsonTypeInfo{T}"/> (no reflection-based serialization).
/// </summary>
/// <typeparam name="T">The mapped type.</typeparam>
/// <remarks>Register with <c>DapperConfigurationBuilder.AddJsonb(MyJsonContext.Default.Address)</c>.</remarks>
internal sealed class JsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>Initialises a new handler.</summary>
    /// <param name="typeInfo">The serialization metadata of <typeparamref name="T"/>.</param>
    public JsonbTypeHandler(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        _typeInfo = typeInfo;
    }

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter is NpgsqlParameter npgsqlParameter)
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;

        parameter.Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, _typeInfo);
    }

    /// <inheritdoc />
    public override T? Parse(object value) => value switch
    {
        string json => JsonSerializer.Deserialize(json, _typeInfo),
        JsonDocument document => document.Deserialize(_typeInfo),
        JsonElement element => element.Deserialize(_typeInfo),
        _ => JsonSerializer.Deserialize(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null", _typeInfo),
    };
}
