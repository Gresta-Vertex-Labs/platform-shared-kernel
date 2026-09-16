namespace SharedKernel.Application.Behaviors.Commands;

/// <summary>
/// The scoped implementation of <see cref="ICommandScope"/>, driven exclusively by
/// <see cref="CommandScopeBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// A stack of callback frames, one pushed per <see cref="Enter"/> call. The nesting depth is
/// <c>_frames.Count</c>: zero means no command is executing, one means the outermost command is
/// executing, and more than one means a nested <c>ISender.Send</c> call is in flight.
/// </remarks>
internal sealed class CommandScope : ICommandScope
{
    private readonly Stack<List<Func<CancellationToken, Task>>> _frames = new();

    /// <inheritdoc />
    public bool IsActive => _frames.Count > 0;

    /// <inheritdoc />
    public bool IsNested => _frames.Count > 1;

    /// <inheritdoc />
    public void OnCompleted(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_frames.Count == 0)
        {
            throw new InvalidOperationException(
                "ICommandScope.OnCompleted can only be called while a command is executing.");
        }

        _frames.Peek().Add(callback);
    }

    /// <summary>Pushes a new, empty callback frame — called once per command dispatch, including nested ones.</summary>
    internal void Enter() => _frames.Push([]);

    /// <summary>
    /// Pops the current frame, always resetting depth by exactly one regardless of outcome.
    /// </summary>
    /// <param name="succeeded">
    /// <see langword="true"/> when the just-completed command succeeded; <see langword="false"/>
    /// when it failed or faulted, in which case the frame's callbacks are discarded.
    /// </param>
    /// <returns>
    /// The callbacks to run now — non-empty only when the popped frame belonged to the outermost
    /// command and it succeeded. A nested command's successful callbacks are merged into its
    /// parent frame instead of being returned.
    /// </returns>
    internal IReadOnlyList<Func<CancellationToken, Task>> Exit(bool succeeded)
    {
        var frame = _frames.Pop();

        if (!succeeded)
            return [];

        if (_frames.Count > 0)
        {
            _frames.Peek().AddRange(frame);
            return [];
        }

        return frame;
    }
}
