using System.Diagnostics;
using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Shared distributed-tracing helper for <c>EfRepository{TAggregate,TId}</c>/
/// <c>EfReadRepository{TAggregate,TId}</c> repository operations.
/// </summary>
/// <remarks>
/// Avoids duplicating the start/tag/try-catch/finish boilerplate across every repository method.
/// Span name is <c>"{typeof(TAggregate).Name}.{operationName}"</c>,
/// <see cref="ActivityKind.Internal"/> (an EF Core repository call is in-process
/// persistence-layer work, not an outbound RPC to a separate service; <c>ActivityKind.Client</c> is
/// reserved for spans representing a synchronous call to a remote/external service).
/// <see cref="PersistenceTagKeys.AggregateType"/>/<see cref="PersistenceTagKeys.Operation"/> (plus
/// their OpenTelemetry-semantic-convention mirrors <see cref="PersistenceTagKeys.DbCollectionName"/>/
/// <see cref="PersistenceTagKeys.DbOperationName"/>) are set at span start;
/// <see cref="PersistenceTagKeys.Outcome"/> is set to <c>"success"</c>, <c>"failure"</c>, or
/// <c>"canceled"</c> at completion (plus <see cref="WellKnownTagKeys.ErrorType"/> and
/// <see cref="ActivityStatusCode.Error"/> on a genuine failure — NEVER for a caller-requested
/// cancellation, which is expected control flow, not an error). Never a raw SQL parameter
/// value, entity property value, or tenant/user identifier in any tag — mirrors the
/// <c>cache.key_prefix</c>-never-full-key precedent from <c>02.Caching</c>.
/// </remarks>
internal static class RepositoryTracing
{
    /// <summary>Traces a <see cref="Task{TResult}"/>-returning repository operation.</summary>
    public static async Task<TResult> ExecuteTracedAsync<TAggregate, TResult>(
        string operationName,
        Func<Task<TResult>> operation)
    {
        using var activity = StartActivity<TAggregate>(operationName);

        try
        {
            var result = await operation().ConfigureAwait(false);
            SetSuccess(activity);
            return result;
        }
        catch (Exception ex)
        {
            SetOutcome(activity, ex);
            throw;
        }
    }

    /// <summary>Traces a <see cref="Task"/>-returning (no return value) repository operation.</summary>
    public static async Task ExecuteTracedAsync<TAggregate>(
        string operationName,
        Func<Task> operation)
    {
        using var activity = StartActivity<TAggregate>(operationName);

        try
        {
            await operation().ConfigureAwait(false);
            SetSuccess(activity);
        }
        catch (Exception ex)
        {
            SetOutcome(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Traces an <see cref="IAsyncEnumerable{TResult}"/>-returning streaming repository operation —
    /// the span wraps the FULL enumeration (starts before the first yield, ends after the last).
    /// </summary>
    /// <remarks>
    /// <strong>Early-disposal fix:</strong> when the caller stops enumerating before
    /// exhaustion (e.g. an <c>await foreach</c> with a <c>break</c>), the iterator's own
    /// <see cref="IAsyncDisposable.DisposeAsync"/> path runs WITHOUT ever reaching code placed after
    /// the enumeration loop in a plain iterator method — the pre-W2 version left the span outcome
    /// untagged in that case. The whole body now runs inside <see langword="try"/>/<see langword="finally"/>
    /// so the span is always finalized (as "success" unless a genuine exception was already recorded)
    /// regardless of how the enumeration ends.
    /// </remarks>
    public static async IAsyncEnumerable<TResult> ExecuteTracedStreamAsync<TAggregate, TResult>(
        string operationName,
        IAsyncEnumerable<TResult> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity<TAggregate>(operationName);
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        var outcomeSet = false;

        try
        {
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        SetOutcome(activity, ex);
                        outcomeSet = true;
                        throw;
                    }

                    if (!moved)
                        break;

                    yield return enumerator.Current;
                }
            }
        }
        finally
        {
            if (!outcomeSet)
                SetSuccess(activity);
        }
    }

    private static Activity? StartActivity<TAggregate>(string operationName)
    {
        var activity = PersistenceActivitySource.Source.StartActivity(
            $"{typeof(TAggregate).Name}.{operationName}", ActivityKind.Internal);

        activity?.SetTag(PersistenceTagKeys.AggregateType, typeof(TAggregate).Name);
        activity?.SetTag(PersistenceTagKeys.Operation, operationName);
        activity?.SetTag(PersistenceTagKeys.DbCollectionName, typeof(TAggregate).Name);
        activity?.SetTag(PersistenceTagKeys.DbOperationName, operationName);

        return activity;
    }

    private static void SetSuccess(Activity? activity) =>
        activity?.SetTag(PersistenceTagKeys.Outcome, "success");

    // Dispatches to SetCanceled/SetFailure — a caller-requested cancellation is expected control
    // flow, never an error.
    private static void SetOutcome(Activity? activity, Exception ex)
    {
        if (ex is OperationCanceledException)
            SetCanceled(activity);
        else
            SetFailure(activity, ex);
    }

    private static void SetCanceled(Activity? activity) =>
        activity?.SetTag(PersistenceTagKeys.Outcome, "canceled");

    private static void SetFailure(Activity? activity, Exception ex)
    {
        activity?.SetTag(PersistenceTagKeys.Outcome, "failure");
        activity?.SetTag(WellKnownTagKeys.ErrorType, ex.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
