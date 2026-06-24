using Grpc.Core;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Static factory producing a <see cref="Grpc.Core.ServerCallContext"/> stub for testing gRPC
/// interceptors and service implementations in isolation, without a live channel.
/// </summary>
/// <remarks>
/// Wraps <see cref="Grpc.Core.Testing.TestServerCallContext"/>. After the call completes, inspect
/// <see cref="Grpc.Core.Testing.TestServerCallContext.ResponseTrailers"/> /
/// <see cref="Grpc.Core.Testing.TestServerCallContext.Status"/> for post-execution metadata.
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
        Grpc.Core.Testing.TestServerCallContext.Create(
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
