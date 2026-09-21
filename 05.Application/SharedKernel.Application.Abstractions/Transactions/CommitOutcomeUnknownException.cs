namespace SharedKernel.Application.Transactions;

/// <summary>
/// The <c>COMMIT</c> of a unit of work failed in a way that leaves its outcome unknown: the connection broke,
/// timed out or was cancelled after the commit was sent, so the transaction may or may not have been applied.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never retried.</strong> Replaying the operation after an ambiguous commit could apply it twice, so a
/// retrying execution strategy does not retry this exception and <c>ExecuteInTransactionAsync</c> does not
/// replay the delegate. The change trackers of the unit of work are cleared.
/// </para>
/// <para>
/// <strong>What to do.</strong> Treat the operation as "possibly done": re-read the affected data (or look up
/// the idempotency key the request carried) before deciding to repeat it. A commit the database rejected with
/// an error (a deferred constraint, a serialization failure) is not ambiguous — it rolled back — and surfaces as
/// that error instead.
/// </para>
/// </remarks>
public sealed class CommitOutcomeUnknownException : Exception
{
    /// <summary>Initializes a new instance with the failure raised by the commit.</summary>
    /// <param name="innerException">The exception the commit raised.</param>
    public CommitOutcomeUnknownException(Exception innerException)
        : base(
            "The transaction's COMMIT failed without a response from the database, so it is unknown whether the "
                + "changes were applied. The operation was not retried; re-read the data before repeating it.",
            innerException)
    {
    }
}
