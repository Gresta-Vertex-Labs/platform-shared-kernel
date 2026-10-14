using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Shared distributed-tracing helper for <c>EfRepository{TAggregate,TId}</c>/<c>EfReadRepository{TAggregate,TId}</c>
/// repository operations.
/// </summary>
/// <remarks>
/// <para>
/// The span is named <c>persistence {operation}</c> with the operation in lowercase snake case and without the
/// <c>Async</c> suffix (<c>persistence get_by_id</c>), kind <see cref="ActivityKind.Internal"/>: the <c>Client</c>
/// span of the database call is the one Npgsql emits, and a repository call is in-process work around it. The
/// aggregate and operation are tags (<see cref="PersistenceTagKeys"/>), never part of the name.
/// </para>
/// <para>
/// The outcome (<see cref="WellKnownTagKeys.Outcome"/>) is the string the shared recorder returns:
/// <c>success</c>, <c>exception</c> (status Error, <c>error.type</c>, <c>error.code</c> for a classified exception and
/// the exception event) or <c>canceled</c> (never an error). No SQL parameter, entity value or tenant or user
/// identifier is ever a tag.
/// </para>
/// </remarks>
internal static class RepositoryTracing
{
    private const string AsyncSuffix = "Async";
    private const string SpanNamePrefix = "persistence ";

    private static readonly ConcurrentDictionary<string, string> SpanNames = new(StringComparer.Ordinal);

    /// <summary>Traces a <see cref="Task{TResult}"/>-returning repository operation.</summary>
    public static async Task<TResult> ExecuteTracedAsync<TAggregate, TResult>(
        string operationName,
        Func<Task<TResult>> operation)
    {
        using var activity = StartActivity<TAggregate>(operationName);

        try
        {
            var result = await operation().ConfigureAwait(false);
            Succeed(activity);
            return result;
        }
        catch (Exception ex)
        {
            Fail(activity, ex);
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
            Succeed(activity);
        }
        catch (Exception ex)
        {
            Fail(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Traces an <see cref="IAsyncEnumerable{TResult}"/>-returning streaming repository operation: the span wraps the
    /// full enumeration (starts before the first yield, ends after the last).
    /// </summary>
    /// <remarks>
    /// <strong>Early disposal:</strong> when the caller stops enumerating before exhaustion (an <c>await foreach</c>
    /// with a <c>break</c>), the iterator's own dispose path runs without reaching code placed after the loop. The whole
    /// body therefore runs inside <see langword="try"/>/<see langword="finally"/>, so the span is always finalized (as
    /// <c>success</c> unless an exception was already recorded) however the enumeration ends.
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
                        Fail(activity, ex);
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
                Succeed(activity);
        }
    }

    /// <summary>The span name of <paramref name="operationName"/> (<c>GetByIdAsync</c> becomes <c>persistence get_by_id</c>), derived once.</summary>
    internal static string SpanName(string operationName) =>
        SpanNames.GetOrAdd(operationName, static name => SpanNamePrefix + ToSnakeCase(name));

    private static string ToSnakeCase(string operationName)
    {
        var name = operationName.EndsWith(AsyncSuffix, StringComparison.Ordinal)
            ? operationName[..^AsyncSuffix.Length]
            : operationName;

        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static Activity? StartActivity<TAggregate>(string operationName)
    {
        var activity = PersistenceActivitySource.Source.StartActivity(SpanName(operationName), ActivityKind.Internal);

        activity?.SetTag(PersistenceTagKeys.AggregateType, typeof(TAggregate).Name);
        activity?.SetTag(PersistenceTagKeys.Operation, operationName);
        activity?.SetTag(PersistenceTagKeys.DbCollectionName, typeof(TAggregate).Name);
        activity?.SetTag(PersistenceTagKeys.DbOperationName, operationName);

        return activity;
    }

    private static void Succeed(Activity? activity) =>
        activity?.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success);

    // The shared recorder sets status, error.type, error.code and the exception event, and treats a caller-requested
    // cancellation as expected control flow: no error status, outcome "canceled".
    private static void Fail(Activity? activity, Exception exception) =>
        activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(exception));
}
