namespace SharedKernel.Application;

/// <summary>
/// Names a position in the canonical MediatR pipeline order that <see cref="ApplicationPipelineBuilder.WithBehavior"/>
/// can append a custom behavior into.
/// </summary>
/// <remarks>
/// <para>
/// The five stages, in fixed execution order (outermost first): <see cref="Observability"/> (Tracing,
/// Logging, Metrics), <see cref="Authorization"/>, <see cref="Validation"/>, <see cref="Query"/>,
/// <see cref="Command"/>. Within a stage, this package's own built-in behaviors — if any — always
/// run first; a custom behavior added to that stage runs after them, in the order it was added.
/// </para>
/// <para>
/// <see cref="Query"/> ships no built-in behavior of its own — it exists purely so a sibling package
/// (e.g. <c>SharedKernel.Application.Caching</c>'s <c>CachingBehavior</c>) has a stable,
/// documented slot between validation and the command stage. <see cref="Command"/>'s built-ins are
/// <c>CommandScopeBehavior</c>, <c>IdempotencyBehavior</c>, <c>AuditingBehavior</c> (records
/// failures, outside the transaction), <c>TransactionBehavior</c>, and the inner half of auditing
/// (queues the success record on the transaction's pre-commit hook), in that order; a custom behavior
/// added to <see cref="Command"/> (e.g. <c>CacheInvalidationBehavior</c>) runs innermost among them,
/// inside the transaction, closest to the handler.
/// </para>
/// </remarks>
public enum PipelineStage
{
    /// <summary>Tracing, Logging, and Metrics — the outermost stage.</summary>
    Observability,

    /// <summary>Authorization.</summary>
    Authorization,

    /// <summary>Validation.</summary>
    Validation,

    /// <summary>The read-side stage — no built-in behavior of its own.</summary>
    Query,

    /// <summary>The write-side stage — CommandScope, Idempotency, Auditing, Transaction.</summary>
    Command,
}
