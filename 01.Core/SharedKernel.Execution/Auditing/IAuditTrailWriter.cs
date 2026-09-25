namespace SharedKernel.Execution.Auditing;

/// <summary>
/// Appends an entry to the service's append-only audit trail.
/// </summary>
/// <remarks>
/// <para>
/// One member, and it is not an update or a delete: nothing reachable through this contract can
/// change or remove a recorded entry. <c>SharedKernel.Application.Behaviors</c>' <c>AuditingBehavior</c>
/// calls it for commands that opt in; <c>SharedKernel.Persistence.EfCore.Auditing</c> implements it.
/// </para>
/// <para>
/// <strong>Transaction rule.</strong>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="AuditOutcome.Succeeded"/>: the entry is written inside the caller's active business
/// transaction and commits or rolls back with it — a "succeeded" attestation must never outlive the
/// change it describes. The EF Core writer throws <see cref="InvalidOperationException"/> when no
/// transaction is active; the pipeline writes it from <see cref="Transactions.IUnitOfWork.OnBeforeCommit"/>.
/// </description></item>
/// <item><description>
/// <see cref="AuditOutcome.Failed"/>: the entry is written on its own connection and committed at
/// once, independent of the business transaction, which by then has rolled back.
/// </description></item>
/// </list>
/// </remarks>
public interface IAuditTrailWriter
{
    /// <summary>Records <paramref name="entry"/>.</summary>
    /// <param name="entry">The entry to record.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <exception cref="InvalidOperationException">
    /// A <see cref="AuditOutcome.Succeeded"/> entry was recorded outside an active transaction (EF Core writer).
    /// </exception>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
