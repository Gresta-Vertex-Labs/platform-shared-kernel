namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// Internal, non-public ambient marker that identifies a MediatR dispatch as originating from
/// <see cref="FireAndForgetBackgroundConsumer"/>'s own internal <c>ISender.Send</c> call, as
/// opposed to an external caller mistakenly bypassing <see cref="IFireAndForgetDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves (WO-039, P-238):</b> <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>
/// runs against every <c>ISender.Send</c> call for an <see cref="Messaging.IFireAndForgetCommand"/> —
/// including <see cref="FireAndForgetBackgroundConsumer"/>'s own internal dispatch, which traverses
/// the identical MediatR pipeline against the identical service provider as an external caller's
/// misuse. Without a way to distinguish the two, the guard rejected the consumer's own dispatch
/// every time, making the entire fire-and-forget feature non-functional as documented.
/// </para>
/// <para>
/// <b>Why <see cref="AsyncLocal{T}"/>:</b> the marker's backing field is static-declared but its
/// <em>value</em> flows per async call chain, not as shared global state — the same ambient-context
/// shape already used by the BCL's own <see cref="System.Diagnostics.Activity.Current"/> and
/// <see cref="System.Threading.ExecutionContext"/>. It carries zero business data and is one of the
/// exactly two sanctioned static-state exceptions in this domain (see <c>05.Application/CLAUDE.md</c>
/// "Hard violations" — the other being <c>ApplicationDiagnostics</c>'s <c>Meter</c>/<c>Histogram</c>
/// pair).
/// </para>
/// <para>
/// <b>Unspoofable by design:</b> this type has no public surface. <see cref="EnterTrustedDispatch"/>
/// is <see langword="internal"/>, called only from <see cref="FireAndForgetBackgroundConsumer"/>'s
/// own dispatch call, and the returned scope is guaranteed to reset the marker on disposal — even
/// when the wrapped dispatch throws — via a <see langword="try"/>/<see langword="finally"/> inside
/// the scope's <see cref="IDisposable.Dispose"/>. Consuming-service code has no way to set or observe
/// this marker, so it cannot be used to defeat <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>'s
/// external-misuse protection.
/// </para>
/// </remarks>
internal static class FireAndForgetDispatchContext
{
    private static readonly AsyncLocal<bool> IsTrustedDispatch = new();

    /// <summary>
    /// Gets whether the current async flow is executing inside
    /// <see cref="FireAndForgetBackgroundConsumer"/>'s own trusted internal dispatch.
    /// </summary>
    internal static bool IsTrusted => IsTrustedDispatch.Value;

    /// <summary>
    /// Marks the current async flow as a trusted internal dispatch for the scope of the returned
    /// <see cref="IDisposable"/>. The marker is reset to <see langword="false"/> when the scope is
    /// disposed, guaranteed even if the wrapped call throws.
    /// </summary>
    /// <returns>A disposable scope; dispose it (typically via <see langword="using"/>) to reset the marker.</returns>
    internal static IDisposable EnterTrustedDispatch()
    {
        IsTrustedDispatch.Value = true;
        return new TrustedDispatchScope();
    }

    private sealed class TrustedDispatchScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            IsTrustedDispatch.Value = false;
        }
    }
}
