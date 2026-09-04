using Grpc.Core;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Static factory producing a <see cref="Grpc.Core.ServerCallContext"/> stub for testing gRPC
/// interceptors and service implementations in isolation, without a live channel.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <see cref="Grpc.Core.Testing.TestServerCallContext"/>. After the call completes, inspect
/// <see cref="Grpc.Core.Testing.TestServerCallContext.ResponseTrailers"/> /
/// <see cref="Grpc.Core.Testing.TestServerCallContext.Status"/> for post-execution metadata.
/// </para>
/// <para>
/// <b>NAMESPACE NOTE (added P-470/WO-074):</b> every reference to the third-party <c>Grpc.Core</c>
/// namespace in this file is fully qualified as <c>global::Grpc.Core</c> — this package's own
/// sibling folder <c>Grpc/</c> (mapped to <c>SharedKernel.Testing.Grpc</c>) makes the bare
/// <c>Grpc.Core</c> reference ambiguous from ANY file under <c>SharedKernel.Testing.*</c>, not only
/// files inside the <c>Grpc/</c> folder itself — C#'s namespace lookup walks up the CURRENT file's
/// own namespace chain (here, <c>SharedKernel.Testing.Communication</c> → <c>SharedKernel.Testing</c>)
/// checking for a nested <c>Grpc</c> namespace at each level before falling back to the global one,
/// and <c>SharedKernel.Testing.Grpc</c> now matches at the <c>SharedKernel.Testing</c> level. This
/// is a project-wide gotcha, not specific to this file — any FUTURE file anywhere under
/// <c>SharedKernel.Testing.*</c> that references <c>Grpc.Core</c>/<c>Grpc.Core.Testing</c> unqualified
/// must use <c>global::Grpc.Core</c> instead, exactly like the pre-existing <c>GreenDonut</c>
/// <c>Result&lt;T&gt;</c>/<c>Error</c> ambiguity this project already documents.
/// </para>
/// </remarks>
public static class TestServerCallContext
{
    /// <summary>
    /// Creates a <see cref="Grpc.Core.ServerCallContext"/> stub for use in interceptor/service tests.
    /// </summary>
    /// <param name="requestHeaders">Inbound request metadata. Defaults to empty metadata.</param>
    /// <param name="method">The gRPC method name. Defaults to <c>"test-method"</c>.</param>
    /// <param name="host">The host string. Defaults to <c>"localhost"</c>.</param>
    /// <param name="deadline">The call deadline. Defaults to <see cref="DateTime.MaxValue"/> (no deadline).</param>
    /// <param name="cancellationToken">The cancellation token observed by the call. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A new <see cref="ServerCallContext"/> stub.</returns>
    public static ServerCallContext Create(
        Metadata? requestHeaders = null,
        string method = "test-method",
        string host = "localhost",
        DateTime? deadline = null,
        CancellationToken cancellationToken = default) =>
        global::Grpc.Core.Testing.TestServerCallContext.Create(
            method: method,
            host: host,
            deadline: deadline ?? DateTime.MaxValue,
            requestHeaders: requestHeaders ?? new Metadata(),
            cancellationToken: cancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => null,
            writeOptionsSetter: _ => { });
}
