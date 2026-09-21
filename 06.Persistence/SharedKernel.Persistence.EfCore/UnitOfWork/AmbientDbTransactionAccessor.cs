using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Coordination;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// Scoped, mutable implementation of <see cref="IAmbientDbTransaction"/> — the write side that
/// <see cref="EfUnitOfWork"/> updates as a
/// transaction begins and ends.
/// </summary>
/// <remarks>
/// Registered scoped by
/// <c>EfCorePersistenceBuilder.Build()</c> — one instance per DI scope, shared
/// between the EF Core transaction machinery (which sets <see cref="Current"/>) and any Dapper
/// command service resolving <see cref="IAmbientDbTransaction"/> to enlist in the same transaction.
/// </remarks>
internal sealed class AmbientDbTransactionAccessor : IAmbientDbTransaction
{
    /// <inheritdoc />
    public (DbConnection Connection, DbTransaction Transaction)? Current { get; internal set; }
}
