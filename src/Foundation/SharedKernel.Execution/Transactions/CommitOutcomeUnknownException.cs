using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Execution.Transactions;

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
/// <para>
/// <strong>Classification.</strong> Its <see cref="SharedKernelException.Error"/> has the code
/// <see cref="ExecutionErrorCodes.CommitOutcomeUnknown"/> and <see cref="ErrorType.Timeout"/> (HTTP 504, gRPC
/// <c>DeadlineExceeded</c>): "outcome unknown", not "not processed". The original failure is the
/// <see cref="Exception.InnerException"/> and never part of the message.
/// </para>
/// </remarks>
public sealed class CommitOutcomeUnknownException : SharedKernelException
{
    /// <summary>Initializes a new instance with the failure raised by the commit.</summary>
    /// <param name="innerException">The exception the commit raised.</param>
    public CommitOutcomeUnknownException(Exception innerException)
        : base(ExecutionErrors.CommitOutcomeUnknown(), innerException)
    {
    }

    /// <summary>Initializes a new instance with an explicit error.</summary>
    /// <param name="error">The error describing the failure.</param>
    public CommitOutcomeUnknownException(Error error)
        : base(error)
    {
    }

    /// <summary>Initializes a new instance with an explicit error and the failure raised by the commit.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The exception the commit raised.</param>
    public CommitOutcomeUnknownException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
