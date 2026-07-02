namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// Determines how the fire-and-forget dispatcher behaves when the bounded channel is full.
/// </summary>
public enum FireAndForgetRejectionPolicy
{
    /// <summary>
    /// Drops the command silently and logs a <c>Warning</c> — the caller receives no exception.
    /// Use when throughput continuity is more important than guaranteed delivery of every command.
    /// </summary>
    DropAndLog,

    /// <summary>
    /// Blocks the caller until channel capacity is available or the <see cref="CancellationToken"/>
    /// is cancelled. Use when every command must be accepted, at the cost of caller latency under load.
    /// </summary>
    Block
}

/// <summary>
/// Configuration options for the fire-and-forget command dispatch infrastructure.
/// </summary>
public sealed class FireAndForgetOptions
{
    /// <summary>
    /// Gets or sets the maximum number of fire-and-forget commands that can queue up in the
    /// bounded channel before the <see cref="RejectionPolicy"/> takes effect.
    /// </summary>
    /// <value>Defaults to <c>1000</c>.</value>
    public int Capacity { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how the dispatcher behaves when the bounded channel is full.
    /// </summary>
    /// <value>Defaults to <see cref="FireAndForgetRejectionPolicy.DropAndLog"/>.</value>
    public FireAndForgetRejectionPolicy RejectionPolicy { get; set; } = FireAndForgetRejectionPolicy.DropAndLog;
}
