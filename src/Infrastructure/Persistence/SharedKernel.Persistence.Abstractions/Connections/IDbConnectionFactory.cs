using System.Data.Common;

namespace SharedKernel.Persistence.Abstractions.Connections;

/// <summary>
/// Factory that produces open database connections for low-level data access.
/// </summary>
/// <remarks>
/// <para>
/// Every call to <see cref="CreateConnectionAsync"/> returns a new, already-open
/// <see cref="DbConnection"/>. The <strong>caller is responsible for disposing</strong> the
/// returned connection — recommended pattern is <c>await using var conn = await factory.CreateConnectionAsync(cancellationToken);</c>.
/// </para>
/// <para>
/// The return type is <see cref="DbConnection"/> rather than <see cref="System.Data.IDbConnection"/>
/// — the plain interface has no async members at all, which forced every caller (including
/// <c>await using</c> patterns and Dapper's own async query methods) through a synchronous
/// <c>Dispose()</c>/blocking path or an unsafe downcast. <see cref="DbConnection"/> is the genuine
/// base type every ADO.NET provider (including Npgsql) already returns.
/// </para>
/// <para>
/// Any component needing a raw <see cref="DbConnection"/> may inject this factory —
/// it is not restricted to Dapper. EF Core repositories obtain connections implicitly
/// through <c>DbContext</c> and do not use this factory.
/// </para>
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Opens and returns a new database connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open <see cref="DbConnection"/> that the caller must dispose.</returns>
    Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
