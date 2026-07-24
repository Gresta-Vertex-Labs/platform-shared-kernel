using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Workflows.Temporal.Authoring;

/// <summary>
/// The sole <c>05.Application</c> bridge: a closed-generic activity base that resolves
/// <see cref="ISender"/>, sends <typeparamref name="TCommand"/> through the full MediatR pipeline
/// (validation, authorization, transaction, logging, metrics), and maps the resulting
/// <see cref="Result"/> through <see cref="Failures.WorkflowFailureMapper"/>.
/// </summary>
/// <typeparam name="TCommand">The void-returning command type to dispatch.</typeparam>
/// <remarks>
/// <para>
/// This is a closed generic per command — no reflection, no <c>MakeGenericType</c>, no polymorphic
/// payload deserialisation. The rejected alternative — a single non-generic "dispatch any command"
/// activity reconstructing an <c>ICommand</c> from a serialised envelope at runtime — would need
/// polymorphic deserialisation of an arbitrary command from a payload persisted permanently in
/// Temporal's event history: reflection-dependent, AOT-hostile, and a versioning trap the moment a
/// command's shape changes while old histories still replay.
/// </para>
/// <para>
/// <b>Why this base's <see cref="ExecuteAsync"/> deliberately carries no <c>[Activity]</c>
/// attribute — verified against the real Temporalio 1.17.0 assembly:</b> Temporal derives an
/// activity's registered name from its attributed method's own name (stripping an <c>Async</c>
/// suffix) when no explicit name is supplied — never from the declaring or concrete type name. Two
/// sibling classes that both inherit an identically-attributed base method register under the
/// <em>identical</em> Temporal activity name, so a worker hosting more than one
/// <see cref="CommandActivity{TCommand}"/> subtype — the overwhelmingly common case — would collide.
/// The concrete sealed activity therefore supplies its own <c>[Activity]</c>-attributed entry point
/// with an explicit name, e.g.:
/// <code>
/// public sealed class ApproveOrderActivity : CommandActivity&lt;ApproveOrderCommand&gt;
/// {
///     public ApproveOrderActivity(ISender sender, ILogger&lt;ApproveOrderActivity&gt; logger, IClock clock)
///         : base(sender, logger, clock) { }
///
///     [Activity(nameof(ApproveOrderActivity))]
///     public override Task ExecuteAsync(ApproveOrderCommand command, CancellationToken cancellationToken = default)
///         =&gt; base.ExecuteAsync(command, cancellationToken);
/// }
/// </code>
/// This costs one five-line class per command and has none of the reflection/AOT/versioning
/// properties of the rejected non-generic alternative.
/// </para>
/// </remarks>
public abstract class CommandActivity<TCommand> : ActivityBase
    where TCommand : ICommand
{
    private readonly ISender _sender;

    /// <summary>Initializes the activity with the MediatR sender and ordinary DI dependencies.</summary>
    protected CommandActivity(ISender sender, ILogger logger, IClock clock)
        : base(logger, clock)
    {
        _sender = sender;
    }

    /// <summary>
    /// Sends <paramref name="command"/> through the MediatR pipeline and throws the mapped Temporal
    /// failure if it fails. A <see cref="Result"/> never reaches the end of this method unmapped.
    /// </summary>
    public virtual async Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        Result result = await _sender.Send(command, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw FailFrom(result);
        }
    }
}

/// <summary>
/// The value-returning sibling of <see cref="CommandActivity{TCommand}"/>. See that type's remarks
/// for why the concrete sealed activity must supply its own explicitly-named <c>[Activity]</c> entry
/// point.
/// </summary>
/// <typeparam name="TCommand">The value-returning command type to dispatch.</typeparam>
/// <typeparam name="TResult">The command's unwrapped result payload type.</typeparam>
public abstract class CommandActivity<TCommand, TResult> : ActivityBase
    where TCommand : ICommand<TResult>
{
    private readonly ISender _sender;

    /// <summary>Initializes the activity with the MediatR sender and ordinary DI dependencies.</summary>
    protected CommandActivity(ISender sender, ILogger logger, IClock clock)
        : base(logger, clock)
    {
        _sender = sender;
    }

    /// <summary>
    /// Sends <paramref name="command"/> through the MediatR pipeline, returning the unwrapped result
    /// on success or throwing the mapped Temporal failure on failure. A <see cref="Result{T}"/>
    /// never reaches the end of this method unmapped.
    /// </summary>
    public virtual async Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        Result<TResult> result = await _sender.Send(command, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw Fail(result.Error);
        }

        return result.Value;
    }
}
