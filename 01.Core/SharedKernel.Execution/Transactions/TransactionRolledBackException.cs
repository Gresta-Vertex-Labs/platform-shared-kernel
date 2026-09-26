namespace SharedKernel.Execution.Transactions;

/// <summary>
/// A unit of work was rolled back although its outermost operation completed successfully, because work that
/// joined its transaction failed (a nested <c>ExecuteInTransactionAsync</c> returned a failed <c>Result</c> or
/// threw) and the transaction was marked rollback-only.
/// </summary>
/// <remarks>
/// Changes staged by a failed nested operation cannot be separated from the rest of the transaction, so nothing
/// is committed. Either let the failure propagate from the outer operation, or run the nested work in its own
/// unit of work outside the outer transaction.
/// </remarks>
public sealed class TransactionRolledBackException : InvalidOperationException
{
    /// <summary>Initializes a new instance.</summary>
    public TransactionRolledBackException()
        : base(
            "The transaction was rolled back: work that joined it failed (a nested ExecuteInTransactionAsync "
                + "returned a failed Result or threw), so it was marked rollback-only and nothing was committed, "
                + "although the outermost operation completed successfully.")
    {
    }
}
