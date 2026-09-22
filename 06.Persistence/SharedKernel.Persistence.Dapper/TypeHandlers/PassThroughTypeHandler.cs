using System.Data;
using Dapper;
using Pgvector;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Lets Dapper pass pgvector values (<see cref="Vector"/>, <see cref="HalfVector"/>,
/// <see cref="SparseVector"/>) as parameters and map them from columns. Always registered by
/// <see cref="DapperConfiguration.Apply"/>.
/// </summary>
/// <remarks>
/// Npgsql itself converts the values; the data source needs pgvector's mapping
/// (<c>UseVector = true</c> in the database's settings section).
/// </remarks>
internal sealed class PassThroughTypeHandler<T> : SqlMapper.TypeHandler<T>
    where T : class
{
    public override void SetValue(IDbDataParameter parameter, T? value) =>
        parameter.Value = value is null ? DBNull.Value : value;

    public override T? Parse(object value) => (T)value;
}
