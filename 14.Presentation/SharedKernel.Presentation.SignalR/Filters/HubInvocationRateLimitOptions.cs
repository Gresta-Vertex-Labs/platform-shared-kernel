using Microsoft.AspNetCore.SignalR;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Configures per-connection hub-method invocation rate limiting and argument-payload shape
/// validation applied by <see cref="HubInvocationRateLimitFilter"/>.
/// </summary>
/// <remarks>
/// Every check on this type defaults to disabled — a host that never configures this options type
/// (via <c>AddSharedKernelSignalR</c>'s <c>configureRateLimit</c> callback) sees
/// <see cref="HubInvocationRateLimitFilter"/> register but always no-op, carrying zero observable
/// behavior change.
/// </remarks>
public sealed class HubInvocationRateLimitOptions
{
    /// <summary>
    /// Gets or sets the maximum number of hub-method invocations permitted per connection per
    /// <see cref="Window"/>. <see langword="null"/> (the default) disables invocation rate
    /// limiting entirely.
    /// </summary>
    public int? PermitLimit { get; set; }

    /// <summary>
    /// Gets or sets the replenishment window <see cref="PermitLimit"/> applies over. Defaults to
    /// one second. Only meaningful when <see cref="PermitLimit"/> is set.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum accepted length, in characters, of any <see cref="string"/>
    /// argument passed to a hub method invocation. <see langword="null"/> (the default) disables
    /// this check. Additive to and distinct from <c>HubOptions.MaximumReceiveMessageSize</c> — that
    /// caps the whole transport message; this validates individual argument values before the
    /// target method body executes.
    /// </summary>
    public int? MaxStringArgumentLength { get; set; }

    /// <summary>
    /// Gets a composable collection of additional argument-payload validators, each returning a
    /// caller-safe rejection message when the invocation's arguments fail a custom shape check, or
    /// <see langword="null"/> when the invocation is acceptable. Evaluated in order; the first
    /// non-null result short-circuits the invocation with a <see cref="HubException"/> carrying
    /// that message.
    /// </summary>
    public ICollection<Func<HubInvocationContext, string?>> ArgumentValidators { get; } = new List<Func<HubInvocationContext, string?>>();
}
