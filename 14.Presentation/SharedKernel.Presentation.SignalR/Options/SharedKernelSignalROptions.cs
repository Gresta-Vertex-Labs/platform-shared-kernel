using SharedKernel.Configuration;

namespace SharedKernel.Presentation.SignalR.Options;

/// <summary>
/// Settings for the SignalR conventions set up by <see cref="SignalRHostBuilderExtensions.AddSharedKernelSignalR"/>,
/// bound from <c>SharedKernel:Presentation:SignalR</c> and validated at startup.
/// </summary>
/// <remarks>
/// The <c>configure</c> callback of <see cref="SignalRHostBuilderExtensions.AddSharedKernelSignalR"/> runs after
/// binding, so code can override configuration. SignalR's own settings (message size, keep-alive, timeouts) stay on
/// <c>HubOptions</c>, whose framework defaults this package leaves alone.
/// </remarks>
public sealed class SharedKernelSignalROptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section these settings bind from: <c>SharedKernel:Presentation:SignalR</c>.</summary>
    public static string SectionName => "SharedKernel:Presentation:SignalR";

    /// <summary>Gets the per-connection limit on hub method invocations. Off by default.</summary>
    public SignalRInvocationRateLimitOptions InvocationRateLimit { get; } = new();
}
