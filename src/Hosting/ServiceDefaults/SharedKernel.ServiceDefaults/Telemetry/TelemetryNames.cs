namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// The telemetry names <see cref="TelemetryExtensions.AddSharedKernelTelemetry"/> subscribes to, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kernel names are never copied here.</b> A kernel <c>ActivitySource</c> or <c>Meter</c> is named
/// <c>SharedKernel.{Capability}[.{Sub}]</c> by its owner, and the single <see cref="SharedKernelWildcard"/>
/// subscribes every one of them, including ones created after the host started. The OpenTelemetry SDK turns a
/// <c>*</c> into a pattern: <c>SharedKernel.*</c> matches <c>SharedKernel.Messaging</c> and
/// <c>SharedKernel.Persistence.EfCore.Auditing</c>, but neither <c>SharedKernel</c> nor
/// <c>OtherVendor.SharedKernel.X</c>. A kernel source outside the <c>SharedKernel.</c> prefix is not exported; that is
/// a defect of its owner, not something this package works around.
/// </para>
/// <para>
/// <b>Framework names.</b> The ASP.NET Core Server SignalR hub activities (<c>OnConnected</c>, <c>InvocationIn</c>,
/// <c>OnDisconnected</c>) come from one <c>ActivitySource</c>, <see cref="SignalRServerSource"/>, read from
/// <c>Microsoft.AspNetCore.SignalR.Core</c> of the .NET 10 shared framework. The HttpClient metrics
/// (<c>http.client.request.duration</c> and friends) come from <c>AddHttpClientInstrumentation()</c>, which subscribes
/// the <c>System.Net.Http</c> and <c>System.Net.NameResolution</c> meters itself. The SignalR transport meter
/// (<c>Microsoft.AspNetCore.Http.Connections</c>) is already subscribed by the ASP.NET Core metrics instrumentation.
/// </para>
/// <para>
/// Third-party names (<c>MassTransit</c>, <c>Npgsql</c>, <c>Polly</c>, the <c>Temporalio.*</c> sources) stay with the
/// <c>With{X}Telemetry()</c> extension that wires them.
/// </para>
/// </remarks>
internal static class TelemetryNames
{
    /// <summary>The prefix every kernel source and meter name starts with.</summary>
    internal const string SharedKernelPrefix = "SharedKernel.";

    /// <summary>The wildcard subscribing every <c>SharedKernel.*</c> source and meter.</summary>
    internal const string SharedKernelWildcard = SharedKernelPrefix + "*";

    /// <summary>The ASP.NET Core SignalR server <c>ActivitySource</c> (.NET 10 shared framework).</summary>
    internal const string SignalRServerSource = "Microsoft.AspNetCore.SignalR.Server";
}
