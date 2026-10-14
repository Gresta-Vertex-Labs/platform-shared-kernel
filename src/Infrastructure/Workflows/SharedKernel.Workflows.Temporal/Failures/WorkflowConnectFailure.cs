namespace SharedKernel.Workflows.Temporal.Failures;

/// <summary>
/// Recognises the exception the Temporal SDK throws when a lazily connecting client cannot reach the service.
/// </summary>
/// <remarks>
/// The SDK reports a failed connect as a plain <see cref="InvalidOperationException"/> whose message starts with
/// <c>Connection failed:</c> (verified against Temporalio 1.17.0 with an unreachable port); it is neither a
/// <c>TemporalException</c> nor an <c>RpcException</c>, so the status table cannot see it. It is a transport fault and
/// maps to <c>workflow.service_unavailable</c>. The message is matched by its fixed prefix only and never copied: it
/// carries the target host. Any other <see cref="InvalidOperationException"/> is a defect and propagates, and an SDK
/// message change makes the connect failure propagate too, which the real-environment test pins.
/// </remarks>
internal static class WorkflowConnectFailure
{
    private const string MessagePrefix = "Connection failed:";

    /// <summary>Whether <paramref name="exception"/> is the SDK's failed-connect exception.</summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> for a failed lazy connect.</returns>
    public static bool Is(Exception exception) =>
        exception is InvalidOperationException && exception.Message.StartsWith(MessagePrefix, StringComparison.Ordinal);
}
