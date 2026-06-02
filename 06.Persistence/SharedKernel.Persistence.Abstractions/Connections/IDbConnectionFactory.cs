using System.Data;

namespace SharedKernel.Persistence.Abstractions.Connections;

/// <summary>
/// Factory that produces open database connections for low-level data access.
/// </summary>
/// <remarks>
/// <para>
/// Every call to <see cref="CreateConnectionAsync"/> returns a new, already-open
/// <see cref="IDbConnection"/>. The <strong>caller is responsible for disposing</strong> the
/// returned connection — recommended pattern is <c>await using var conn = await factory.CreateConnectionAsync(ct);</c>.
/// </para>
/// <para>
/// Any component needing a raw <see cref="IDbConnection"/> may inject this factory —
/// it is not restricted to Dapper. EF Core repositories obtain connections implicitly
/// through <c>DbContext</c> and do not use this factory.
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
