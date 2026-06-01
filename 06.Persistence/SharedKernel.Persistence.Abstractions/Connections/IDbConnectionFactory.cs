using System.Data;

namespace SharedKernel.Persistence.Abstractions.Connections;

/// <summary>
/// Factory that produces open database connections for use by Dapper read-side services.
/// </summary>
/// <remarks>
/// <para>
/// Every call to <see cref="CreateConnectionAsync"/> returns a new, already-open
/// <see cref="IDbConnection"/>. The <strong>caller is responsible for disposing</strong> the
/// returned connection — recommended pattern is <c>await using var conn = await factory.CreateConnectionAsync(ct);</c>.
/// </para>
/// <para>
/// This factory is used exclusively by Dapper read-side services (<c>DapperReadService</c>
/// subclasses). EF Core repositories never use this factory — they obtain connections implicitly
/// through <c>DbContext</c>.
/// </para>
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Opens and returns a new database connection.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open <see cref="IDbConnection"/> that the caller must dispose.</returns>
    Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default);
}
