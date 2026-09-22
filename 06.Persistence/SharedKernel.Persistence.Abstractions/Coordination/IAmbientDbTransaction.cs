using System.ComponentModel;
using System.Data.Common;

namespace SharedKernel.Persistence.Abstractions.Coordination;

/// <summary>
/// Exposes the ADO.NET connection and transaction currently open on
/// <c>IUnitOfWork</c>'s active transaction, if any, so a non-EF-Core caller
/// (a Dapper command service) can enlist in the same atomic unit of work.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped and updated by the EF Core transaction implementation
/// (<c>EfUnitOfWork</c>) as a transaction begins, commits, and rolls back. A Dapper command service
/// resolves this seam instead of opening its own connection whenever it must write inside the same
/// transaction as EF Core-tracked changes.
/// </para>
/// <para>
/// <see cref="Current"/> is <see langword="null"/> whenever no explicit
/// <c>IUnitOfWork.ExecuteInTransactionAsync</c> scope is
/// active for the current DI scope — a Dapper command service used outside such a scope opens and
/// manages its own connection instead.
/// </para>
/// <para>Infrastructure seam between the persistence packages; not for application code.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAmbientDbTransaction
{
    /// <summary>
    /// Gets the connection and transaction pair currently open on the active explicit transaction, or
    /// <see langword="null"/> when no explicit transaction is active for the current scope.
    /// </summary>
    (DbConnection Connection, DbTransaction Transaction)? Current { get; }
}
