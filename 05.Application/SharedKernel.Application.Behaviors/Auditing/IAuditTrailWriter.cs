namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// A minimal seam for recording an append-only audit-trail entry.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Persistence.Abstractions.IAuditTrailWriter</c>
/// (<c>06.Persistence</c>). <c>05.Application</c> can never reference <c>06.Persistence</c>
/// (layering runs the other direction), so <see cref="AuditingBehavior{TRequest,TResponse}"/>
/// depends on this local interface instead; the consuming service bridges it to the real, richer
/// <c>06.Persistence.Abstractions.IAuditTrailWriter</c> at the composition root. Same-name-
/// different-namespace precedent, mirroring <c>Transaction.IUnitOfWork</c>'s existing bridge shape
/// exactly — never a compiled reference to <c>06.Persistence</c> from this package.
/// </para>
/// <para>
/// Deliberately smaller than the real contract, which returns a persisted record and internally
/// resolves actor identity, tenant identity, timestamp, and hash-chain linkage — this local seam
/// never needs any of that; the composition-root bridge adapter maps <see cref="AuditEntry"/> onto
/// the real, richer contract and discards the persisted record, since
/// <see cref="AuditingBehavior{TRequest,TResponse}"/> never needs it back.
/// </para>
/// <para>
/// This package ships only the interface — no implementation.
/// </para>
/// </remarks>
public interface IAuditTrailWriter
{
    /// <summary>Records <paramref name="entry"/> to the append-only audit trail.</summary>
    /// <param name="entry">The entry to record.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <remarks>
    /// A failed write (a thrown exception) must propagate to the caller unchanged —
    /// <see cref="AuditingBehavior{TRequest,TResponse}"/> never catches or swallows it.
    /// </remarks>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
