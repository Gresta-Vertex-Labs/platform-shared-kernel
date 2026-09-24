namespace SharedKernel.Application;

/// <summary>
/// A per-DI-scope seam tracking whether a command is currently executing, whether it is nested
/// inside another command's handler, and a registry of work to run after the outermost command
/// succeeds.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped. Because MediatR dispatches a nested <c>ISender.Send</c> call from inside a
/// handler through the same DI scope as the outer command, a single <see cref="ICommandScope"/>
/// instance observes the entire nesting depth for one logical command execution.
/// </para>
/// <para>
/// <c>CommandScopeBehavior</c> is the sole owner of entering/exiting a frame; application code only
/// ever reads <see cref="IsActive"/>/<see cref="IsNested"/> or calls <see cref="OnCompleted"/> from
/// inside a command handler.
/// </para>
/// </remarks>
public interface ICommandScope
{
    /// <summary>Gets a value indicating whether a command is currently executing in this DI scope.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether the currently-executing command was sent from inside another
    /// command's handler (i.e. this is not the outermost command in the current DI scope).
    /// </summary>
    bool IsNested { get; }

    /// <summary>
    /// Registers <paramref name="callback"/> to run once the <b>outermost</b> command in the
    /// current DI scope succeeds — after that command's <c>next()</c> call, which includes its
    /// transaction commit, has already returned.
    /// </summary>
    /// <param name="callback">The work to run after the outermost command succeeds.</param>
    /// <remarks>
    /// When called from a nested command's handler, <paramref name="callback"/> is merged into the
    /// outer command's own registry rather than run immediately — it still only runs once, after
    /// the outermost command succeeds. When the outermost (or the nesting) command instead fails or
    /// throws, every callback registered for that execution is discarded, never run.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No command is currently executing in this DI scope (<see cref="IsActive"/> is
    /// <see langword="false"/>).
    /// </exception>
    void OnCompleted(Func<CancellationToken, Task> callback);
}
