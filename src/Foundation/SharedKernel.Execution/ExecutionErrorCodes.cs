namespace SharedKernel.Execution;

/// <summary>
/// The <see cref="SharedKernel.Primitives.Errors.Error.Code"/> values carried by the exceptions of
/// <c>SharedKernel.Execution</c>.
/// </summary>
/// <remarks>
/// These are wire strings (ProblemDetails <c>code</c>, gRPC rich status, dashboards): they are never renamed.
/// </remarks>
public static class ExecutionErrorCodes
{
    /// <summary>
    /// The commit of a unit of work failed without a response, so it is unknown whether the changes were
    /// applied. Carried by <see cref="Transactions.CommitOutcomeUnknownException"/> as a timeout error.
    /// </summary>
    public const string CommitOutcomeUnknown = "execution.commit_outcome_unknown";

    /// <summary>
    /// A unit of work was rolled back although its outermost operation completed, because joined work failed.
    /// Carried by <see cref="Transactions.TransactionRolledBackException"/> as an unexpected error.
    /// </summary>
    public const string TransactionRolledBack = "execution.transaction_rolled_back";
}
