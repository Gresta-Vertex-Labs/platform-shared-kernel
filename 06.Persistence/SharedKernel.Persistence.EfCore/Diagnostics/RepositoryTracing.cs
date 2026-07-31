using System.Diagnostics;
using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Shared distributed-tracing helper for <c>EfRepository{TAggregate,TId}</c>/
/// <c>EfReadRepository{TAggregate,TId}</c> repository operations (WO-051/P-319).
/// </summary>
/// <remarks>
/// Avoids duplicating the start/tag/try-catch/finish boilerplate across every repository method.
/// Span name is <c>"{typeof(TAggregate).Name}.{operationName}"</c>, <see cref="ActivityKind.Client"/>.
/// <see cref="PersistenceTagKeys.AggregateType"/>/<see cref="PersistenceTagKeys.Operation"/> are set
/// at span start; <see cref="PersistenceTagKeys.Outcome"/> is set to <c>"success"</c> or
/// <c>"failure"</c> at completion (plus <see cref="WellKnownTagKeys.ErrorType"/> and
/// <see cref="ActivityStatusCode.Error"/> on failure). Never a raw SQL parameter value, entity
/// property value, or tenant/user identifier in any tag — mirrors the
/// <c>cache.key_prefix</c>-never-full-key precedent from <c>02.Caching</c>'s own P-304.
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
            SetFailure(activity, ex);
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
            SetFailure(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Traces an <see cref="IAsyncEnumerable{TResult}"/>-returning streaming repository operation —
    /// the span wraps the FULL enumeration (starts before the first yield, ends after the last).
    /// </summary>
    public static async IAsyncEnumerable<TResult> ExecuteTracedStreamAsync<TAggregate, TResult>(
        string operationName,
        IAsyncEnumerable<TResult> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = StartActivity<TAggregate>(operationName);
        var enumerator = source.GetAsyncEnumerator(ct);

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
                    SetFailure(activity, ex);
                    throw;
                }

                if (!moved)
                    break;

                yield return enumerator.Current;
            }
        }

        SetSuccess(activity);
    }

    private static Activity? StartActivity<TAggregate>(string operationName)
    {
        var activity = PersistenceActivitySource.Source.StartActivity(
            $"{typeof(TAggregate).Name}.{operationName}", ActivityKind.Client);

        activity?.SetTag(PersistenceTagKeys.AggregateType, typeof(TAggregate).Name);
        activity?.SetTag(PersistenceTagKeys.Operation, operationName);

        return activity;
    }

    private static void SetSuccess(Activity? activity) =>
        activity?.SetTag(PersistenceTagKeys.Outcome, "success");

    private static void SetFailure(Activity? activity, Exception ex)
    {
        activity?.SetTag(PersistenceTagKeys.Outcome, "failure");
        activity?.SetTag(WellKnownTagKeys.ErrorType, ex.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
