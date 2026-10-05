namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// A lower bound, learned by this process, below which every ledger record is sealed: every record whose inserting
/// transaction id is below <see cref="Value"/> has a link.
/// </summary>
/// <remarks>
/// <para>
/// It only bounds the index range the sealer and the sealing probe scan; it is never read from the links table.
/// The former design trusted the highest <c>record_insert_xid</c> among the links as its watermark, so a single forged
/// link with a large <c>record_insert_xid</c> stopped all sealing and made the probe report no backlog (finding S5).
/// </para>
/// <para>
/// It stays true once learned: links are append-only, and no record can later appear below a transaction-id horizon
/// (every transaction below it has ended). It is learned only from scans that saw, below the horizon, that every
/// record before a position has a link. A new process starts at zero and scans the ledger's sealed prefix once.
/// </para>
/// </remarks>
internal sealed class AuditSealedFloor
{
    private long _value;

    /// <summary>Gets the transaction id below which every record is sealed.</summary>
    public long Value => Interlocked.Read(ref _value);

    /// <summary>Raises the floor to <paramref name="candidate"/> if that is higher.</summary>
    public void Advance(long candidate)
    {
        var current = Interlocked.Read(ref _value);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref _value, candidate, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
