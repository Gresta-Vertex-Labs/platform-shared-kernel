using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Execution.Transactions;

/// <summary>
/// A unit of work was rolled back although its outermost operation completed successfully, because work that
/// joined its transaction failed (a nested <c>ExecuteInTransactionAsync</c> returned a failed <c>Result</c> or
/// threw) and the transaction was marked rollback-only.
/// </summary>
/// <remarks>
/// <para>
/// Changes staged by a failed nested operation cannot be separated from the rest of the transaction, so nothing
/// is committed. Either let the failure propagate from the outer operation, or run the nested work in its own
/// unit of work outside the outer transaction.
/// </para>
/// <para>
/// Its <see cref="SharedKernelException.Error"/> has the code <see cref="ExecutionErrorCodes.TransactionRolledBack"/>
/// and <see cref="ErrorType.Unexpected"/>: a composition defect, not an outage.
/// </para>
/// </remarks>
public sealed class TransactionRolledBackException : SharedKernelException
{
    /// <summary>Initializes a new instance.</summary>
    public TransactionRolledBackException()
        : base(ExecutionErrors.TransactionRolledBack())
    {
    }

    /// <summary>Initializes a new instance with an explicit error.</summary>
    /// <param name="error">The error describing the failure.</param>
    public TransactionRolledBackException(Error error)
        : base(error)
    {
    }

    /// <summary>Initializes a new instance with an explicit error and the failure that caused the rollback.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception that caused the rollback.</param>
    public TransactionRolledBackException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
