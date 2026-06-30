namespace SharedKernel.Application.Behaviors.Resilience;

/// <summary>
/// Marks a request as eligible for retry-with-backoff via <see cref="ResilienceBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// Zero-member marker, named consistently with <c>IAuthorizeRequest</c>/<c>IIdempotentRequest</c>.
/// A request opts in to retry-with-backoff only if it is provably safe to retry. Can be implemented
/// by:
/// <list type="bullet">
/// <item><description>a query (queries are idempotent by definition — re-running a read is always safe), or</description></item>
/// <item><description>
/// a command that also implements <c>IIdempotentRequest</c> (retry-safety for a mutation is
/// borrowed from idempotency: if a duplicate submission is already detected and rejected by
/// <c>IdempotentCommandBehavior</c>, a retried attempt of the same logical command is equally safe).
/// </description></item>
/// </list>
/// <para>
/// <b>The retry-after-partial-commit hazard, resolved:</b> a command implementing
/// <see cref="IRetryableRequest"/> without also implementing <c>IIdempotentRequest</c> is a
/// documented misuse, not mechanically prevented — there is no compile-time way to require
/// "interface A implies interface B" across two independent marker interfaces in C#. This is a
/// deliberate, accepted gap (documented here and in <c>CLAUDE.md</c>'s Hard Violations section),
/// not an oversight. The hazard itself is resolved by where <see cref="ResilienceBehavior{TRequest,TResponse}"/>
/// sits in the pipeline: it wraps <c>IdempotentCommandBehavior</c> + <c>TransactionBehavior</c>, so
/// a retry re-runs the full duplicate-check-then-commit unit, never just a bare second commit
/// attempt.
/// </para>
/// </remarks>
public interface IRetryableRequest;
