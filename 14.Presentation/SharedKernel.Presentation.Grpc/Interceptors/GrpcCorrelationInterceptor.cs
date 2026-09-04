using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that extracts (or generates) the inbound correlation identifier for
/// every gRPC call — the receiving half of what
/// <c>SharedKernel.Communication.Grpc.Interceptors.CorrelationTracingInterceptor</c> already
/// sends on the outbound (client) side, and the gRPC counterpart to
/// <c>SharedKernel.Presentation.WebApi.Middleware.CorrelationIdMiddleware</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reads the <see cref="WellKnownHeaders.CorrelationId"/> gRPC metadata key from
/// <see cref="ServerCallContext.RequestHeaders"/> — the identical key
/// <c>CorrelationTracingInterceptor</c> writes, proven by a round-trip test — and generates
/// <c>Guid.NewGuid("N")</c> when absent or whitespace. gRPC metadata key casing is normalized
/// internally by <see cref="Metadata"/>, so no behavioral change results from this key's
/// uppercase-hyphenated literal form.
/// </para>
/// <para>
/// Stores the resolved value in <see cref="ServerCallContext.UserState"/> under
/// <see cref="ItemsKey"/> — the gRPC per-call analogue of <c>HttpContext.Items</c>/
/// <c>HubCallerContext.Items</c> — and calls
/// <see cref="Activity.SetBaggage(string, string?)"/> on <see cref="Activity.Current"/> under
/// <see cref="WellKnownBaggageKeys.CorrelationId"/>, identically to
/// <c>CorrelationIdMiddleware</c>'s behavior, so <c>13.ServiceDefaults</c>'s
/// <c>BaggageLogRecordProcessor</c> story covers gRPC with zero <c>ProjectReference</c> on
/// <c>13.ServiceDefaults</c>.
/// </para>
/// <para>
/// Overrides all four server interceptor methods, since gRPC has three streaming call shapes in
/// addition to unary that equally need correlation propagation.
/// </para>
/// </remarks>
public sealed class GrpcCorrelationInterceptor : Interceptor
{
    /// <summary>The gRPC metadata key carrying the correlation identifier (<c>"X-Correlation-Id"</c>).</summary>
    public const string MetadataKey = WellKnownHeaders.CorrelationId;

    /// <summary>The <see cref="ServerCallContext.UserState"/> key the resolved correlation identifier is stored under.</summary>
    public const string ItemsKey = "CorrelationId";

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, responseStream, context);
    }

    private static void Enrich(ServerCallContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.UserState[ItemsKey] = correlationId;
        Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
    }

    private static string ResolveCorrelationId(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(MetadataKey);

        return !string.IsNullOrWhiteSpace(value) ? value : Guid.NewGuid().ToString("N");
    }
}
