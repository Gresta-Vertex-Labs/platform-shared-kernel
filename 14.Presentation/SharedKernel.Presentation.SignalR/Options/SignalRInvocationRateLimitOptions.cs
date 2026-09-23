namespace SharedKernel.Presentation.SignalR.Options;

/// <summary>Limits how often one connection may invoke hub methods.</summary>
/// <remarks>
/// <para>
/// Each connection gets a token bucket of <see cref="PermitLimit"/> tokens, refilled at <see cref="PermitLimit"/> per
/// <see cref="Window"/>: a connection may burst up to <see cref="PermitLimit"/> invocations and then sustain that many
/// per <see cref="Window"/>. One connection exhausting its bucket never slows another.
/// </para>
/// <para>
/// A refused invocation never reaches the hub method; the client receives a <c>HubException</c> with the message
/// <c>rate_limit.exceeded: Too many requests.</c> and the refusal is logged at Warning. All buckets live in one
/// partitioned limiter keyed by connection id, with one replenishment timer for the whole server; the bucket of a
/// closed connection is dropped once it has refilled and stayed idle.
/// </para>
/// </remarks>
public sealed class SignalRInvocationRateLimitOptions
{
    /// <summary>
    /// Gets or sets the number of invocations one connection may make per <see cref="Window"/>, which is also the
    /// largest burst. <see langword="null"/> (the default) turns the limit off; otherwise at least 1.
    /// </summary>
    public int? PermitLimit { get; set; }

    /// <summary>
    /// Gets or sets the period over which <see cref="PermitLimit"/> invocations are allowed. Defaults to one second;
    /// must be greater than zero.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);
}
