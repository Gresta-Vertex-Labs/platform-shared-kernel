using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Application.Streaming;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline.Streaming;

/// <summary>
/// Gives a streaming query the span, the duration and item metrics and the log that a unary request gets from the
/// tracing, logging and metrics behaviors.
/// </summary>
/// <typeparam name="TRequest">The streaming query type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
/// <remarks>
/// <para>
/// Registered first in the stream chain, so it is outermost and a denial by
/// <c>StreamAuthorizationBehavior</c> is observed too. One behavior decides one outcome for the span, the timer, the
/// log and the metrics, so they cannot disagree.
/// </para>
/// <para>
/// An <see cref="IAsyncEnumerable{T}"/> is lazy: the span, the timer and the log start at the first
/// <c>MoveNextAsync</c> and end exactly once, at completion, at a fault or at dispose. A stream that is never
/// enumerated records nothing. Ran to the end is <c>success</c>; the caller's cancellation, an
/// <see cref="OperationCanceledException"/> or a dispose before the end is <c>canceled</c> (status stays unset, no
/// error log); any other exception is <c>exception</c> and is rethrown as the same instance. There is no slow-request
/// warning: a stream's duration is paced by its consumer.
/// </para>
/// <para>
/// A C# iterator cannot <c>yield</c> inside a <c>try</c> that has a <c>catch</c>, so the enumerator is driven through
/// helpers that catch and return the fault, the <c>yield return</c> stays outside them, and the <c>finally</c> tells a
/// dispose before the end from a completion with a flag.
/// </para>
/// </remarks>
internal sealed class StreamObservabilityBehavior<TRequest, TResponse>(
    ApplicationMetrics metrics,
    ILogger<StreamObservabilityBehavior<TRequest, TResponse>> logger)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamQuery<TResponse>
{
    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
        => Observe(next, cancellationToken);

    private async IAsyncEnumerable<TResponse> Observe(
        StreamHandlerContinuation<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            ApplicationSpanNames.Stream,
            ActivityKind.Internal);
        activity?.SetTag(ApplicationTagKeys.RequestType, requestType);
        activity?.SetTag(ApplicationTagKeys.RequestKind, RequestKind.Stream);

        long items = 0;
        var completed = false;
        Exception? fault = null;
        IAsyncEnumerator<TResponse>? enumerator = null;

        try
        {
            (enumerator, fault) = Open(next, cancellationToken);

            while (fault is null)
            {
                var (hasNext, moveFault) = await MoveNextAsync(enumerator!, activity).ConfigureAwait(false);
                if (moveFault is not null)
                {
                    fault = moveFault;
                    break;
                }

                if (!hasNext)
                {
                    completed = true;
                    break;
                }

                items++;
                yield return enumerator!.Current;
            }
        }
        finally
        {
            var disposeFault = await DisposeEnumeratorAsync(enumerator).ConfigureAwait(false);
            var effective = fault ?? disposeFault;

            Complete(activity, requestType, startTimestamp, items, completed && effective is null, effective);

            // A fault on dispose surfaces when nothing else did; a fault from the stream is rethrown below.
            if (fault is null && disposeFault is not null)
                ExceptionDispatchInfo.Capture(disposeFault).Throw();
        }

        if (fault is not null)
            ExceptionDispatchInfo.Capture(fault).Throw();
    }

    private static (IAsyncEnumerator<TResponse>? Enumerator, Exception? Fault) Open(
        StreamHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return (next().GetAsyncEnumerator(cancellationToken), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static async ValueTask<(bool HasNext, Exception? Fault)> MoveNextAsync(
        IAsyncEnumerator<TResponse> enumerator,
        Activity? activity)
    {
        // The iterator resumes on the caller's execution context, so the stream span is made current again for the
        // handler's own work. The assignment is scoped to this async method and never reaches the consumer.
        if (activity is not null)
            Activity.Current = activity;

        try
        {
            return (await enumerator.MoveNextAsync().ConfigureAwait(false), null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    private static async ValueTask<Exception?> DisposeEnumeratorAsync(IAsyncEnumerator<TResponse>? enumerator)
    {
        if (enumerator is null)
            return null;

        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void Complete(
        Activity? activity,
        string requestType,
        long startTimestamp,
        long items,
        bool ranToTheEnd,
        Exception? fault)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        string outcome;
        string? errorType = null;

        if (fault is not null)
        {
            outcome = activity.RecordException(fault);
            if (outcome == WellKnownOutcomes.Exception)
                errorType = fault.GetType().FullName ?? fault.GetType().Name;
        }
        else
        {
            // Ran to the end is a success; a dispose before the end is the consumer walking away.
            outcome = ranToTheEnd ? WellKnownOutcomes.Success : WellKnownOutcomes.Canceled;
        }

        activity?.SetTag(WellKnownTagKeys.Outcome, outcome);

        var tags = new TagList
        {
            { ApplicationTagKeys.RequestType, requestType },
            { ApplicationTagKeys.RequestKind, RequestKind.Stream },
            { WellKnownTagKeys.Outcome, outcome },
        };
        if (errorType is not null)
            tags.Add(WellKnownTagKeys.ErrorType, errorType);

        metrics.RecordRequestDuration(elapsed.TotalSeconds, tags);

        var itemTags = new TagList
        {
            { ApplicationTagKeys.RequestType, requestType },
            { WellKnownTagKeys.Outcome, outcome },
        };
        metrics.RecordStreamItems(items, itemTags);

        Log(requestType, items, elapsed.TotalMilliseconds, outcome, fault);
    }

    private void Log(string requestType, long items, double elapsedMilliseconds, string outcome, Exception? fault)
    {
        switch (outcome)
        {
            case WellKnownOutcomes.Success:
                PipelineLog.StreamCompleted(logger, requestType, items, elapsedMilliseconds);
                break;

            case WellKnownOutcomes.Canceled:
                PipelineLog.StreamCanceled(logger, requestType, items, elapsedMilliseconds);
                break;

            default:
                if (fault is IHasError { Error: { } error })
                {
                    PipelineLog.StreamFailedClassified(
                        logger,
                        requestType,
                        error.Type.ToString(),
                        error.Code,
                        items,
                        elapsedMilliseconds,
                        fault);
                }
                else
                {
                    PipelineLog.StreamFailed(logger, requestType, items, elapsedMilliseconds, fault!);
                }

                break;
        }
    }
}
