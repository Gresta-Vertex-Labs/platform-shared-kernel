using System.Data;
using Dapper;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Lets Dapper pass a <see cref="TenantId"/> (or <c>TenantId?</c>) as a <c>uuid</c> parameter and map a
/// <c>uuid</c> column to one. Always registered by <see cref="DapperConfiguration.Apply"/>.
/// </summary>
/// <remarks>
/// An unset <c>default(TenantId)</c> is sent as <see langword="null"/>, never as <see cref="Guid.Empty"/>, so it
/// can never match a tenant row. A stored <see cref="Guid.Empty"/> reads back as <c>default(TenantId)</c>.
/// </remarks>
internal sealed class TenantIdTypeHandler : SqlMapper.TypeHandler<TenantId>
{
    public override void SetValue(IDbDataParameter parameter, TenantId value)
    {
        parameter.DbType = DbType.Guid;
        parameter.Value = value.IsDefault ? DBNull.Value : value.Value;
    }

    public override TenantId Parse(object value)
    {
        var guid = value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            _ => throw new DataException($"Cannot convert a value of type '{value.GetType().Name}' to a TenantId."),
        };

        return guid == Guid.Empty ? default : new TenantId(guid);
    }
}
