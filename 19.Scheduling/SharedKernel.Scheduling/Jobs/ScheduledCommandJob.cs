using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Diagnostics;

namespace SharedKernel.Scheduling.Jobs;

/// <summary>
/// The sole <c>05.Application</c> bridge for this package: dispatches a
/// <typeparamref name="TCommand"/> built by a registration-time factory through the full MediatR
/// pipeline (validation, authorization, transaction, logging, metrics), and reports the resulting
/// <see cref="Result"/> back to the hosted scheduling loop.
/// </summary>
/// <typeparam name="TCommand">The void-returning command type to dispatch.</typeparam>
/// <remarks>
/// <para>
/// The scheduling-side counterpart to <c>17.Workflows</c>' <c>CommandActivity&lt;TCommand&gt;</c> — a
/// closed generic per command, zero reflection (no <c>Type.GetMethod</c>/<c>MakeGenericMethod</c>/
/// <c>Invoke</c>, forbidden platform-wide). Unlike <c>CommandActivity&lt;TCommand&gt;</c>, this type
/// needs no consumer-authored subclass: Temporal's activity-name-collision problem that forces that
/// pattern there simply does not exist here, so <see cref="ScheduledCommandJob{TCommand}"/> is
/// <see langword="sealed"/> and registered directly by <c>IScheduledJobRegistry.AddRecurring</c>/
/// <c>.AddDeferred</c> (as a transient, one instance resolved per job execution's own DI scope — never
/// a captured root-scoped <c>ISender</c>).
/// </para>
/// <para>
/// A fired job has no external caller able to supply a command instance the way an HTTP request or a
/// Temporal activity input does. The command is instead built by a caller-supplied
/// <c>Func&lt;ScheduledJobExecutionContext, TCommand&gt;</c> factory, closed over at registration time
/// and invoked fresh on every single execution attempt.
/// </para>
/// </remarks>
public sealed class ScheduledCommandJob<TCommand>
    where TCommand : class, ICommand
{
    private readonly ISender _sender;
    private readonly IClock _clock;
    private readonly ILogger<ScheduledCommandJob<TCommand>> _logger;

    /// <summary>Initializes the job with the MediatR sender and ordinary DI dependencies.</summary>
    public ScheduledCommandJob(ISender sender, IClock clock, ILogger<ScheduledCommandJob<TCommand>> logger)
    {
        _sender = sender;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Builds the command via <paramref name="commandFactory"/> and sends it through the MediatR
    /// pipeline, logging the outcome. A <see cref="Result"/> is always returned — this method never
    /// swallows a failure, and never throws for an ordinary <see cref="Result.IsFailure"/> outcome.
    /// </summary>
    /// <param name="commandFactory">
    /// The registration-time factory that builds the <typeparamref name="TCommand"/> instance for this
    /// execution.
    /// </param>
    /// <param name="context">The execution context for this single fire attempt.</param>
    /// <param name="cancellationToken">
    /// The host's stopping token, threaded through to <see cref="ISender.Send"/> so the command
    /// handler can observe shutdown cooperatively.
    /// </param>
    public async Task<Result> ExecuteAsync(
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        ScheduledJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(context);

        TCommand command = commandFactory(context);

        DateTimeOffset startedAtUtc = _clock.UtcNow;
        Result result = await _sender.Send(command, cancellationToken).ConfigureAwait(false);
        TimeSpan duration = _clock.UtcNow - startedAtUtc;

        if (result.IsSuccess)
        {
            Log.JobFireSucceeded(_logger, context.JobName, typeof(TCommand).Name, duration);
        }
        else
        {
            Log.JobFireFailed(_logger, context.JobName, typeof(TCommand).Name, duration, result.Error.Code, result.Error.Message);
        }

        return result;
    }
}
