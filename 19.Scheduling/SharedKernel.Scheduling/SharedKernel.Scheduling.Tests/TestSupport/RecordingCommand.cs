using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Scheduling.Tests.TestSupport;

/// <summary>A void-returning command whose every dispatch is recorded by a <see cref="RecordingCommandRecorder"/>.</summary>
public sealed record RecordingCommand : ICommand;

/// <summary>MediatR handler bridging <see cref="RecordingCommand"/> to a test-scoped <see cref="RecordingCommandRecorder"/>.</summary>
public sealed class RecordingCommandHandler(RecordingCommandRecorder recorder) : IRequestHandler<RecordingCommand, Result>
{
    public Task<Result> Handle(RecordingCommand request, CancellationToken cancellationToken) =>
        recorder.HandleAsync(cancellationToken);
}

/// <summary>
/// Thread-safe recorder of every <see cref="RecordingCommand"/> dispatch, with hooks for controlled
/// concurrency (overlap tests) and outcome/cancellation behavior (command-bridge/cancellation tests).
/// </summary>
public sealed class RecordingCommandRecorder
{
    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _startTimestamps = [];
    private int _activeCount;

    /// <summary>
    /// Optional per-invocation hook, given the 1-based invocation index and the command's
    /// cancellation token. Awaited before the invocation completes — used to hold a specific
    /// invocation open (e.g. only the first) for deterministic overlap-window tests.
    /// </summary>
    public Func<int, CancellationToken, Task>? OnHandling { get; set; }

    /// <summary>Optional override for the returned <see cref="Result"/>. Defaults to <see cref="Result.Success"/>.</summary>
    public Func<Result>? ResultFactory { get; set; }

    /// <summary>The total number of invocations recorded so far.</summary>
    public int InvocationCount
    {
        get
        {
            lock (_gate)
                return _startTimestamps.Count;
        }
    }

    /// <summary>The highest number of concurrently-in-flight invocations observed so far.</summary>
    public int MaxConcurrentObserved { get; private set; }

    /// <summary>A snapshot of every invocation's start timestamp, in order.</summary>
    public IReadOnlyList<DateTimeOffset> StartTimestamps
    {
        get
        {
            lock (_gate)
                return [.. _startTimestamps];
        }
    }

    internal async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        int myIndex;
        lock (_gate)
        {
            _startTimestamps.Add(DateTimeOffset.UtcNow);
            myIndex = _startTimestamps.Count;
            _activeCount++;
            if (_activeCount > MaxConcurrentObserved)
            {
                MaxConcurrentObserved = _activeCount;
            }
        }

        try
        {
            if (OnHandling is not null)
            {
                await OnHandling(myIndex, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _activeCount--;
            }
        }

        return ResultFactory?.Invoke() ?? Result.Success();
    }
}
