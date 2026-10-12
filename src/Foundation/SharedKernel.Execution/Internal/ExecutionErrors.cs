using SharedKernel.Primitives.Errors;

namespace SharedKernel.Execution.Internal;

/// <summary>Builds the canonical <see cref="Error"/> for each classified failure of this package.</summary>
internal static class ExecutionErrors
{
    public static Error CommitOutcomeUnknown() =>
        Error.Timeout(
            ExecutionErrorCodes.CommitOutcomeUnknown,
            "The transaction's COMMIT failed without a response from the database, so it is unknown whether the "
                + "changes were applied. The operation was not retried; re-read the data before repeating it.");

    public static Error TransactionRolledBack() =>
        Error.Unexpected(
            ExecutionErrorCodes.TransactionRolledBack,
            "The transaction was rolled back: work that joined it failed (a nested ExecuteInTransactionAsync "
                + "returned a failed Result or threw), so it was marked rollback-only and nothing was committed, "
                + "although the outermost operation completed successfully.");
}
